import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:tkg_pda/app/app.dart';
import 'package:tkg_pda/app/app_controller.dart';
import 'package:tkg_pda/core/auth/session_memory.dart';
import 'package:tkg_pda/core/errors/api_problem.dart';
import 'package:tkg_pda/core/storage/device_code_store.dart';
import 'package:tkg_pda/data/models/pda_models.dart';

import '../support/fakes.dart';

// Customer UI — Multi-Part Transport Batch: multiple effective parts
// selected on the existing part-first flow, each independently resolved to
// its own variant/slot/operation, then submitted as one TransportBatch (or,
// for exactly one item, through the existing single-request path).
void main() {
  testWidgets('selecting multiple parts adds them all to the cart', (
    tester,
  ) async {
    final harness = await _harness(tester);
    await _enterBatchMode(tester);

    await _expandMain(tester, testMainPartOption);
    await _addPart(tester, 'batch-add-main-${testMainPartOption.partId}');
    await _addPart(tester, 'batch-add-alt-$testAltPartId');

    expect(harness.controller.batchCart.length, 2);
    await _scrollUntilVisible(
      tester,
      Key('batch-cart-chip-${testMainPartOption.partId}'),
    );
    expect(
      find.byKey(Key('batch-cart-chip-${testMainPartOption.partId}')),
      findsOneWidget,
    );
    expect(find.byKey(Key('batch-cart-chip-$testAltPartId')), findsOneWidget);
    harness.dispose();
  });

  testWidgets('browsing another main part does not clear the selection', (
    tester,
  ) async {
    final harness = await _harness(tester);
    await _enterBatchMode(tester);
    await _expandMain(tester, testMainPartOption);
    await _addPart(tester, 'batch-add-main-${testMainPartOption.partId}');

    // Browse a completely different main part.
    await _expandMain(tester, testSecondMainPartOption);

    expect(harness.controller.batchCart.length, 1);
    await _scrollUntilVisible(
      tester,
      Key('batch-cart-chip-${testMainPartOption.partId}'),
    );
    expect(
      find.byKey(Key('batch-cart-chip-${testMainPartOption.partId}')),
      findsOneWidget,
    );
    harness.dispose();
  });

  testWidgets('the cart shows a visible count and marks added parts', (
    tester,
  ) async {
    final harness = await _harness(tester);
    await _enterBatchMode(tester);
    await _scrollUntilVisible(tester, const Key('batch-cart-count'));
    expect(
      tester.widget<Text>(find.byKey(const Key('batch-cart-count'))).data,
      contains('0'),
    );

    await _expandMain(tester, testMainPartOption);
    await _addPart(tester, 'batch-add-main-${testMainPartOption.partId}');

    await _scrollUntilVisible(tester, const Key('batch-cart-count'));
    expect(
      tester.widget<Text>(find.byKey(const Key('batch-cart-count'))).data,
      contains('1'),
    );
    await _scrollUntilVisible(
      tester,
      Key('batch-add-main-${testMainPartOption.partId}'),
    );
    final addedChip = tester.widget<ActionChip>(
      find.descendant(
        of: find.byKey(Key('batch-add-main-${testMainPartOption.partId}')),
        matching: find.byType(ActionChip),
      ),
    );
    expect(addedChip.onPressed, isNull);
    harness.dispose();
  });

  testWidgets(
    'the main-part browse chip shows a count badge once one of its parts is added',
    (tester) async {
      final harness = await _harness(tester);
      await _enterBatchMode(tester);

      expect(
        find.byKey(Key('batch-browse-main-count-${testMainPartOption.partId}')),
        findsNothing,
      );

      await _expandMain(tester, testMainPartOption);
      await _addPart(tester, 'batch-add-main-${testMainPartOption.partId}');
      await _addPart(tester, 'batch-add-alt-$testAltPartId');

      await _scrollUntilVisible(
        tester,
        Key('batch-browse-main-count-${testMainPartOption.partId}'),
      );
      expect(
        tester
            .widget<Text>(
              find.descendant(
                of: find.byKey(
                  Key('batch-browse-main-count-${testMainPartOption.partId}'),
                ),
                matching: find.byType(Text),
              ),
            )
            .data,
        '2',
      );
      harness.dispose();
    },
  );

  testWidgets(
    'the AppBar title shows how many parts are selected while in batch mode',
    (tester) async {
      final harness = await _harness(tester);
      expect(find.text('${testMachine.code} · 0 Parça Seçili'), findsNothing);

      await _enterBatchMode(tester);
      expect(find.text('${testMachine.code} · 0 Parça Seçili'), findsOneWidget);

      await _expandMain(tester, testMainPartOption);
      await _addPart(tester, 'batch-add-main-${testMainPartOption.partId}');
      expect(find.text('${testMachine.code} · 1 Parça Seçili'), findsOneWidget);
      harness.dispose();
    },
  );

  testWidgets(
    'the configuration progress dots reflect which cart items are done',
    (tester) async {
      final harness = await _harness(tester);
      await _enterBatchMode(tester);
      await _expandMain(tester, testMainPartOption);
      await _addPart(tester, 'batch-add-main-${testMainPartOption.partId}');
      await _addPart(tester, 'batch-add-alt-$testAltPartId');

      await _scrollUntilVisible(tester, const Key('batch-configure-start'));
      await tester.tap(find.byKey(const Key('batch-configure-start')));
      await tester.pumpAndSettle();

      await _scrollUntilVisible(tester, const Key('batch-progress-dots'));
      expect(find.byKey(const Key('batch-progress-dot-0')), findsOneWidget);
      expect(find.byKey(const Key('batch-progress-dot-1')), findsOneWidget);
      expect(harness.controller.batchCart[0].isConfigured, isFalse);

      await _selectSlot(tester, 'slot-vacant');
      await _tapAction(tester, 'action-BRING_EMPTY');
      await _confirmTransport(tester);

      expect(harness.controller.batchCart[0].isConfigured, isTrue);
      await _scrollUntilVisible(tester, const Key('batch-progress-dots'));
      expect(
        find.descendant(
          of: find.byKey(const Key('batch-progress-dot-0')),
          matching: find.byIcon(Icons.check),
        ),
        findsOneWidget,
      );
      harness.dispose();
    },
  );

  testWidgets('the same part cannot be added twice', (tester) async {
    final harness = await _harness(tester);
    await _enterBatchMode(tester);
    await _expandMain(tester, testMainPartOption);
    await _addPart(tester, 'batch-add-main-${testMainPartOption.partId}');

    // The chip is now disabled (already added); tapping it again is a no-op.
    await tester.tap(
      find.byKey(Key('batch-add-main-${testMainPartOption.partId}')),
    );
    await tester.pumpAndSettle();

    expect(harness.controller.batchCart.length, 1);
    expect(
      harness.controller.addPartToBatch(
        testMainPartOption,
        const EffectivePartSelection(
          partId: testMainPartId,
          partCode: '71550BN000-10-HAOS',
          isMain: true,
        ),
      ),
      isFalse,
    );
    expect(harness.controller.batchCart.length, 1);
    harness.dispose();
  });

  testWidgets('a selected part can be removed before submission', (
    tester,
  ) async {
    final harness = await _harness(tester);
    await _enterBatchMode(tester);
    await _expandMain(tester, testMainPartOption);
    await _addPart(tester, 'batch-add-main-${testMainPartOption.partId}');
    await _addPart(tester, 'batch-add-alt-$testAltPartId');
    expect(harness.controller.batchCart.length, 2);
    final chipKey = Key('batch-cart-chip-${testMainPartOption.partId}');
    await _scrollUntilVisible(tester, chipKey);
    final chip = tester.widget<InputChip>(find.byKey(chipKey));
    chip.onDeleted!();
    await tester.pumpAndSettle();

    expect(harness.controller.batchCart.length, 1);
    expect(
      harness.controller.batchCart.single.effectivePart.partId,
      testAltPartId,
    );
    harness.dispose();
  });

  testWidgets('machine change clears the entire batch selection', (
    tester,
  ) async {
    final harness = await _harness(tester);
    await _enterBatchMode(tester);
    await _expandMain(tester, testMainPartOption);
    await _addPart(tester, 'batch-add-main-${testMainPartOption.partId}');
    expect(harness.controller.batchCart, isNotEmpty);

    harness.controller.leaveMachine();
    await tester.pumpAndSettle();

    expect(harness.controller.batchMode, isFalse);
    expect(harness.controller.batchCart, isEmpty);
    harness.dispose();
  });

  testWidgets(
    'zero compatible variants makes a cart item invalid and blocks submission '
    'until it is removed',
    (tester) async {
      final harness = await _harness(tester);
      await _enterBatchMode(tester);
      await _expandMain(tester, testMainPartOption);
      await _addPart(tester, 'batch-add-main-${testMainPartOption.partId}');
      await _addPart(tester, 'batch-add-related-$testOrphanPartId');

      await _scrollUntilVisible(tester, const Key('batch-configure-start'));
      await tester.tap(find.byKey(const Key('batch-configure-start')));
      await tester.pumpAndSettle();

      // Item 1 (main -> V1, already active) auto-resolves and is configured
      // first; item 2 (orphan) has zero compatible variants.
      await _selectSlot(tester, 'slot-vacant');
      await _tapAction(tester, 'action-BRING_EMPTY');
      await _confirmTransport(tester);

      await _scrollUntilVisible(tester, const Key('zero-compatible-variants'));
      expect(find.byKey(const Key('zero-compatible-variants')), findsOneWidget);
      expect(
        find.byKey(const Key('skip-invalid-batch-item')),
        findsOneWidget,
      );
      expect(harness.controller.canReviewBatch, isFalse);

      await _scrollUntilVisible(tester, const Key('skip-invalid-batch-item'));
      await tester.tap(find.byKey(const Key('skip-invalid-batch-item')));
      await tester.pumpAndSettle();

      // The invalid item is gone; the one already-configured item can review.
      expect(harness.controller.batchCart.length, 1);
      expect(harness.controller.canReviewBatch, isTrue);
      harness.dispose();
    },
  );

  testWidgets(
    'each cart item resolves its own compatible variant independently: '
    'auto-resolve without a switch, auto-resolve with a switch, and a '
    'manual choice among several variants',
    (tester) async {
      final repository = FakePdaRepository();
      final harness = await _harness(tester, repository: repository);
      await _enterBatchMode(tester);
      await _expandMain(tester, testMainPartOption);
      await _addPart(tester, 'batch-add-main-${testMainPartOption.partId}');
      await _addPart(tester, 'batch-add-alt-$testAltPartId');
      await _addPart(tester, 'batch-add-related-$testRelatedPartId');

      await _scrollUntilVisible(tester, const Key('batch-configure-start'));
      await tester.tap(find.byKey(const Key('batch-configure-start')));
      await tester.pumpAndSettle();

      // Item 1: main part -> single variant, already active -> no switch.
      expect(harness.controller.needsPartSelection, isFalse);
      expect(harness.controller.context?.activeVariantId, testVariantV1Id);
      expect(repository.switchCount, 0);
      await _selectSlot(tester, 'slot-vacant');
      await _tapAction(tester, 'action-BRING_EMPTY');
      await _confirmTransport(tester);

      // Item 2: ALT part -> single variant, different -> auto-switch.
      expect(harness.controller.needsPartSelection, isFalse);
      expect(harness.controller.context?.activeVariantId, testVariantV2Id);
      expect(repository.switchCount, 1);
      await _selectSlot(tester, 'slot-full');
      await _tapAction(tester, 'action-REMOVE_FULL');
      await _confirmTransport(tester);

      // Item 3: RELATED part -> two variants -> requires an explicit choice.
      expect(harness.controller.needsPartSelection, isTrue);
      await _scrollUntilVisible(
        tester,
        const Key('multiple-compatible-variants'),
      );
      expect(
        find.byKey(const Key('multiple-compatible-variants')),
        findsOneWidget,
      );
      await _scrollUntilVisible(
        tester,
        Key('compatible-variant-$testVariantV1Id'),
      );
      await tester.tap(find.byKey(Key('compatible-variant-$testVariantV1Id')));
      await tester.pumpAndSettle();
      expect(harness.controller.needsPartSelection, isFalse);
      await _selectSlot(tester, 'slot-empty');
      await _tapAction(tester, 'action-REMOVE_EMPTY');
      await _confirmTransport(tester);

      expect(harness.controller.canReviewBatch, isTrue);
      final cart = harness.controller.batchCart;
      expect(cart[0].command!.variantId, testVariantV1Id);
      expect(cart[0].command!.operation, 'BRING_EMPTY');
      expect(cart[1].command!.variantId, testVariantV2Id);
      expect(cart[1].command!.operation, 'REMOVE_FULL');
      expect(cart[2].command!.variantId, testVariantV1Id);
      expect(cart[2].command!.operation, 'REMOVE_EMPTY');
      harness.dispose();
    },
  );

  testWidgets(
    'successful batch submission sends N independent items with unique '
    'idempotency keys',
    (tester) async {
      final repository = FakePdaRepository();
      final harness = await _harness(tester, repository: repository);
      await _configureTwoItemCart(tester);

      expect(find.byKey(const Key('batch-review')), findsOneWidget);
      await _scrollUntilVisible(tester, const Key('batch-submit'));
      await tester.tap(find.byKey(const Key('batch-submit')));
      await tester.pumpAndSettle();

      expect(repository.batchCreateCount, 1);
      expect(repository.lastBatchMachineId, testMachine.id);
      expect(repository.lastBatchItems!.length, 2);
      final keys = repository.lastBatchItems!
          .map((item) => item.idempotencyKey)
          .toSet();
      expect(keys.length, 2);
      expect(harness.controller.batchMode, isFalse);
      harness.dispose();
    },
  );

  testWidgets(
    'retrying an ambiguous batch submission reuses the same batch and item '
    'idempotency keys',
    (tester) async {
      final repository = FakePdaRepository()
        ..batchCreateError = const AmbiguousMutationException(
          idempotencyKey: 'ambiguous-batch',
        );
      final harness = await _harness(tester, repository: repository);
      await _configureTwoItemCart(tester);

      await _scrollUntilVisible(tester, const Key('batch-submit'));
      await tester.tap(find.byKey(const Key('batch-submit')));
      await tester.pumpAndSettle();

      expect(harness.controller.hasAmbiguousBatchSubmission, isTrue);
      expect(
        find.byKey(const Key('ambiguous-batch-banner')),
        findsOneWidget,
      );
      final firstBatchKey = repository.lastBatchIdempotencyKey;
      final firstItemKeys = repository.lastBatchItems!
          .map((item) => item.idempotencyKey)
          .toList();

      repository.batchCreateError = null;
      await _scrollUntilVisible(tester, const Key('retry-ambiguous-batch'));
      await tester.tap(find.byKey(const Key('retry-ambiguous-batch')));
      await tester.pumpAndSettle();

      expect(repository.batchCreateCount, 2);
      expect(repository.lastBatchIdempotencyKey, firstBatchKey);
      expect(
        repository.lastBatchItems!.map((item) => item.idempotencyKey).toList(),
        firstItemKeys,
      );
      expect(harness.controller.batchMode, isFalse);
      harness.dispose();
    },
  );

  testWidgets(
    'an API error during batch submission preserves the cart for correction',
    (tester) async {
      // A 503 (not one of the 409/422/428 "shouldRefetch" statuses) keeps the
      // resulting ApiProblem visible instead of it being cleared by the
      // automatic post-error context refresh, exactly like the existing
      // single-request path already behaves for non-refetch statuses.
      final repository = FakePdaRepository()
        ..batchCreateError = const ApiProblem(
          status: 503,
          code: 'DATABASE_UNAVAILABLE',
          detail: 'Veritabanı geçici olarak kullanılamıyor.',
          correlationId: 'corr-batch',
        );
      final harness = await _harness(tester, repository: repository);
      await _configureTwoItemCart(tester);

      await _scrollUntilVisible(tester, const Key('batch-submit'));
      await tester.tap(find.byKey(const Key('batch-submit')));
      await tester.pumpAndSettle();

      expect(harness.controller.batchMode, isTrue);
      expect(harness.controller.batchCart.length, 2);
      expect(harness.controller.problem?.code, 'DATABASE_UNAVAILABLE');
      expect(find.byKey(const Key('batch-review')), findsOneWidget);
      harness.dispose();
    },
  );

  testWidgets(
    'exactly one configured cart item submits through the existing '
    'single-request path, not the batch endpoint',
    (tester) async {
      final repository = FakePdaRepository();
      final harness = await _harness(tester, repository: repository);
      await _enterBatchMode(tester);
      await _expandMain(tester, testMainPartOption);
      await _addPart(tester, 'batch-add-main-${testMainPartOption.partId}');
      await _scrollUntilVisible(tester, const Key('batch-configure-start'));
      await tester.tap(find.byKey(const Key('batch-configure-start')));
      await tester.pumpAndSettle();
      await _selectSlot(tester, 'slot-vacant');
      await _tapAction(tester, 'action-BRING_EMPTY');
      await _confirmTransport(tester);

      await _scrollUntilVisible(tester, const Key('batch-submit'));
      await tester.tap(find.byKey(const Key('batch-submit')));
      await tester.pumpAndSettle();

      expect(repository.createCount, 1);
      expect(repository.batchCreateCount, 0);
      expect(harness.controller.batchMode, isFalse);
      harness.dispose();
    },
  );

  testWidgets(
    'one-click main-part selection while configuring an item changes only the '
    'effective selection, never the cart contents',
    (tester) async {
      final harness = await _harness(tester);
      await _enterBatchMode(tester);
      await _expandMain(tester, testMainPartOption);
      await _addPart(tester, 'batch-add-main-${testMainPartOption.partId}');
      await _addPart(tester, 'batch-add-related-$testOrphanPartId');

      await _scrollUntilVisible(tester, const Key('batch-configure-start'));
      await tester.tap(find.byKey(const Key('batch-configure-start')));
      await tester.pumpAndSettle();

      // Item 1 (main) auto-resolves and is configured; item 2 (orphan) has
      // zero compatible variants, so the part-first gate stays open with the
      // main-part chips on screen.
      await _selectSlot(tester, 'slot-vacant');
      await _tapAction(tester, 'action-BRING_EMPTY');
      await _confirmTransport(tester);

      final cartBefore = harness.controller.batchCart.length;
      await _scrollUntilVisible(
        tester,
        Key('main-part-${testSecondMainPartOption.partId}'),
      );
      await tester.tap(
        find.byKey(Key('main-part-${testSecondMainPartOption.partId}')),
      );
      await tester.pumpAndSettle();

      expect(harness.controller.batchCart.length, cartBefore);
      expect(
        harness.controller.selectedEffectivePart?.partId,
        testSecondMainPartOption.partId,
      );
      harness.dispose();
    },
  );

  testWidgets(
    'the existing single-part workflow (outside batch mode) still works',
    (tester) async {
      final repository = FakePdaRepository();
      final harness = await _harness(tester, repository: repository);
      // Never enters batch mode. One click on the main part resolves its
      // single compatible variant and opens the operation flow directly.
      await tester.tap(
        find.byKey(Key('main-part-${testMainPartOption.partId}')),
      );
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('part-first-continue')));
      await tester.pumpAndSettle();

      await _selectSlot(tester, 'slot-vacant');
      await _tapAction(tester, 'action-BRING_EMPTY');
      await _confirmTransport(tester);

      expect(repository.createCount, 1);
      expect(repository.batchCreateCount, 0);
      expect(harness.controller.batchMode, isFalse);
      harness.dispose();
    },
  );
}

/// Adds main + ALT (both auto-resolve; no manual variant choice needed) and
/// walks each through the operation screen to Review.
Future<void> _configureTwoItemCart(WidgetTester tester) async {
  await _enterBatchMode(tester);
  await _expandMain(tester, testMainPartOption);
  await _addPart(tester, 'batch-add-main-${testMainPartOption.partId}');
  await _addPart(tester, 'batch-add-alt-$testAltPartId');
  await _scrollUntilVisible(tester, const Key('batch-configure-start'));
  await tester.tap(find.byKey(const Key('batch-configure-start')));
  await tester.pumpAndSettle();

  await _selectSlot(tester, 'slot-vacant');
  await _tapAction(tester, 'action-BRING_EMPTY');
  await _confirmTransport(tester);

  await _selectSlot(tester, 'slot-full');
  await _tapAction(tester, 'action-REMOVE_FULL');
  await _confirmTransport(tester);
}

Future<void> _enterBatchMode(WidgetTester tester) async {
  await tester.tap(find.byKey(const Key('enter-batch-selection')));
  await tester.pumpAndSettle();
}

Future<void> _expandMain(WidgetTester tester, MachineMainPartOption main) async {
  await _scrollUntilVisible(tester, Key('batch-browse-main-${main.partId}'));
  await tester.tap(find.byKey(Key('batch-browse-main-${main.partId}')));
  await tester.pumpAndSettle();
}

Future<void> _addPart(WidgetTester tester, String addKey) async {
  await _scrollUntilVisible(tester, Key(addKey));
  await tester.tap(find.byKey(Key(addKey)));
  await tester.pumpAndSettle();
}

Future<void> _tapAction(WidgetTester tester, String actionKey) async {
  await _scrollUntilVisible(tester, Key(actionKey));
  await tester.tap(find.byKey(Key(actionKey)));
  await tester.pumpAndSettle();
}

Future<void> _confirmTransport(WidgetTester tester) async {
  await _scrollUntilVisible(tester, const Key('confirm-transport'));
  await tester.tap(find.byKey(const Key('confirm-transport')));
  await tester.pumpAndSettle();
}

Future<void> _selectSlot(WidgetTester tester, String slotId) async {
  await _scrollUntilVisible(tester, Key('slot-$slotId'));
  await tester.tap(find.byKey(Key('slot-$slotId')));
  await tester.pump();
  await _scrollUntilVisible(tester, const Key('slot-details'));
}

/// Repeatedly scrolls the nearest [Scrollable] until [key] is onstage.
/// [WidgetTester.ensureVisible] only repositions an ALREADY-onstage element,
/// so it cannot be used for widgets a lazily-built ListView/sliver hasn't
/// realized yet — this is the same purpose the rest of this test suite
/// covers with its own local `_scrollTo` helpers. Tries scrolling forward
/// first (the common case — content further down the list) and falls back
/// to scrolling backward, since some checks in this file look back at a
/// cart summary positioned above content that was just scrolled past.
Future<void> _scrollUntilVisible(WidgetTester tester, Key key) async {
  final finder = find.byKey(key);
  if (finder.evaluate().isNotEmpty) return;
  final scrollable = find.byType(Scrollable).last;
  try {
    await tester.scrollUntilVisible(finder, 350, scrollable: scrollable);
  } on StateError {
    await tester.scrollUntilVisible(finder, -350, scrollable: scrollable);
  }
  await tester.pumpAndSettle();
}

Future<_Harness> _harness(
  WidgetTester tester, {
  FakePdaRepository? repository,
}) async {
  // A generously tall viewport avoids fragile scroll-then-tap sequences for
  // the extra batch-mode chrome (cart summary, configuring banner) stacked
  // above the existing part-first/operation content.
  tester.view.devicePixelRatio = 1;
  tester.view.physicalSize = const Size(1024, 2200);
  addTearDown(tester.view.resetDevicePixelRatio);
  addTearDown(tester.view.resetPhysicalSize);
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
