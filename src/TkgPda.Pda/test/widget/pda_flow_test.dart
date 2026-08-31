import 'dart:async';

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

    expect(find.text('TKG Operator · Cihaz Kurulumu'), findsOneWidget);
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

  testWidgets('login screen shows Sicil Numarası and no username field', (
    tester,
  ) async {
    final harness = await _harness(tester);

    expect(find.text('Sicil Numarası'), findsOneWidget);
    expect(find.text('Kullanıcı Adı'), findsNothing);
    expect(find.byKey(const Key('employee-number-field')), findsOneWidget);
    expect(find.byKey(const Key('username-field')), findsNothing);
    harness.dispose();
  });

  testWidgets('login submits the employee number, not a username', (
    tester,
  ) async {
    final repository = FakePdaRepository();
    final harness = await _harness(tester, repository: repository);

    await _loginFromUi(tester, employeeNumber: '00123');

    expect(repository.lastEmployeeNumber, '00123');
    harness.dispose();
  });

  testWidgets('login preserves significant leading zeros in the sicil value', (
    tester,
  ) async {
    final repository = FakePdaRepository();
    final harness = await _harness(tester, repository: repository);

    await _loginFromUi(tester, employeeNumber: '001245');

    expect(repository.lastEmployeeNumber, '001245');
    expect(repository.lastEmployeeNumber, isNot('1245'));
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

  testWidgets('login shows a device error when the device is not authorized', (
    tester,
  ) async {
    final repository = FakePdaRepository()
      ..loginProblem = const ApiProblem(
        status: 403,
        code: 'DEVICE_NOT_AUTHORIZED',
        detail: 'The client device is unknown, inactive or not a PDA.',
        correlationId: 'corr-device',
      );
    final harness = await _harness(tester, repository: repository);

    await _loginFromUi(tester);

    expect(find.byKey(const Key('problem-banner')), findsOneWidget);
    expect(find.text('Bu işlem için yetkiniz bulunmuyor.'), findsOneWidget);
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

  testWidgets('an operator with no assigned machines sees an empty state', (
    tester,
  ) async {
    final repository = FakePdaRepository()..machineValues = [];
    final harness = await _loggedInHarness(tester, repository: repository);

    expect(find.byKey(const Key('machine-list-empty')), findsOneWidget);
    expect(
      find.textContaining('Size tanımlı makine bulunamadı'),
      findsOneWidget,
    );
    harness.dispose();
  });

  testWidgets('machine selection loads operational context', (tester) async {
    final harness = await _loggedInHarness(tester);

    await tester.tap(find.byKey(Key('machine-${testMachine.id}')));
    await tester.pumpAndSettle();

    expect(find.text('${testMachine.code} · Operasyon'), findsOneWidget);
    expect(find.byKey(const Key('part-first-selector')), findsOneWidget);
    expect(harness.controller.context, isNotNull);
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
    expect(harness.controller.selectedEffectivePart, isNull);
    expect(harness.controller.needsPartSelection, isTrue);
    expect(find.byKey(const Key('part-first-selector')), findsOneWidget);
    harness.dispose();
  });

  testWidgets('normalized layout renders all proven regions and slots', (
    tester,
  ) async {
    final harness = await _operationHarness(tester);

    await _scrollTo(tester, find.byKey(const Key('slot-slot-machine')));
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

    await _scrollTo(tester, find.byKey(const Key('slot-slot-machine')));
    await tester.tap(find.byKey(const Key('slot-slot-machine')));
    await tester.pump();

    expect(find.textContaining('Makine Alanı'), findsWidgets);
    expect(harness.controller.selectedSlot, isNull);
    expect(find.byKey(const Key('slot-details')), findsNothing);
    harness.dispose();
  });

  testWidgets(
    'layout slot tap is inspection-only and exposes no transport action',
    (tester) async {
      final harness = await _operationHarness(tester);

      await _selectSlot(tester, 'slot-vacant');

      expect(find.byKey(const Key('slot-details')), findsOneWidget);
      expect(harness.controller.selectedSlot?.id, 'slot-vacant');
      expect(find.byKey(const Key('action-BRING_EMPTY')), findsNothing);
      expect(find.byKey(const Key('action-REMOVE_FULL')), findsNothing);
      expect(find.byKey(const Key('action-REMOVE_EMPTY')), findsNothing);
      harness.dispose();
    },
  );

  testWidgets('the selected slot on the layout canvas shows a clear marker', (
    tester,
  ) async {
    final harness = await _operationHarness(tester);

    expect(find.byKey(const Key('slot-selected-badge')), findsNothing);
    // Select without the usual scroll-to-details step: the details panel
    // appearing below can push the canvas itself out of the ListView's
    // cache extent, which is irrelevant to what this test checks.
    await _scrollTo(tester, find.byKey(const Key('slot-slot-vacant')));
    await tester.tap(find.byKey(const Key('slot-slot-vacant')));
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('slot-selected-badge')), findsOneWidget);
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
      expect(find.byKey(const Key('action-REMOVE_EMPTY')), findsNothing);
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

  group('Customer UX — Automatic Machine Slot Resolution', () {
    testWidgets(
      'direct action buttons are reachable without tapping a layout slot',
      (tester) async {
        final harness = await _operationHarness(tester);

        expect(
          find.byKey(const Key('direct-action-BRING_EMPTY')),
          findsOneWidget,
        );
        expect(
          find.byKey(const Key('direct-action-REMOVE_FULL')),
          findsOneWidget,
        );
        expect(
          find.byKey(const Key('direct-action-REMOVE_EMPTY')),
          findsOneWidget,
        );
        expect(harness.controller.selectedSlot, isNull);
        harness.dispose();
      },
    );

    testWidgets(
      'a direct BRING_EMPTY submits with no chosen slot and highlights the '
      'backend-resolved slot',
      (tester) async {
        final repository = FakePdaRepository();
        final harness = await _operationHarness(tester, repository: repository);

        await tester.tap(find.byKey(const Key('direct-action-BRING_EMPTY')));
        await tester.pumpAndSettle();
        expect(find.text('Otomatik seçilecek'), findsOneWidget);
        await tester.tap(find.byKey(const Key('confirm-direct-transport')));
        await tester.pumpAndSettle();

        expect(repository.createCount, 1);
        expect(repository.lastCommand!.sourceSlotId, isNull);
        expect(repository.lastCommand!.destinationSlotId, isNull);
        expect(repository.lastCommand!.productGroupId, testProductGroupId);
        expect(repository.lastCommand!.partId, testMainPartId);
        expect(repository.lastCommand!.operation, 'BRING_EMPTY');

        // The fake's post-create context carries testActiveRequest, whose
        // destinationSlotId is 'slot-vacant' — the controller should mirror
        // that into selectedSlot without any operator tap.
        expect(harness.controller.selectedSlot?.id, 'slot-vacant');
        expect(harness.controller.slotWasAutoResolved, isTrue);
        await _scrollTo(
          tester,
          find.byKey(const Key('slot-auto-resolved-indicator')),
        );
        expect(
          find.byKey(const Key('slot-auto-resolved-indicator')),
          findsOneWidget,
        );
        expect(find.text('Konum otomatik seçildi'), findsOneWidget);
        harness.dispose();
      },
    );

    testWidgets(
      'tapping a slot manually afterwards clears the automatic-resolution '
      'indicator',
      (tester) async {
        final repository = FakePdaRepository();
        final harness = await _operationHarness(tester, repository: repository);

        await tester.tap(find.byKey(const Key('direct-action-BRING_EMPTY')));
        await tester.pumpAndSettle();
        await tester.tap(find.byKey(const Key('confirm-direct-transport')));
        await tester.pumpAndSettle();
        expect(harness.controller.slotWasAutoResolved, isTrue);

        await _scrollTo(tester, find.byKey(const Key('slot-slot-vacant')));
        await tester.tap(find.byKey(const Key('slot-slot-vacant')));
        await tester.pumpAndSettle();

        expect(harness.controller.slotWasAutoResolved, isFalse);
        harness.dispose();
      },
    );

    testWidgets(
      'a friendly no-slot message from the backend surfaces without any '
      'raw rule code',
      (tester) async {
        final repository = FakePdaRepository()
          ..createError = const ApiProblem(
            status: 422,
            code: 'BRING_EMPTY_SLOT_NOT_FOUND',
            detail: 'Bu parça için boş konum bulunamadı.',
            correlationId: null,
          );
        final harness = await _operationHarness(tester, repository: repository);

        await tester.tap(find.byKey(const Key('direct-action-BRING_EMPTY')));
        await tester.pumpAndSettle();
        await tester.tap(find.byKey(const Key('confirm-direct-transport')));
        await tester.pumpAndSettle();

        expect(
          find.text('Bu parça için boş konum bulunamadı.'),
          findsOneWidget,
        );
        expect(find.textContaining('BRING_EMPTY_SLOT_NOT_FOUND'), findsNothing);
        expect(harness.controller.selectedSlot, isNull);
        harness.dispose();
      },
    );
  });

  testWidgets('the action button is disabled while a submission is in flight, '
      'preventing duplicate submission', (tester) async {
    final repository = FakePdaRepository()
      ..createCompleter = Completer<TransportResult>();
    final harness = await _operationHarness(tester, repository: repository);

    await tester.tap(find.byKey(const Key('direct-action-BRING_EMPTY')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('confirm-direct-transport')));
    // A single frame — the pending completer never resolves, and the busy
    // indicator is an indeterminate animation, so pumpAndSettle would hang.
    await tester.pump();

    expect(harness.controller.busy, isTrue);
    expect(
      tester
          .widget<ElevatedButton>(
            find.byKey(const Key('direct-action-BRING_EMPTY')),
          )
          .onPressed,
      isNull,
    );
    expect(repository.createCount, 1);

    repository.createCompleter!.complete(
      const TransportResult(
        id: '90000000-0000-0000-0000-000000000001',
        requestNo: 42,
        status: 'QUEUED',
        operation: 'BRING_EMPTY',
        idempotentReplay: false,
      ),
    );
    await tester.pumpAndSettle();
    expect(repository.createCount, 1);
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
    expect(find.byKey(const Key('offline-banner')), findsOneWidget);
    final button = tester.widget<ElevatedButton>(
      find.byKey(const Key('direct-action-BRING_EMPTY')),
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

  testWidgets('Device Code change clears session and operational state', (
    tester,
  ) async {
    final harness = await _operationHarness(tester);
    expect(harness.controller.isAuthenticated, isTrue);
    expect(harness.controller.context, isNotNull);

    await harness.controller.beginDeviceCodeChange();
    await tester.pumpAndSettle();

    expect(find.text('TKG Operator · Cihaz Kurulumu'), findsOneWidget);
    expect(harness.controller.isAuthenticated, isFalse);
    expect(harness.controller.context, isNull);
    expect(harness.controller.machines, isEmpty);
    await harness.controller.saveDeviceCode('SH01A-TERM01');
    expect(harness.store.value, 'SH01A-TERM01');
    harness.dispose();
  });

  testWidgets('resume refetch completes before mutations are re-enabled', (
    tester,
  ) async {
    final repository = FakePdaRepository();
    final harness = await _operationHarness(tester, repository: repository);
    repository.meCompleter = Completer<AuthenticatedUser>();

    harness.controller.onPaused();
    expect(harness.controller.mutationEnabled, isFalse);
    expect(harness.controller.pollingActive, isFalse);

    final resumed = harness.controller.onResumed();
    await tester.pump();
    expect(harness.controller.resumeValidationPending, isTrue);
    expect(harness.controller.mutationEnabled, isFalse);

    repository.meCompleter!.complete(testUser);
    await resumed;
    await tester.pump();
    expect(harness.controller.resumeValidationPending, isFalse);
    expect(harness.controller.mutationEnabled, isTrue);
    expect(repository.meCount, 1);
    harness.dispose();
  });

  for (final size in const [
    Size(1024, 600),
    Size(1280, 720),
    Size(1366, 768),
    Size(1920, 1080),
  ]) {
    testWidgets(
      'wide touch layout is usable at ${size.width.toInt()}x${size.height.toInt()}',
      (tester) async {
        tester.view.devicePixelRatio = 1;
        tester.view.physicalSize = size;
        addTearDown(tester.view.resetDevicePixelRatio);
        addTearDown(tester.view.resetPhysicalSize);
        final repository = FakePdaRepository()
          ..contextValue = testContext(activeRequests: [testActiveRequest]);
        final harness = await _operationHarness(tester, repository: repository);

        expect(find.byKey(const Key('operation-wide-layout')), findsOneWidget);
        await tester.scrollUntilVisible(
          find.byKey(const Key('active-transport-section')),
          240,
          scrollable: find.descendant(
            of: find.byKey(const Key('operation-wide-left')),
            matching: find.byType(Scrollable),
          ),
        );
        expect(
          find.byKey(const Key('active-transport-section')),
          findsOneWidget,
        );
        expect(tester.takeException(), isNull);

        final machine = harness.controller.context!.slots.singleWhere(
          (slot) => slot.isMachineRegion,
        );
        harness.controller.selectSlot(machine);
        expect(harness.controller.selectedSlot, isNull);

        await tester.scrollUntilVisible(
          find.byKey(const Key('direct-action-BRING_EMPTY')),
          240,
          scrollable: find.descendant(
            of: find.byKey(const Key('operation-wide-left')),
            matching: find.byType(Scrollable),
          ),
        );
        await tester.pumpAndSettle();
        expect(
          tester
              .getSize(find.byKey(const Key('direct-action-BRING_EMPTY')))
              .height,
          greaterThanOrEqualTo(48),
        );
        expect(tester.takeException(), isNull);
        harness.dispose();
      },
    );
  }

  testWidgets(
    'the layout canvas grows taller on narrow/mobile widths for bigger touch targets',
    (tester) async {
      tester.view.devicePixelRatio = 1;
      tester.view.physicalSize = const Size(360, 800);
      addTearDown(tester.view.resetDevicePixelRatio);
      addTearDown(tester.view.resetPhysicalSize);
      final harness = await _operationHarness(tester);

      await _scrollTo(tester, find.byType(AspectRatio));
      final aspectRatio = tester.widget<AspectRatio>(find.byType(AspectRatio));
      expect(aspectRatio.aspectRatio, 1.0);
      expect(tester.takeException(), isNull);
      harness.dispose();
    },
  );
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
  await harness.controller.login('operator');
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
  // WP-03B: the operation screen now gates behind part-first selection.
  // Resolve the default fixture's main part, which auto-resolves to the
  // already-active variant (no switchVariant round-trip), to reach the
  // existing operation content exactly as before.
  await _resolveDefaultPartFirstSelection(harness.controller);
  await tester.pumpAndSettle();
  return harness;
}

Future<void> _resolveDefaultPartFirstSelection(AppController controller) async {
  await controller.selectMainPartOption(testMainPartOption);
  // Customer UX — Part Selection Flow: a single compatible variant now
  // pauses at partFirstReadyToContinue instead of resolving immediately, so
  // fixtures reaching into the operation screen directly must explicitly
  // continue past that gate.
  controller.confirmPartFirstSelection();
}

Future<void> _loginFromUi(
  WidgetTester tester, {
  String employeeNumber = '00123',
}) async {
  await tester.enterText(
    find.byKey(const Key('employee-number-field')),
    employeeNumber,
  );
  await tester.tap(find.byKey(const Key('login-button')));
  await tester.pumpAndSettle();
}

Future<void> _selectSlot(WidgetTester tester, String slotId) async {
  // The direct-actions card above the layout can push slots beyond the
  // ListView's cache extent, so they aren't built yet — scrollUntilVisible
  // (rather than ensureVisible) incrementally scrolls until the slot mounts.
  await _scrollTo(tester, find.byKey(Key('slot-$slotId')));
  await tester.tap(find.byKey(Key('slot-$slotId')));
  await tester.pump();
  await _scrollTo(tester, find.byKey(const Key('slot-details')));
}

Future<void> _submitBringEmpty(WidgetTester tester) async {
  await tester.tap(find.byKey(const Key('direct-action-BRING_EMPTY')));
  await tester.pumpAndSettle();
  await tester.tap(find.byKey(const Key('confirm-direct-transport')));
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
