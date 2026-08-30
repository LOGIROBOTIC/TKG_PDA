import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';
import 'package:tkg_pda/app/app.dart';
import 'package:tkg_pda/app/app_controller.dart';
import 'package:tkg_pda/core/auth/session_memory.dart';
import 'package:tkg_pda/core/network/api_client.dart';
import 'package:tkg_pda/core/storage/device_code_store.dart';
import 'package:tkg_pda/core/storage/device_code_store_web.dart';
import 'package:tkg_pda/data/models/pda_models.dart';
import 'package:tkg_pda/data/repositories/pda_repository.dart';
import 'package:web/web.dart' as web;

const _apiBaseUrl = String.fromEnvironment('TKG_API_BASE_URL');
const _deviceCode = String.fromEnvironment('TKG_TEST_DEVICE_CODE');
const _employeeNumber = String.fromEnvironment('TKG_TEST_EMPLOYEE_NUMBER');
const _password = String.fromEnvironment('TKG_TEST_PASSWORD');

void main() {
  IntegrationTestWidgetsFlutterBinding.ensureInitialized();

  testWidgets(
    'real Web operator contract stays device-bound and server-authoritative',
    (tester) async {
      expect(_apiBaseUrl, isNotEmpty);
      expect(_deviceCode, isNotEmpty);
      expect(_employeeNumber, isNotEmpty);
      expect(_password, isNotEmpty);

      var checks = 0;
      void check(Object? actual, Object? matcher, String reason) {
        expect(actual, matcher, reason: reason);
        checks++;
      }

      final store = WebDeviceCodeStore();
      await store.clear();
      web.window.sessionStorage.clear();
      final first = _createController(store);
      await first.initialize();
      await tester.pumpWidget(TkgPdaApp(controller: first));
      await tester.pumpAndSettle();

      check(
        first.stage,
        AppStage.deviceSetup,
        'first launch uses Device Setup',
      );
      check(
        find.text('TKG Operator · Cihaz Kurulumu'),
        findsOneWidget,
        'setup UI',
      );
      await tester.enterText(
        find.byKey(const Key('device-code-field')),
        _deviceCode,
      );
      await tester.tap(find.byKey(const Key('save-device-code')));
      await tester.pumpAndSettle();
      check(first.stage, AppStage.login, 'provisioning opens login');
      check(await store.read(), _deviceCode, 'Device Code persisted');

      await tester.enterText(
        find.byKey(const Key('employee-number-field')),
        _employeeNumber,
      );
      await tester.enterText(
        find.byKey(const Key('password-field')),
        _password,
      );
      await tester.tap(find.byKey(const Key('login-button')));
      await tester.pumpAndSettle();
      check(first.isAuthenticated, isTrue, 'real login succeeded');
      check(first.stage, AppStage.machines, 'login opens authorized machines');
      check(first.machines.length, 1, 'only one permitted machine is returned');
      check(
        first.machines.single.code,
        'SH-01-A',
        'machine permission is authoritative',
      );

      final localKeys = <String>[
        for (var index = 0; index < web.window.localStorage.length; index++)
          ?web.window.localStorage.key(index),
      ];
      final localValues = <String>[
        for (final key in localKeys) web.window.localStorage.getItem(key) ?? '',
      ];
      check(localKeys, [
        DeviceCodeStore.persistentKey,
      ], 'only Device Code key persists');
      check(
        web.window.sessionStorage.length,
        0,
        'sessionStorage remains empty',
      );
      check(
        localValues.any((value) => value.contains(_password)),
        isFalse,
        'password absent',
      );
      check(
        localValues.any((value) => value.startsWith('eyJ')),
        isFalse,
        'JWT absent',
      );

      await first.selectMachine(first.machines.single);
      await tester.pumpAndSettle();
      final context = first.context!;
      check(
        first.stage,
        AppStage.operation,
        'machine selection opens operation',
      );
      check(context.machine.code, 'SH-01-A', 'context stays in machine scope');
      check(context.activeVariantId, isNotNull, 'active variant projected');
      check(context.productGroups, isNotEmpty, 'product groups projected');
      check(
        context.productGroups.first.parts,
        isNotEmpty,
        'alt parts projected',
      );

      // WP-03B: the operation view now gates behind part-first selection.
      // Resolve the active variant's own main part through the real
      // part-options/compatible-variants endpoints (preferring the
      // already-active variant if the part resolves to more than one, so
      // this step does not itself trigger an unintended variant switch).
      await first.selectEffectivePart(
        EffectivePartSelection(
          partId: context.productGroups.first.mainPartId,
          partCode: context.productGroups.first.mainPartCode,
          isMain: true,
        ),
      );
      await tester.pumpAndSettle();
      if (first.needsPartSelection) {
        final variants = first.compatibleVariants ?? const [];
        final match = variants.firstWhere(
          (option) => option.variantId == context.activeVariantId,
          orElse: () => variants.first,
        );
        await first.selectCompatibleVariant(match);
        await tester.pumpAndSettle();
      }
      check(
        first.needsPartSelection,
        isFalse,
        'part-first resolves before entering the operation view',
      );

      check(
        context.slots.any((slot) => slot.isMachineRegion),
        isTrue,
        'MACHINE rendered',
      );
      check(
        context.slots
            .where((slot) => slot.isMachineRegion)
            .every((slot) => !slot.transportEligible),
        isTrue,
        'MACHINE remains non-transport',
      );
      check(
        context.variantLayouts.length,
        greaterThanOrEqualTo(2),
        'variant options projected',
      );
      check(
        context.machine.permissions.canChangeVariant,
        isTrue,
        'variant switch capability projected',
      );
      final originalVariant = context.variantLayouts.firstWhere(
        (option) =>
            option.variantId == context.activeVariantId &&
            option.layoutId == context.activeLayoutId,
      );
      final alternateVariant = context.variantLayouts.firstWhere(
        (option) => option.variantId != context.activeVariantId,
      );
      await first.switchVariant(alternateVariant);
      await tester.pumpAndSettle();
      check(
        first.context!.activeVariantId,
        alternateVariant.variantId,
        'variant switch reached the real API',
      );
      await first.switchVariant(originalVariant);
      await tester.pumpAndSettle();
      check(
        first.context!.activeVariantId,
        originalVariant.variantId,
        'variant restored through the real API',
      );

      first.onPaused();
      check(first.mutationEnabled, isFalse, 'background disables mutations');
      final resumed = first.onResumed();
      check(
        first.resumeValidationPending,
        isTrue,
        'resume starts server revalidation',
      );
      check(
        first.mutationEnabled,
        isFalse,
        'mutations stay disabled during resume validation',
      );
      await resumed;
      await tester.pumpAndSettle();
      check(
        first.mutationEnabled,
        isTrue,
        'resume refetch completes before mutations re-enable',
      );

      final vacant = context.slots.firstWhere(
        (slot) => slot.transportEligible && slot.occupancyState == 'VACANT',
      );
      final empty = context.slots.firstWhere(
        (slot) =>
            slot.transportEligible &&
            slot.occupancyState == 'OCCUPIED' &&
            slot.loadState == 'EMPTY',
      );
      final full = context.slots.firstWhere(
        (slot) =>
            slot.transportEligible &&
            slot.occupancyState == 'OCCUPIED' &&
            slot.loadState == 'FULL',
      );
      check(
        _enabled(vacant, 'BRING_EMPTY'),
        isTrue,
        'BRING_EMPTY comes from API',
      );
      check(
        _enabled(empty, 'REMOVE_EMPTY'),
        isTrue,
        'REMOVE_EMPTY comes from API',
      );
      check(
        _enabled(full, 'REMOVE_FULL'),
        isTrue,
        'REMOVE_FULL comes from API',
      );
      check(
        _enabledLoad(empty, 'MARK_FULL'),
        isTrue,
        'Kasa Doldu comes from API',
      );
      check(
        _enabledLoad(full, 'MARK_EMPTY'),
        isTrue,
        'Kasa Boş comes from API',
      );

      first.selectSlot(
        context.slots.firstWhere((slot) => slot.isMachineRegion),
      );
      check(first.selectedSlot, isNull, 'MACHINE cannot be selected');
      first.selectSlot(vacant);
      await tester.pump();
      await _reveal(tester, find.byKey(const Key('action-BRING_EMPTY')));
      check(
        find.byKey(const Key('action-BRING_EMPTY')),
        findsOneWidget,
        'action visible',
      );
      await tester.tap(find.byKey(const Key('action-BRING_EMPTY')));
      await tester.pumpAndSettle();
      check(
        find.text('Taşıma Talebini Onaylayın'),
        findsOneWidget,
        'confirmation opens',
      );
      await tester.tap(find.byKey(const Key('confirm-transport')));
      await tester.pumpAndSettle();
      check(
        first.context!.activeTransportRequests.length,
        1,
        'one request created',
      );
      check(
        first.context!.activeTransportRequests.single.operation,
        'BRING_EMPTY',
        'submitted operation preserved',
      );

      await _reveal(
        tester,
        find.byKey(
          Key(
            'cancel-${first.context!.activeTransportRequests.single.requestId}',
          ),
        ),
      );
      await tester.tap(
        find.byKey(
          Key(
            'cancel-${first.context!.activeTransportRequests.single.requestId}',
          ),
        ),
      );
      await tester.pumpAndSettle();
      check(
        find.text('Talebi iptal et'),
        findsOneWidget,
        'cancel confirmation opens',
      );
      await tester.tap(find.byKey(const Key('confirm-cancel')));
      await tester.pumpAndSettle();
      check(
        first.context!.activeTransportRequests,
        isEmpty,
        'cancelled request disappears',
      );

      first.handleUnauthorized();
      await tester.pumpAndSettle();
      check(first.isAuthenticated, isFalse, '401 clears memory session');
      check(first.context, isNull, '401 clears operational context');
      check(first.deviceCode, _deviceCode, '401 retains Device Code');
      check(
        find.text('TKG Operatör Girişi'),
        findsOneWidget,
        '401 returns the browser to login',
      );

      final second = _createController(store);
      await second.initialize();
      await tester.pumpWidget(TkgPdaApp(controller: second));
      await tester.pumpAndSettle();
      check(second.deviceCode, _deviceCode, 'reload retains Device Code');
      check(second.isAuthenticated, isFalse, 'reload loses JWT');
      check(second.stage, AppStage.login, 'reload returns to login');
      check(
        web.window.sessionStorage.length,
        0,
        'reload leaves sessionStorage empty',
      );
      check(checks, 48, 'all frozen Phase 09 browser checks ran');

      first.dispose();
      second.dispose();
    },
  );
}

AppController _createController(DeviceCodeStore store) {
  final session = SessionMemory();
  late final AppController controller;
  final client = ApiClient(
    baseUri: Uri.parse(_apiBaseUrl),
    deviceCode: () => controller.deviceCode,
    accessToken: () => session.accessToken,
    onUnauthorized: () => controller.handleUnauthorized(),
  );
  controller = AppController(
    repository: PdaRepository(client),
    deviceCodeStore: store,
    session: session,
    enablePolling: false,
  );
  return controller;
}

bool _enabled(LayoutSlot slot, String action) => slot.availableActions.any(
  (candidate) => candidate.action == action && candidate.enabled,
);

bool _enabledLoad(LayoutSlot slot, String action) => slot.loadStateActions.any(
  (candidate) => candidate.action == action && candidate.enabled,
);

Future<void> _reveal(WidgetTester tester, Finder finder) async {
  final right = find.descendant(
    of: find.byKey(const Key('operation-wide-right')),
    matching: find.byType(Scrollable),
  );
  if (right.evaluate().isNotEmpty) {
    await tester.scrollUntilVisible(finder, 280, scrollable: right);
  } else {
    await tester.scrollUntilVisible(
      finder,
      280,
      scrollable: find.byType(Scrollable).last,
    );
  }
  await tester.pumpAndSettle();
}
