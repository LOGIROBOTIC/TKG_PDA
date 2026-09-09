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
        'machine selection opens combined part and action workspace',
      );
      check(context.machine.code, 'SH-01-A', 'context stays in machine scope');
      check(context.activeVariantId, isNotNull, 'active variant projected');
      check(context.productGroups, isNotEmpty, 'product groups projected');
      check(
        context.productGroups.first.parts,
        isNotEmpty,
        'alt parts projected',
      );

      // Resolve the active variant's own main part through the real
      // part-options/compatible-variants endpoints (preferring the
      // already-active variant if the part resolves to more than one, so
      // this step does not itself trigger an unintended variant switch).
      await first.selectMainPartOption(
        first.partOptions.firstWhere(
          (option) => option.partId == context.productGroups.first.mainPartId,
        ),
      );
      await tester.pumpAndSettle();
      if (!first.transportActionsReady) {
        final variants = first.compatibleVariants ?? const [];
        final match = variants.firstWhere(
          (option) => option.variantId == context.activeVariantId,
          orElse: () => variants.first,
        );
        await first.selectCompatibleVariant(match);
        await tester.pumpAndSettle();
      }
      check(
        first.transportActionsReady,
        isTrue,
        'part and compatible variant expose operations on the same page',
      );
      check(
        find.text('İşleme Devam Et'),
        findsNothing,
        'no forward-navigation step remains',
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
      // A deliberate variant change invalidates the previous part choice.
      // Re-resolve it before exercising the production operator flow.
      await first.selectMainPartOption(
        first.partOptions.firstWhere(
          (option) => option.partId == context.productGroups.first.mainPartId,
        ),
      );
      await tester.pumpAndSettle();
      if (!first.transportActionsReady) {
        final variants = first.compatibleVariants ?? const [];
        await first.selectCompatibleVariant(
          variants.firstWhere(
            (option) => option.variantId == originalVariant.variantId,
            orElse: () => variants.first,
          ),
        );
        await tester.pumpAndSettle();
      }

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
      // REMOVE_EMPTY is internal orchestration vocabulary generated by the
      // Remove-All workflow, not a standalone Operator action, so the API must not
      // offer it for this slot even though the slot is OCCUPIED+EMPTY — exactly the
      // state it would apply to.
      check(
        _enabled(empty, 'REMOVE_EMPTY'),
        isFalse,
        'REMOVE_EMPTY is not offered as an Operator action',
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

      check(
        find.text('Fiziksel Yerleşim'),
        findsNothing,
        'physical layout is absent from the normal Operator workspace',
      );
      check(
        find.byKey(Key('slot-${vacant.id}')),
        findsNothing,
        'physical slot is not exposed as an Operator control',
      );
      check(
        find.byKey(const Key('slot-details')),
        findsNothing,
        'legacy physical-slot detail panel is absent',
      );
      await _reveal(tester, find.byKey(const Key('direct-action-BRING_EMPTY-1')));
      check(
        find.byKey(const Key('direct-action-BRING_EMPTY-1')),
        findsOneWidget,
        'part-first operation visible',
      );
      await tester.tap(find.byKey(const Key('direct-action-BRING_EMPTY-1')));
      await tester.pumpAndSettle();
      // WP-33: a container action submits directly. The pre-WP-33 per-operation
      // confirmation dialog is gone, so neither its text nor its confirm button may
      // appear, and the request must already exist after the single tap.
      check(
        find.text('Boş kasa getirmek istiyor musunuz?'),
        findsNothing,
        'no stale per-operation confirmation dialog',
      );
      check(
        find.byKey(const Key('confirm-direct-transport')),
        findsNothing,
        'container action is direct-submit under WP-33',
      );
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
      // Physical routing stays a backend decision. The client never sent a slot —
      // the request contract has no such field — yet the created request carries a
      // server-resolved destination, which is what proves the resolution happened
      // on the server rather than being skipped.
      check(
        first.context!.activeTransportRequests.single.destinationSlotId,
        isNotNull,
        'destination was resolved by the backend, not chosen by the client',
      );
      // Tapping logical container 1 must not also fire container 2. With the old
      // shared key a finder matched every container card at once, so this is the
      // business-level guard for that regression.
      check(
        find.byKey(const Key('direct-action-BRING_EMPTY-2')),
        findsOneWidget,
        'a second logical container is rendered, so the keys must stay distinct',
      );
      // The harness seeds logical container 2 as OCCUPIED+EMPTY, so its bring-empty
      // is correctly inert — and that is exactly the state REMOVE_EMPTY applies to.
      // It is still not offered, which is the WP-33 rule checked at the one position
      // where a regression would actually surface.
      check(
        tester
            .widget<ElevatedButton>(
              find.byKey(const Key('direct-action-BRING_EMPTY-2')),
            )
            .onPressed,
        isNull,
        'an occupied logical container offers no bring-empty',
      );
      check(
        find.byKey(const Key('direct-action-REMOVE_EMPTY-2')),
        findsNothing,
        'REMOVE_EMPTY is never offered, not even on an OCCUPIED+EMPTY container',
      );
      await _reveal(tester, find.byKey(const Key('direct-action-BRING_EMPTY-1')));
      check(
        tester
            .widget<ElevatedButton>(
              find.byKey(const Key('direct-action-BRING_EMPTY-1')),
            )
            .onPressed,
        isNull,
        'active request prevents repeated submission',
      );
      // WP-33 feedback names the logical container that was acted on. The old text
      // 'Taşıma talebi oluşturuldu.' belongs to the pre-WP-33 submitTransport path;
      // a container action reports '<label> talebi oluşturuldu.'. Asserting the
      // labelled form also proves the tap hit container 1 and not a sibling.
      check(
        find.text('1. Kasa talebi oluşturuldu.'),
        findsOneWidget,
        'successful creation names the logical container in Turkish',
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
      // Frozen count so a silently skipped assertion is itself a failure. Raised
      // from 53 to 58 for the WP-33 alignment: the confirmation dialog check became
      // two (no dialog text, no confirm button) and four were added — backend-resolved
      // destination, the distinct second container key, that container's inert
      // bring-empty, and REMOVE_EMPTY absent on an OCCUPIED+EMPTY container.
      check(checks, 58, 'all frozen browser checks ran');

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
  // Discover the target in whichever scrollable currently owns the combined
  // machine/part/action workspace.
  if (finder.evaluate().isNotEmpty) {
    try {
      await tester.ensureVisible(finder);
      await tester.pumpAndSettle();
      return;
    } catch (_) {
      // Fall through to explicit scrollable discovery below.
    }
  }
  final scrollableCount = find.byType(Scrollable).evaluate().length;
  for (var index = 0; index < scrollableCount; index++) {
    try {
      await tester.scrollUntilVisible(
        finder,
        280,
        scrollable: find.byType(Scrollable).at(index),
      );
      await tester.pumpAndSettle();
      if (finder.evaluate().isNotEmpty) return;
    } catch (_) {
      // Not in this scrollable — try the next one.
    }
  }
}
