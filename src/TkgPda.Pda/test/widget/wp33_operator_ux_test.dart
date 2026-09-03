import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:tkg_pda/app/app.dart';
import 'package:tkg_pda/app/app_controller.dart';
import 'package:tkg_pda/core/auth/session_memory.dart';
import 'package:tkg_pda/core/errors/api_problem.dart';
import 'package:tkg_pda/core/storage/device_code_store.dart';
import 'package:tkg_pda/data/models/pda_models.dart';

import '../support/fakes.dart';

void main() {
  testWidgets('logical container actions submit once without confirmation', (
    tester,
  ) async {
    final harness = await _operationHarness(tester);

    expect(find.text('1. Kasa Doldu'), findsOneWidget);
    expect(find.text('1. Kasa Boş'), findsOneWidget);
    expect(find.text('Tüm Kasaları Kaldır'), findsOneWidget);
    expect(find.byKey(const Key('direct-action-REMOVE_EMPTY')), findsNothing);

    await tester.tap(find.text('1. Kasa Doldu'));
    await tester.pumpAndSettle();

    expect(harness.repository.containerActionCount, 1);
    expect(harness.repository.lastCommand?.operation, 'REMOVE_FULL');
    expect(find.byKey(const Key('confirm-direct-transport')), findsNothing);
    harness.dispose();
  });

  testWidgets('container projection supports variable position counts', (
    tester,
  ) async {
    final repository = FakePdaRepository()
      ..containerPositionsValue = [
        ...FakePdaRepository().containerPositionsValue,
        const ContainerPosition(
          positionNumber: 2,
          label: '2. Kasa',
          fullAction: ContainerActionAvailability(
            enabled: true,
            status: 'READY',
            disabledCode: null,
            disabledMessage: null,
          ),
          emptyAction: ContainerActionAvailability(
            enabled: false,
            status: 'READY',
            disabledCode: 'CONTAINER_POSITION_OCCUPIED',
            disabledMessage: 'Bu konumda zaten bir kasa bulunuyor.',
          ),
          transportState: 'READY',
          queuePosition: null,
          etaSeconds: null,
          etaState: 'UNAVAILABLE',
        ),
      ];
    final harness = await _operationHarness(tester, repository: repository);

    await tester.drag(
      find.byKey(const Key('container-position-list')),
      const Offset(-260, 0),
    );
    await tester.pumpAndSettle();
    expect(find.text('2. Kasa Doldu'), findsOneWidget);
    expect(find.text('2. Kasa Boş'), findsOneWidget);
    harness.dispose();
  });

  testWidgets('ambiguous remove-all retry reuses the same idempotency key', (
    tester,
  ) async {
    final repository = FakePdaRepository()
      ..removeAllError = const AmbiguousMutationException(
        idempotencyKey: 'ambiguous-remove-all',
      );
    final harness = await _operationHarness(tester, repository: repository);

    await tester.tap(find.text('Tüm Kasaları Kaldır'));
    await tester.pumpAndSettle();
    final firstKey = repository.lastRemoveAllIdempotencyKey;
    expect(
      find.byKey(const Key('ambiguous-submission-banner')),
      findsOneWidget,
    );

    repository.removeAllError = null;
    await tester.tap(find.byKey(const Key('retry-ambiguous')));
    await tester.pumpAndSettle();

    expect(repository.removeAllCount, 2);
    expect(repository.lastRemoveAllIdempotencyKey, firstKey);
    harness.dispose();
  });

  testWidgets('global tasks separates active and terminal business views', (
    tester,
  ) async {
    final repository = FakePdaRepository()
      ..operatorQueueValue = OperatorQueuePage(
        terminal: false,
        offset: 0,
        limit: 50,
        hasMore: false,
        items: [
          OperatorQueueItem(
            queuePosition: 1,
            machine: 'SH-01-A · Hücre',
            part: '71550BN000-10-HAOS',
            logicalContainerNumber: 2,
            businessAction: 'Dolu kasa kaldırılıyor',
            requestedAt: DateTime.utc(2026, 9, 3, 9, 30),
            state: 'IN_PROGRESS',
            etaSeconds: null,
            etaState: 'UNAVAILABLE',
          ),
        ],
      );
    final harness = await _loggedInHarness(tester, repository);

    await tester.tap(find.byKey(const Key('open-operator-queue')));
    await tester.pumpAndSettle();

    expect(find.text('Görevler'), findsOneWidget);
    expect(find.text('SH-01-A · Hücre'), findsOneWidget);
    expect(find.text('2. Kasa · Dolu kasa kaldırılıyor'), findsOneWidget);
    expect(find.text('Süre bilgisi yok'), findsOneWidget);
    expect(find.text('Aktif'), findsOneWidget);
    expect(find.text('Tamamlananlar'), findsOneWidget);
    expect(find.textContaining('WorkSite'), findsNothing);
    expect(find.textContaining('AGV'), findsNothing);
    expect(find.textContaining('RDS'), findsNothing);
    harness.dispose();
  });
}

Future<_Harness> _operationHarness(
  WidgetTester tester, {
  FakePdaRepository? repository,
}) async {
  final harness = await _loggedInHarness(
    tester,
    repository ?? FakePdaRepository(),
  );
  await harness.controller.selectMachine(testMachine);
  await harness.controller.selectMainPartOption(testMainPartOption);
  await tester.pumpAndSettle();
  return harness;
}

Future<_Harness> _loggedInHarness(
  WidgetTester tester,
  FakePdaRepository repository,
) async {
  final controller = AppController(
    repository: repository,
    deviceCodeStore: MemoryDeviceCodeStore(testDeviceCode),
    session: SessionMemory(),
    enablePolling: false,
  );
  await controller.initialize();
  await tester.pumpWidget(
    TkgPdaApp(key: ValueKey(controller), controller: controller),
  );
  await controller.login('00123');
  await tester.pumpAndSettle();
  return _Harness(controller, repository);
}

class _Harness {
  const _Harness(this.controller, this.repository);

  final AppController controller;
  final FakePdaRepository repository;

  void dispose() => controller.dispose();
}
