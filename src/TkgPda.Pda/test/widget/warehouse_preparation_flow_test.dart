import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:tkg_pda/app/app.dart';
import 'package:tkg_pda/app/app_controller.dart';
import 'package:tkg_pda/core/auth/session_memory.dart';
import 'package:tkg_pda/core/errors/api_problem.dart';
import 'package:tkg_pda/core/storage/device_code_store.dart';
import 'package:tkg_pda/data/models/pda_models.dart';

import '../support/fakes.dart';

// Customer UI — Warehouse Preparation Console: a warehouse/supervisor-only
// list of WAITING_PREPARATION requests with a confirmed "Hazır" action,
// kept separate from the line-operator part-selection flow.
void main() {
  testWidgets(
    'warehouse-authorized user can open the console and see waiting requests',
    (tester) async {
      final repository = FakePdaRepository()
        ..loginUser = _warehouseUser
        ..warehouseRequestsValue = [testWarehousePreparationRequest];
      final harness = await _harness(tester, repository: repository);

      expect(
        find.byKey(const Key('open-warehouse-preparation')),
        findsOneWidget,
      );
      await tester.tap(find.byKey(const Key('open-warehouse-preparation')));
      await tester.pumpAndSettle();

      expect(
        find.byKey(const Key('warehouse-preparation-list')),
        findsOneWidget,
      );
      expect(
        find.byKey(
          Key('warehouse-item-${testWarehousePreparationRequest.requestId}'),
        ),
        findsOneWidget,
      );
      expect(find.textContaining('SLOT-V'), findsNothing);
      harness.dispose();
    },
  );

  testWidgets('ordinary line operator does not gain the warehouse action', (
    tester,
  ) async {
    final harness = await _harness(tester);

    expect(find.byKey(const Key('open-warehouse-preparation')), findsNothing);
    expect(harness.controller.canAccessWarehousePreparation, isFalse);
    harness.dispose();
  });

  testWidgets('supervisor can also open the console', (tester) async {
    final repository = FakePdaRepository()
      ..loginUser = _supervisorUser
      ..warehouseRequestsValue = [testWarehousePreparationRequest];
    final harness = await _harness(tester, repository: repository);

    expect(find.byKey(const Key('open-warehouse-preparation')), findsOneWidget);
    harness.dispose();
  });

  testWidgets('empty state is shown when there is nothing to prepare', (
    tester,
  ) async {
    final repository = FakePdaRepository()..loginUser = _warehouseUser;
    final harness = await _harness(tester, repository: repository);
    await tester.tap(find.byKey(const Key('open-warehouse-preparation')));
    await tester.pumpAndSettle();

    expect(
      find.byKey(const Key('warehouse-preparation-empty')),
      findsOneWidget,
    );
    harness.dispose();
  });

  testWidgets('a failed list load shows an error with retry', (tester) async {
    final repository = FakePdaRepository()
      ..loginUser = _warehouseUser
      ..warehouseRequestsError = const ApiProblem(
        status: 503,
        code: 'DATABASE_UNAVAILABLE',
        detail: 'Veritabanı geçici olarak kullanılamıyor.',
        correlationId: null,
      );
    final harness = await _harness(tester, repository: repository);
    await tester.tap(find.byKey(const Key('open-warehouse-preparation')));
    await tester.pumpAndSettle();

    expect(
      find.byKey(const Key('warehouse-preparation-error')),
      findsOneWidget,
    );

    repository.warehouseRequestsError = null;
    repository.warehouseRequestsValue = [testWarehousePreparationRequest];
    await tester.tap(find.byKey(const Key('warehouse-preparation-retry')));
    await tester.pumpAndSettle();

    expect(
      find.byKey(
        Key('warehouse-item-${testWarehousePreparationRequest.requestId}'),
      ),
      findsOneWidget,
    );
    harness.dispose();
  });

  testWidgets('marking ready requires confirmation before acting', (
    tester,
  ) async {
    final repository = FakePdaRepository()
      ..loginUser = _warehouseUser
      ..warehouseRequestsValue = [testWarehousePreparationRequest];
    final harness = await _harness(tester, repository: repository);
    await tester.tap(find.byKey(const Key('open-warehouse-preparation')));
    await tester.pumpAndSettle();

    await tester.tap(
      find.byKey(
        Key('warehouse-ready-${testWarehousePreparationRequest.requestId}'),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('confirm-ready')), findsOneWidget);

    await tester.tap(find.text('Vazgeç'));
    await tester.pumpAndSettle();

    expect(repository.markReadyCount, 0);
    expect(
      find.byKey(
        Key('warehouse-item-${testWarehousePreparationRequest.requestId}'),
      ),
      findsOneWidget,
    );
    harness.dispose();
  });

  testWidgets(
    'confirming ready marks the request and removes it from the list',
    (tester) async {
      final repository = FakePdaRepository()
        ..loginUser = _warehouseUser
        ..warehouseRequestsValue = [testWarehousePreparationRequest];
      final harness = await _harness(tester, repository: repository);
      await tester.tap(find.byKey(const Key('open-warehouse-preparation')));
      await tester.pumpAndSettle();

      await tester.tap(
        find.byKey(
          Key('warehouse-ready-${testWarehousePreparationRequest.requestId}'),
        ),
      );
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('confirm-ready')));
      await tester.pumpAndSettle();

      expect(repository.markReadyCount, 1);
      expect(
        repository.lastMarkReadyRequestId,
        testWarehousePreparationRequest.requestId,
      );
      expect(
        find.byKey(
          Key('warehouse-item-${testWarehousePreparationRequest.requestId}'),
        ),
        findsNothing,
      );
      expect(
        find.byKey(const Key('warehouse-preparation-empty')),
        findsOneWidget,
      );
      harness.dispose();
    },
  );

  testWidgets('a failed ready action keeps the item for retry', (tester) async {
    final repository = FakePdaRepository()
      ..loginUser = _warehouseUser
      ..warehouseRequestsValue = [testWarehousePreparationRequest]
      ..markReadyError = const ApiProblem(
        status: 409,
        code: 'REQUEST_TERMINAL',
        detail: 'İstek artık etkin değil.',
        correlationId: 'corr-ready',
      );
    final harness = await _harness(tester, repository: repository);
    await tester.tap(find.byKey(const Key('open-warehouse-preparation')));
    await tester.pumpAndSettle();

    await tester.tap(
      find.byKey(
        Key('warehouse-ready-${testWarehousePreparationRequest.requestId}'),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('confirm-ready')));
    await tester.pumpAndSettle();

    expect(repository.markReadyCount, 1);
    expect(
      find.byKey(
        Key('warehouse-item-${testWarehousePreparationRequest.requestId}'),
      ),
      findsOneWidget,
    );
    expect(harness.controller.problem?.code, 'REQUEST_TERMINAL');
    harness.dispose();
  });
}

final _warehouseUser = AuthenticatedUser(
  id: testUser.id,
  username: 'warehouse-operator',
  fullName: 'Depo Operatörü',
  employeeNo: null,
  roles: const ['WAREHOUSE_OPERATOR'],
);

final _supervisorUser = AuthenticatedUser(
  id: testUser.id,
  username: 'supervisor',
  fullName: 'Vardiya Amiri',
  employeeNo: null,
  roles: const ['SUPERVISOR'],
);

Future<_Harness> _harness(
  WidgetTester tester, {
  FakePdaRepository? repository,
}) async {
  final store = MemoryDeviceCodeStore(testDeviceCode);
  final actualRepository = repository ?? FakePdaRepository();
  final controller = AppController(
    repository: actualRepository,
    deviceCodeStore: store,
    session: SessionMemory(),
    enablePolling: false,
  );
  await controller.initialize();
  await tester.pumpWidget(
    TkgPdaApp(key: ValueKey(controller), controller: controller),
  );
  await tester.pumpAndSettle();
  await controller.login('employee-1');
  await tester.pumpAndSettle();
  return _Harness(controller, actualRepository);
}

class _Harness {
  const _Harness(this.controller, this.repository);

  final AppController controller;
  final FakePdaRepository repository;

  void dispose() => controller.dispose();
}
