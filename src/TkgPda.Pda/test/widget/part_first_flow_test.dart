import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:tkg_pda/app/app.dart';
import 'package:tkg_pda/app/app_controller.dart';
import 'package:tkg_pda/core/auth/session_memory.dart';
import 'package:tkg_pda/core/errors/api_problem.dart';
import 'package:tkg_pda/core/storage/device_code_store.dart';
import 'package:tkg_pda/data/models/pda_models.dart';

import '../support/fakes.dart';

// WP-03B — Flutter part-first Operator flow: Authorized Machine -> Main Part
// -> Related/Alt Part -> Compatible Variant -> existing operation flow.
void main() {
  testWidgets('selecting a machine loads its part-options', (tester) async {
    final repository = FakePdaRepository();
    final harness = await _harness(tester, repository: repository);

    expect(repository.partOptionsCount, greaterThanOrEqualTo(1));
    expect(harness.controller.partOptions, isNotEmpty);
    harness.dispose();
  });

  testWidgets('main parts render as selectable chips', (tester) async {
    final harness = await _harness(tester);

    expect(
      find.byKey(Key('main-part-${testMainPartOption.partId}')),
      findsOneWidget,
    );
    expect(
      find.byKey(Key('main-part-${testSecondMainPartOption.partId}')),
      findsOneWidget,
    );
    harness.dispose();
  });

  testWidgets(
    'one click on a main part makes it the effective part and starts the '
    'compatible-variant lookup, with no second confirmation chip',
    (tester) async {
      final repository = FakePdaRepository();
      final harness = await _harness(tester, repository: repository);

      await tester.tap(
        find.byKey(Key('main-part-${testAmbiguousMainPartOption.partId}')),
      );
      await tester.pumpAndSettle();

      expect(harness.controller.selectedEffectivePart?.isMain, isTrue);
      expect(
        harness.controller.selectedEffectivePart?.partId,
        testAmbiguousMainPartOption.partId,
      );
      expect(
        repository.compatibleVariantsRequestedPartIds,
        contains(testAmbiguousMainPartOption.partId),
      );
      // The redundant "Ana Parçayı Kullan" step is gone entirely.
      expect(find.byKey(const Key('effective-part-main')), findsNothing);
      expect(find.textContaining('Ana Parçayı Kullan'), findsNothing);
      harness.dispose();
    },
  );

  testWidgets('the clicked main part is visibly marked as selected', (
    tester,
  ) async {
    final harness = await _harness(tester);

    await tester.tap(
      find.byKey(Key('main-part-${testAmbiguousMainPartOption.partId}')),
    );
    await tester.pumpAndSettle();

    final selectedChip = tester.widget<ChoiceChip>(
      find.byKey(Key('main-part-${testAmbiguousMainPartOption.partId}')),
    );
    final otherChip = tester.widget<ChoiceChip>(
      find.byKey(Key('main-part-${testSecondMainPartOption.partId}')),
    );
    expect(selectedChip.selected, isTrue);
    expect(otherChip.selected, isFalse);
    harness.dispose();
  });

  testWidgets(
    'selecting a main part shows only its own contextual RELATED/ALT options',
    (tester) async {
      final harness = await _harness(tester);

      await tester.tap(
        find.byKey(Key('main-part-${testAmbiguousMainPartOption.partId}')),
      );
      await tester.pumpAndSettle();

      expect(find.byKey(Key('alt-part-$testAltPartId')), findsOneWidget);
      expect(find.byKey(Key('related-part-$testRelatedPartId')), findsOneWidget);
      // testSecondMainPartOption has no related parts of its own — nothing
      // from an unrelated main part may leak into the selected one's card.
      expect(
        find.byKey(Key('main-part-detail-${testSecondMainPartOption.partId}')),
        findsNothing,
      );
      harness.dispose();
    },
  );

  testWidgets(
    'selecting another main part replaces the previous contextual options',
    (tester) async {
      final harness = await _harness(tester);

      await tester.tap(
        find.byKey(Key('main-part-${testAmbiguousMainPartOption.partId}')),
      );
      await tester.pumpAndSettle();
      expect(find.byKey(Key('alt-part-$testAltPartId')), findsOneWidget);

      await tester.tap(
        find.byKey(Key('main-part-${testSecondMainPartOption.partId}')),
      );
      await tester.pumpAndSettle();

      expect(
        harness.controller.selectedEffectivePart?.partId,
        testSecondMainPartOption.partId,
      );
      expect(
        find.byKey(
          Key('main-part-detail-${testSecondMainPartOption.partId}'),
        ),
        findsOneWidget,
      );
      // The previous main part's alternatives are gone.
      expect(find.byKey(Key('alt-part-$testAltPartId')), findsNothing);
      expect(find.byKey(Key('related-part-$testRelatedPartId')), findsNothing);
      harness.dispose();
    },
  );

  testWidgets(
    'selecting a RELATED part changes the effective part and keeps the parent '
    'main-part context visible',
    (tester) async {
      final harness = await _harness(tester);
      await tester.tap(
        find.byKey(Key('main-part-${testAmbiguousMainPartOption.partId}')),
      );
      await tester.pumpAndSettle();

      // Uses the RELATED part (resolves to 2 variants) rather than ALT (which
      // auto-resolves to a single variant and would unmount this screen).
      await tester.ensureVisible(find.byKey(Key('related-part-$testRelatedPartId')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(Key('related-part-$testRelatedPartId')));
      await tester.pumpAndSettle();

      expect(harness.controller.selectedEffectivePart?.partId, testRelatedPartId);
      expect(harness.controller.selectedEffectivePart?.isMain, isFalse);
      final relatedChip = tester.widget<ChoiceChip>(
        find.byKey(Key('related-part-$testRelatedPartId')),
      );
      expect(relatedChip.selected, isTrue);
      // Parent context stays on screen while the alternative is in use.
      expect(
        find.byKey(
          Key('main-part-detail-${testAmbiguousMainPartOption.partId}'),
        ),
        findsOneWidget,
      );
      expect(
        tester
            .widget<Text>(find.byKey(const Key('effective-part-summary')))
            .data,
        contains('715B3CN500-60'),
      );
      harness.dispose();
    },
  );

  testWidgets(
    'exactly one compatible variant auto-resolves on the first main-part click',
    (tester) async {
      final repository = FakePdaRepository();
      final harness = await _harness(tester, repository: repository);
      await tester.tap(
        find.byKey(Key('main-part-${testMainPartOption.partId}')),
      );
      await tester.pumpAndSettle();

      expect(harness.controller.needsPartSelection, isFalse);
      expect(harness.controller.context?.activeVariantId, testVariantV1Id);
      expect(repository.switchCount, 0); // already active — no switch needed
      expect(find.byKey(const Key('operation-content')), findsOneWidget);
      harness.dispose();
    },
  );

  testWidgets(
    'RELATED part compatibility lookup uses the compatible-variants endpoint',
    (tester) async {
      final repository = FakePdaRepository();
      final harness = await _harness(tester, repository: repository);
      await tester.tap(
        find.byKey(Key('main-part-${testAmbiguousMainPartOption.partId}')),
      );
      await tester.pumpAndSettle();

      await _scrollTo(tester, Key('related-part-$testRelatedPartId'));
      await tester.tap(find.byKey(Key('related-part-$testRelatedPartId')));
      await tester.pumpAndSettle();

      expect(
        repository.compatibleVariantsRequestedPartIds,
        contains(testRelatedPartId),
      );
      await _scrollTo(tester, const Key('multiple-compatible-variants'));
      expect(find.byKey(const Key('multiple-compatible-variants')), findsOneWidget);
      harness.dispose();
    },
  );

  testWidgets(
    'ALT part compatibility lookup resolves a different variant via the '
    'existing variant-selection mechanism',
    (tester) async {
      final repository = FakePdaRepository();
      final harness = await _harness(tester, repository: repository);
      await tester.tap(
        find.byKey(Key('main-part-${testAmbiguousMainPartOption.partId}')),
      );
      await tester.pumpAndSettle();

      await tester.tap(find.byKey(Key('alt-part-$testAltPartId')));
      await tester.pumpAndSettle();

      expect(
        repository.compatibleVariantsRequestedPartIds,
        contains(testAltPartId),
      );
      expect(harness.controller.selectedEffectivePart?.partId, testAltPartId);
      expect(repository.switchCount, 1);
      expect(repository.lastSwitchVariantId, testVariantV2Id);
      expect(harness.controller.needsPartSelection, isFalse);
      harness.dispose();
    },
  );

  testWidgets('zero compatible variants shows a non-destructive empty state', (
    tester,
  ) async {
    final harness = await _harness(tester);
    await tester.tap(
      find.byKey(Key('main-part-${testAmbiguousMainPartOption.partId}')),
    );
    await tester.pumpAndSettle();

    await _scrollTo(tester, Key('related-part-$testOrphanPartId'));
    await tester.tap(find.byKey(Key('related-part-$testOrphanPartId')));
    await tester.pumpAndSettle();

    await _scrollTo(tester, const Key('zero-compatible-variants'));
    expect(find.byKey(const Key('zero-compatible-variants')), findsOneWidget);
    expect(find.text('Uyumlu varyant bulunamadı.'), findsOneWidget);
    // Non-destructive: still on the gate, free to pick a different part.
    expect(harness.controller.needsPartSelection, isTrue);
    expect(find.byKey(Key('alt-part-$testAltPartId')), findsOneWidget);
    harness.dispose();
  });

  testWidgets('multiple compatible variants require explicit user selection', (
    tester,
  ) async {
    final repository = FakePdaRepository();
    final harness = await _harness(tester, repository: repository);
    await tester.tap(
      find.byKey(Key('main-part-${testAmbiguousMainPartOption.partId}')),
    );
    await tester.pumpAndSettle();

    await _scrollTo(tester, Key('compatible-variant-$testVariantV1Id'));
    expect(find.byKey(Key('compatible-variant-$testVariantV1Id')), findsOneWidget);
    expect(find.byKey(Key('compatible-variant-$testVariantV2Id')), findsOneWidget);

    await tester.ensureVisible(find.byKey(Key('compatible-variant-$testVariantV2Id')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(Key('compatible-variant-$testVariantV2Id')));
    await tester.pumpAndSettle();

    expect(repository.switchCount, 1);
    expect(repository.lastSwitchVariantId, testVariantV2Id);
    expect(harness.controller.needsPartSelection, isFalse);
    harness.dispose();
  });

  testWidgets('changing machine clears part/variant selection state', (
    tester,
  ) async {
    final secondMachine = MachineSummary(
      id: '30000000-0000-0000-0000-000000000002',
      code: 'SH-01-B',
      displayName: 'SH-01-B Hücresi',
      displayOrder: 2,
      permissions: const MachinePermissions(
        canView: true,
        canOperate: true,
        canChangeVariant: true,
        canManageSlots: false,
      ),
    );
    final repository = FakePdaRepository()
      ..machineValues = [testMachine, secondMachine];
    final harness = await _harness(tester, repository: repository);

    await tester.tap(find.byKey(Key('main-part-${testMainPartOption.partId}')));
    await tester.pumpAndSettle();
    expect(harness.controller.selectedMainPart, isNotNull);

    harness.controller.leaveMachine();
    await tester.pumpAndSettle();
    await harness.controller.selectMachine(secondMachine);
    await tester.pumpAndSettle();

    expect(harness.controller.selectedMainPart, isNull);
    expect(harness.controller.selectedEffectivePart, isNull);
    expect(harness.controller.compatibleVariants, isNull);
    expect(harness.controller.needsPartSelection, isTrue);
    harness.dispose();
  });

  testWidgets('part-options failure shows retry and recovers', (tester) async {
    final repository = FakePdaRepository()
      ..partOptionsError = const ApiProblem(
        status: 503,
        code: 'DATABASE_UNAVAILABLE',
        detail: '',
        correlationId: null,
      );
    final harness = await _harness(tester, repository: repository);

    expect(find.byKey(const Key('part-options-retry')), findsOneWidget);

    repository.partOptionsError = null;
    await tester.tap(find.byKey(const Key('part-options-retry')));
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('part-first-selector')), findsOneWidget);
    expect(find.byKey(Key('main-part-${testMainPartOption.partId}')), findsOneWidget);
    harness.dispose();
  });

  testWidgets('compatible-variants failure shows retry and recovers', (
    tester,
  ) async {
    final repository = FakePdaRepository()
      ..compatibleVariantsError = const ApiProblem(
        status: 503,
        code: 'DATABASE_UNAVAILABLE',
        detail: '',
        correlationId: null,
      );
    final harness = await _harness(tester, repository: repository);
    await tester.tap(find.byKey(Key('main-part-${testMainPartOption.partId}')));
    await tester.pumpAndSettle();

    await _scrollTo(tester, const Key('compatible-variants-retry'));
    expect(find.byKey(const Key('compatible-variants-retry')), findsOneWidget);

    repository.compatibleVariantsError = null;
    await tester.ensureVisible(find.byKey(const Key('compatible-variants-retry')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('compatible-variants-retry')));
    await tester.pumpAndSettle();

    expect(harness.controller.needsPartSelection, isFalse);
    harness.dispose();
  });

  testWidgets('authorized machine selection still opens the operation screen', (
    tester,
  ) async {
    final harness = await _harness(tester);

    expect(harness.controller.selectedMachine?.id, testMachine.id);
    expect(find.text('${testMachine.code} · Operasyon'), findsOneWidget);
    harness.dispose();
  });

  testWidgets(
    'existing transport actions remain reachable after part-first resolves '
    'the variant',
    (tester) async {
      final harness = await _harness(tester);
      await tester.tap(
        find.byKey(Key('main-part-${testMainPartOption.partId}')),
      );
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('operation-content')), findsOneWidget);
      expect(find.byKey(const Key('slot-slot-vacant')), findsOneWidget);
      harness.dispose();
    },
  );

  testWidgets('the "Parça Değiştir" control returns to the part-first gate', (
    tester,
  ) async {
    final harness = await _harness(tester);
    await tester.tap(find.byKey(Key('main-part-${testMainPartOption.partId}')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('operation-content')), findsOneWidget);

    await tester.tap(find.byKey(const Key('change-part-button')));
    await tester.pumpAndSettle();

    expect(harness.controller.needsPartSelection, isTrue);
    expect(find.byKey(const Key('part-first-selector')), findsOneWidget);
    // Operational context/slots are not lost — just the part-first gate reopens.
    expect(harness.controller.context, isNotNull);
    harness.dispose();
  });
}

/// Scrolls the part-first list until [key] is built and visible. The card
/// stack (main chips -> alternatives -> compatible variants) can extend past
/// the test viewport, so lower sections are not built until scrolled to.
Future<void> _scrollTo(WidgetTester tester, Key key) async {
  await tester.scrollUntilVisible(
    find.byKey(key),
    300,
    scrollable: find.descendant(
      of: find.byKey(const Key('part-first-selector')),
      matching: find.byType(Scrollable),
    ),
  );
  await tester.pumpAndSettle();
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
  await controller.login('employee-1');
  await tester.pumpAndSettle();
  await controller.selectMachine(testMachine);
  await tester.pumpAndSettle();
  return _Harness(controller, actualRepository);
}

class _Harness {
  const _Harness(this.controller, this.repository);

  final AppController controller;
  final FakePdaRepository repository;

  void dispose() => controller.dispose();
}
