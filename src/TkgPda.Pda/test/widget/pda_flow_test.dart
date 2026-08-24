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
  testWidgets('first run opens Device Setup', (tester) async {
    final harness = await _harness(tester, deviceCode: null);

    expect(find.text('TKG PDA Cihaz Kurulumu'), findsOneWidget);
    expect(find.byKey(const Key('device-code-field')), findsOneWidget);
    harness.dispose();
  });

  testWidgets('Device Setup securely saves provisioned code then opens login', (
    tester,
  ) async {
    final harness = await _harness(tester, deviceCode: null);

    await tester.enterText(
      find.byKey(const Key('device-code-field')),
      testDeviceCode,
    );
    await tester.tap(find.byKey(const Key('save-device-code')));
    await tester.pumpAndSettle();

    expect(harness.store.value, testDeviceCode);
    expect(find.text('TKG Operatör Girişi'), findsOneWidget);
    harness.dispose();
  });

  testWidgets('valid login opens machine list', (tester) async {
    final harness = await _harness(tester);

    await _loginFromUi(tester);

    expect(find.text('Makine Seçimi'), findsOneWidget);
    expect(harness.controller.isAuthenticated, isTrue);
    harness.dispose();
  });

  testWidgets('invalid login shows generic ProblemDetails UX', (tester) async {
    final repository = FakePdaRepository()
      ..loginProblem = const ApiProblem(
        status: 401,
        code: 'INVALID_CREDENTIALS',
        detail: 'Kullanıcı adı veya şifre geçersiz.',
        correlationId: 'corr-login',
      );
    final harness = await _harness(tester, repository: repository);

    await _loginFromUi(tester);

    expect(find.byKey(const Key('problem-banner')), findsOneWidget);
    expect(
      find.text('Oturumunuz sona erdi. Lütfen yeniden giriş yapın.'),
      findsOneWidget,
    );
    harness.dispose();
  });

  testWidgets('machine list renders only repository values', (tester) async {
    final repository = FakePdaRepository();
    final harness = await _loggedInHarness(tester, repository: repository);

    expect(find.byKey(Key('machine-${testMachine.id}')), findsOneWidget);
    expect(find.text(testMachine.code), findsOneWidget);
    expect(find.text('Operasyon'), findsOneWidget);
    harness.dispose();
  });

  testWidgets('machine selection loads operational context', (tester) async {
    final harness = await _loggedInHarness(tester);

    await tester.tap(find.byKey(Key('machine-${testMachine.id}')));
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('operation-content')), findsOneWidget);
    expect(find.text('${testMachine.code} · Operasyon'), findsOneWidget);
    harness.dispose();
  });

  testWidgets('active variant is shown prominently', (tester) async {
    final harness = await _operationHarness(tester);

    expect(find.byKey(const Key('active-variant')), findsOneWidget);
    expect(find.textContaining('Aktif Varyant: V1'), findsOneWidget);
    harness.dispose();
  });

  testWidgets('variant selection requires confirmation and calls backend port', (
    tester,
  ) async {
    final repository = FakePdaRepository();
    final harness = await _operationHarness(tester, repository: repository);

    await tester.tap(find.byKey(const Key('variant-selection-button')));
    await tester.pumpAndSettle();
    await tester.tap(
      find.byKey(
        const Key(
          'variant-40000000-0000-0000-0000-000000000002-50000000-0000-0000-0000-000000000001',
        ),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('confirm-variant-switch')));
    await tester.pumpAndSettle();

    expect(repository.switchCount, 1);
    harness.dispose();
  });

  testWidgets('Product Group and Alt Part preserve unknown evidence wording', (
    tester,
  ) async {
    final harness = await _operationHarness(tester);

    await tester.tap(
      find.byKey(
        const Key('product-group-60000000-0000-0000-0000-000000000001'),
      ),
    );
    await tester.pump();

    expect(find.textContaining('715A2BN400-50'), findsOneWidget);
    expect(find.textContaining('Kasa: Tanımlı değil'), findsOneWidget);
    expect(find.textContaining('Lot: Tanımlı değil'), findsOneWidget);
    harness.dispose();
  });

  testWidgets('normalized layout renders all proven regions and slots', (
    tester,
  ) async {
    final harness = await _operationHarness(tester);

    expect(find.byKey(const Key('slot-slot-machine')), findsOneWidget);
    expect(find.byKey(const Key('slot-slot-vacant')), findsOneWidget);
    expect(find.byKey(const Key('slot-slot-empty')), findsOneWidget);
    expect(find.byKey(const Key('slot-slot-full')), findsOneWidget);
    expect(
      find.textContaining('robot veya SEER haritası değildir'),
      findsOneWidget,
    );
    harness.dispose();
  });

  testWidgets('MACHINE region is visibly nontransport and not selectable', (
    tester,
  ) async {
    final harness = await _operationHarness(tester);

    await tester.ensureVisible(find.byKey(const Key('slot-slot-machine')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('slot-slot-machine')));
    await tester.pump();

    expect(find.textContaining('Makine Alanı'), findsWidgets);
    expect(harness.controller.selectedSlot, isNull);
    expect(find.byKey(const Key('slot-details')), findsNothing);
    harness.dispose();
  });

  testWidgets('VACANT slot renders server-enabled BRING_EMPTY', (tester) async {
    final harness = await _operationHarness(tester);

    await _selectSlot(tester, 'slot-vacant');

    final button = tester.widget<ElevatedButton>(
      find.byKey(const Key('action-BRING_EMPTY')),
    );
    expect(button.onPressed, isNotNull);
    harness.dispose();
  });

  testWidgets('OCCUPIED EMPTY slot renders server-enabled REMOVE_EMPTY', (
    tester,
  ) async {
    final harness = await _operationHarness(tester);

    await _selectSlot(tester, 'slot-empty');

    final button = tester.widget<ElevatedButton>(
      find.byKey(const Key('action-REMOVE_EMPTY')),
    );
    expect(button.onPressed, isNotNull);
    harness.dispose();
  });

  testWidgets('OCCUPIED FULL slot renders server-enabled REMOVE_FULL', (
    tester,
  ) async {
    final harness = await _operationHarness(tester);

    await _selectSlot(tester, 'slot-full');

    final button = tester.widget<ElevatedButton>(
      find.byKey(const Key('action-REMOVE_FULL')),
    );
    expect(button.onPressed, isNotNull);
    harness.dispose();
  });

  testWidgets(
    'Kasa Doldu is server-authoritative separate from transport and refetches context',
    (tester) async {
      final repository = FakePdaRepository();
      final harness = await _operationHarness(tester, repository: repository);

      await _selectSlot(tester, 'slot-empty');
      final loadButton = tester.widget<OutlinedButton>(
        find.byKey(const Key('load-state-MARK_FULL')),
      );
      expect(loadButton.onPressed, isNotNull);
      expect(find.byKey(const Key('action-REMOVE_EMPTY')), findsOneWidget);
      await tester.tap(find.byKey(const Key('load-state-MARK_FULL')));
      await tester.pumpAndSettle();
      expect(find.textContaining('taşıma talebi oluşturmaz'), findsOneWidget);
      await tester.tap(find.byKey(const Key('confirm-MARK_FULL')));
      await tester.pumpAndSettle();

      expect(repository.loadStateCount, 1);
      expect(repository.lastLoadState, 'FULL');
      expect(repository.createCount, 0);
      expect(repository.contextCount, 2);
      harness.dispose();
    },
  );

  testWidgets('Kasa Boş uses only backend-projected capability', (
    tester,
  ) async {
    final repository = FakePdaRepository();
    final harness = await _operationHarness(tester, repository: repository);

    await _selectSlot(tester, 'slot-full');
    final loadButton = tester.widget<OutlinedButton>(
      find.byKey(const Key('load-state-MARK_EMPTY')),
    );
    expect(loadButton.onPressed, isNotNull);
    final markFull = tester.widget<OutlinedButton>(
      find.byKey(const Key('load-state-MARK_FULL')),
    );
    expect(markFull.onPressed, isNull);
    harness.dispose();
  });

  testWidgets('disabled action renders backend reason', (tester) async {
    final harness = await _operationHarness(tester);

    await _selectSlot(tester, 'slot-vacant');

    expect(find.text('Konum durumu uygun değil.'), findsNWidgets(2));
    final remove = tester.widget<ElevatedButton>(
      find.byKey(const Key('action-REMOVE_FULL')),
    );
    expect(remove.onPressed, isNull);
    harness.dispose();
  });

  testWidgets('transport confirmation shows fixed material command', (
    tester,
  ) async {
    final harness = await _operationHarness(tester);

    await _selectSlot(tester, 'slot-vacant');
    await tester.tap(find.byKey(const Key('action-BRING_EMPTY')));
    await tester.pumpAndSettle();

    expect(find.text('Taşıma Talebini Onaylayın'), findsOneWidget);
    expect(find.text('Boş Kasa Getir'), findsWidgets);
    expect(find.text('Boş Konum · SLOT-V'), findsWidgets);
    harness.dispose();
  });

  testWidgets('successful submission uses exact BRING_EMPTY slot payload', (
    tester,
  ) async {
    final repository = FakePdaRepository();
    final harness = await _operationHarness(tester, repository: repository);

    await _selectSlot(tester, 'slot-vacant');
    await tester.tap(find.byKey(const Key('action-BRING_EMPTY')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('confirm-transport')));
    await tester.pumpAndSettle();

    expect(repository.createCount, 1);
    expect(repository.lastCommand!.sourceSlotId, isNull);
    expect(repository.lastCommand!.destinationSlotId, 'slot-vacant');
    expect(repository.lastCommand!.operation, 'BRING_EMPTY');
    harness.dispose();
  });

  testWidgets('ambiguous transport displays safe retry choices', (
    tester,
  ) async {
    final repository = FakePdaRepository()
      ..createError = const AmbiguousMutationException(
        idempotencyKey: 'ambiguous',
      );
    final harness = await _operationHarness(tester, repository: repository);

    await _submitBringEmpty(tester);

    expect(
      find.byKey(const Key('ambiguous-submission-banner')),
      findsOneWidget,
    );
    expect(find.byKey(const Key('retry-ambiguous')), findsOneWidget);
    expect(find.byKey(const Key('refresh-after-ambiguous')), findsOneWidget);
    harness.dispose();
  });

  testWidgets('ambiguous retry reuses its existing idempotency key', (
    tester,
  ) async {
    final repository = FakePdaRepository()
      ..createError = const AmbiguousMutationException(
        idempotencyKey: 'ambiguous',
      );
    final harness = await _operationHarness(tester, repository: repository);

    await _submitBringEmpty(tester);
    final firstKey = repository.lastIdempotencyKey;
    repository.createError = null;
    await tester.tap(find.byKey(const Key('retry-ambiguous')));
    await tester.pumpAndSettle();

    expect(repository.createCount, 2);
    expect(repository.lastIdempotencyKey, firstKey);
    harness.dispose();
  });

  testWidgets('offline state keeps last context but disables mutations', (
    tester,
  ) async {
    final repository = FakePdaRepository();
    final harness = await _operationHarness(tester, repository: repository);
    repository.contextError = const ConnectivityException();

    await harness.controller.refreshContext(showBusy: true);
    await tester.pumpAndSettle();
    harness.controller.selectSlot(
      harness.controller.context!.slots.singleWhere(
        (slot) => slot.id == 'slot-vacant',
      ),
    );
    await tester.pump();
    await _scrollTo(tester, find.byKey(const Key('slot-details')));

    expect(find.byKey(const Key('offline-banner')), findsOneWidget);
    final button = tester.widget<ElevatedButton>(
      find.byKey(const Key('action-BRING_EMPTY')),
    );
    expect(button.onPressed, isNull);
    harness.dispose();
  });

  testWidgets('active transport uses stable execution-state presentation', (
    tester,
  ) async {
    final repository = FakePdaRepository()
      ..contextValue = testContext(activeRequests: [testActiveRequest]);
    final harness = await _operationHarness(tester, repository: repository);

    await _scrollTo(tester, find.byKey(const Key('active-transport-section')));
    expect(find.text('Robot bekleniyor'), findsOneWidget);
    expect(find.text('#42 · Boş Kasa Getir'), findsOneWidget);
    expect(find.textContaining('AGV'), findsNothing);
    harness.dispose();
  });

  testWidgets(
    'failure state prioritizes operator message and support reference',
    (tester) async {
      final failure = _failureRequest();
      final repository = FakePdaRepository()
        ..contextValue = testContext(activeRequests: [failure]);
      final harness = await _operationHarness(tester, repository: repository);

      await _scrollTo(
        tester,
        find.byKey(const Key('active-transport-section')),
      );
      expect(find.text('Tamamlanamadı'), findsOneWidget);
      expect(find.text('Robot görevi tamamlayamadı.'), findsOneWidget);
      expect(find.text('Destek referansı'), findsOneWidget);
      harness.dispose();
    },
  );

  testWidgets('cancel requires confirmation and uses backend capability', (
    tester,
  ) async {
    final repository = FakePdaRepository()
      ..contextValue = testContext(activeRequests: [testActiveRequest]);
    final harness = await _operationHarness(tester, repository: repository);

    await _scrollTo(
      tester,
      find.byKey(Key('cancel-${testActiveRequest.requestId}')),
    );
    await tester.tap(find.byKey(Key('cancel-${testActiveRequest.requestId}')));
    await tester.pumpAndSettle();
    expect(find.text('Talebi iptal et'), findsOneWidget);
    await tester.tap(find.byKey(const Key('confirm-cancel')));
    await tester.pumpAndSettle();

    expect(repository.cancelCount, 1);
    harness.dispose();
  });

  testWidgets('401 clears state and router returns to login', (tester) async {
    final harness = await _loggedInHarness(tester);

    harness.controller.handleUnauthorized();
    await tester.pumpAndSettle();

    expect(find.text('TKG Operatör Girişi'), findsOneWidget);
    expect(harness.controller.isAuthenticated, isFalse);
    harness.dispose();
  });

  testWidgets('logout clears auth while preserving device code', (
    tester,
  ) async {
    final harness = await _loggedInHarness(tester);

    await tester.tap(find.byKey(const Key('logout-button')));
    await tester.pumpAndSettle();

    expect(find.text('TKG Operatör Girişi'), findsOneWidget);
    expect(harness.store.value, testDeviceCode);
    expect(harness.controller.isAuthenticated, isFalse);
    harness.dispose();
  });

  testWidgets('process restart does not restore an authenticated session', (
    tester,
  ) async {
    final store = MemoryDeviceCodeStore(testDeviceCode);
    final first = await _loggedInHarness(tester, store: store);
    expect(first.controller.isAuthenticated, isTrue);
    first.dispose();

    final second = await _harness(tester, store: store);

    expect(find.text('TKG Operatör Girişi'), findsOneWidget);
    expect(second.controller.isAuthenticated, isFalse);
    expect(second.store.value, testDeviceCode);
    second.dispose();
  });
}

Future<_Harness> _harness(
  WidgetTester tester, {
  String? deviceCode = testDeviceCode,
  MemoryDeviceCodeStore? store,
  FakePdaRepository? repository,
}) async {
  final actualStore = store ?? MemoryDeviceCodeStore(deviceCode);
  final actualRepository = repository ?? FakePdaRepository();
  final controller = AppController(
    repository: actualRepository,
    deviceCodeStore: actualStore,
    session: SessionMemory(),
    enablePolling: false,
  );
  await controller.initialize();
  await tester.pumpWidget(
    TkgPdaApp(key: ValueKey(controller), controller: controller),
  );
  await tester.pumpAndSettle();
  return _Harness(controller, actualStore, actualRepository);
}

Future<_Harness> _loggedInHarness(
  WidgetTester tester, {
  MemoryDeviceCodeStore? store,
  FakePdaRepository? repository,
}) async {
  final harness = await _harness(tester, store: store, repository: repository);
  await harness.controller.login('operator', 'transient');
  await tester.pumpAndSettle();
  return harness;
}

Future<_Harness> _operationHarness(
  WidgetTester tester, {
  FakePdaRepository? repository,
}) async {
  final harness = await _loggedInHarness(tester, repository: repository);
  await harness.controller.selectMachine(testMachine);
  await tester.pumpAndSettle();
  return harness;
}

Future<void> _loginFromUi(WidgetTester tester) async {
  await tester.enterText(find.byKey(const Key('username-field')), 'operator');
  await tester.enterText(find.byKey(const Key('password-field')), 'transient');
  await tester.tap(find.byKey(const Key('login-button')));
  await tester.pumpAndSettle();
}

Future<void> _selectSlot(WidgetTester tester, String slotId) async {
  await tester.ensureVisible(find.byKey(Key('slot-$slotId')));
  await tester.pumpAndSettle();
  await tester.tap(find.byKey(Key('slot-$slotId')));
  await tester.pump();
  await _scrollTo(tester, find.byKey(const Key('slot-details')));
}

Future<void> _submitBringEmpty(WidgetTester tester) async {
  await _selectSlot(tester, 'slot-vacant');
  await tester.tap(find.byKey(const Key('action-BRING_EMPTY')));
  await tester.pumpAndSettle();
  await tester.tap(find.byKey(const Key('confirm-transport')));
  await tester.pumpAndSettle();
}

Future<void> _scrollTo(WidgetTester tester, Finder finder) async {
  await tester.scrollUntilVisible(
    finder,
    350,
    scrollable: find.byType(Scrollable).last,
  );
  await tester.pumpAndSettle();
}

ActiveTransportRequest _failureRequest() => ActiveTransportRequest(
  requestId: 'failure-request',
  requestNo: 77,
  operation: 'REMOVE_FULL',
  status: 'RUNNING',
  requestedBy: testUser.id,
  requestedByUsername: testUser.username,
  isMine: true,
  sourceSlotId: 'slot-full',
  destinationSlotId: null,
  productGroupId: null,
  partId: null,
  containerTypeId: null,
  priority: 100,
  createdAt: DateTime.utc(2026, 8, 24),
  canCancel: false,
  cancelDisabledCode: 'CANCELLATION_AFTER_EXECUTION_DEFERRED',
  cancelDisabledMessage: 'Yürütme başladıktan sonra PDA iptali yapılamaz.',
  execution: const ExecutionProjection(
    state: 'FAILED',
    rdsTaskRecordId: 'RDS-77',
    rdsTaskLabel: null,
    outOrderNo: null,
    agvId: 'AGV-7',
    rdsStatus: 1004,
    lastSyncAt: null,
    failureCode: 'EXECUTION_FAILED',
    failureCategory: 'EXECUTION',
    operatorMessage: 'Robot görevi tamamlayamadı.',
    technicalMessage: null,
    correlationId: 'failure-correlation',
  ),
);

class _Harness {
  const _Harness(this.controller, this.store, this.repository);

  final AppController controller;
  final MemoryDeviceCodeStore store;
  final FakePdaRepository repository;

  void dispose() => controller.dispose();
}
