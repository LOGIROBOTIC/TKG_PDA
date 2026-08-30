import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:tkg_pda/app/app.dart';
import 'package:tkg_pda/app/app_controller.dart';
import 'package:tkg_pda/core/auth/session_memory.dart';
import 'package:tkg_pda/core/errors/api_problem.dart';
import 'package:tkg_pda/core/storage/device_code_store.dart';
import 'package:tkg_pda/data/models/pda_models.dart';

import '../support/fakes.dart';

// Customer Monitoring — Robot Alarm Visibility: the Operator-facing banner
// on the operation screen must reflect the coarse fleet-wide summary from
// GET /api/v1/rds/robot-alarm-summary, and must not confuse an RDS-read
// failure with "no active alarm".
void main() {
  testWidgets('no banner is shown when there is no active robot alarm', (
    tester,
  ) async {
    final harness = await _operationHarness(tester);

    expect(find.byKey(const Key('robot-alarm-banner')), findsNothing);
    expect(
      find.byKey(const Key('robot-alarm-unavailable-banner')),
      findsNothing,
    );
    harness.dispose();
  });

  testWidgets('a fatal or error alarm shows the intervention message', (
    tester,
  ) async {
    final repository = FakePdaRepository()
      ..robotAlarmSummaryValue = const RobotAlarmSummary(
        hasActiveRobotAlarm: true,
        highestSeverity: 'fatal',
        activeAlarmCount: 1,
      );
    final harness = await _operationHarness(tester, repository: repository);

    expect(find.byKey(const Key('robot-alarm-banner')), findsOneWidget);
    expect(find.text('Robot işlemi için müdahale gerekiyor'), findsOneWidget);
    harness.dispose();
  });

  testWidgets('a warning alarm shows the milder message', (tester) async {
    final repository = FakePdaRepository()
      ..robotAlarmSummaryValue = const RobotAlarmSummary(
        hasActiveRobotAlarm: true,
        highestSeverity: 'warning',
        activeAlarmCount: 1,
      );
    final harness = await _operationHarness(tester, repository: repository);

    expect(find.byKey(const Key('robot-alarm-banner')), findsOneWidget);
    expect(find.text('Robot uyarısı mevcut'), findsOneWidget);
    harness.dispose();
  });

  testWidgets(
    'an RDS-unavailable alarm read shows a distinct state with retry, not "no alarm"',
    (tester) async {
      final repository = FakePdaRepository()
        ..robotAlarmSummaryError = const ApiProblem(
          status: 503,
          code: 'RDS_UNAVAILABLE',
          detail: 'RDS okuma servisi geçici olarak kullanılamıyor.',
          correlationId: null,
        );
      final harness = await _operationHarness(tester, repository: repository);

      expect(
        find.byKey(const Key('robot-alarm-unavailable-banner')),
        findsOneWidget,
      );
      expect(find.byKey(const Key('robot-alarm-banner')), findsNothing);

      repository.robotAlarmSummaryError = null;
      repository.robotAlarmSummaryValue = const RobotAlarmSummary(
        hasActiveRobotAlarm: true,
        highestSeverity: 'error',
        activeAlarmCount: 2,
      );
      await tester.tap(find.byKey(const Key('robot-alarm-retry')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('robot-alarm-banner')), findsOneWidget);
      expect(
        find.byKey(const Key('robot-alarm-unavailable-banner')),
        findsNothing,
      );
      harness.dispose();
    },
  );
}

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
  await controller.login('operator');
  await tester.pumpAndSettle();
  return _Harness(controller, actualRepository);
}

Future<_Harness> _operationHarness(
  WidgetTester tester, {
  FakePdaRepository? repository,
}) async {
  final harness = await _harness(tester, repository: repository);
  await harness.controller.selectMachine(testMachine);
  await tester.pumpAndSettle();
  harness.controller.selectMainPartOption(testMainPartOption);
  await harness.controller.selectEffectivePart(
    EffectivePartSelection(
      partId: testMainPartOption.partId,
      partCode: testMainPartOption.partCode,
      isMain: true,
    ),
  );
  await tester.pumpAndSettle();
  return harness;
}

class _Harness {
  const _Harness(this.controller, this.repository);

  final AppController controller;
  final FakePdaRepository repository;

  void dispose() => controller.dispose();
}
