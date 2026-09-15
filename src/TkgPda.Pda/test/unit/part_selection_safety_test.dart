import 'dart:async';

import 'package:flutter_test/flutter_test.dart';
import 'package:tkg_pda/app/app_controller.dart';
import 'package:tkg_pda/core/auth/session_memory.dart';
import 'package:tkg_pda/core/errors/api_problem.dart';
import 'package:tkg_pda/core/storage/device_code_store.dart';
import 'package:tkg_pda/data/models/pda_models.dart';

import '../support/fakes.dart';

void main() {
  test(
    'changing main/requested part is blocked throughout submission',
    () async {
      final repository = FakePdaRepository();
      final controller = await _controller(repository);
      await controller.selectMainPartOption(testMainPartOption);
      repository.createCompleter = Completer<TransportResult>();
      final positions = controller.containerPositions;
      final submission = controller.submitContainerAction(
        positions.first,
        'FULL',
      );

      expect(controller.canChangePartSelection, isFalse);
      controller.resetPartSelection();
      await controller.selectMainPartOption(testSecondMainPartOption);
      await controller.selectEffectivePart(
        const EffectivePartSelection(
          partId: testRelatedPartId,
          partCode: 'RELATED',
          isMain: false,
        ),
      );
      expect(controller.selectedMainPart?.partId, testMainPartId);
      expect(controller.selectedEffectivePart?.partId, testMainPartId);
      expect(controller.containerPositions, same(positions));

      repository.createCompleter!.complete(
        const TransportResult(
          id: 'test-request',
          requestNo: 1,
          status: 'QUEUED',
          operation: 'REMOVE_FULL',
          idempotentReplay: false,
        ),
      );
      await submission;
      expect(controller.canChangePartSelection, isTrue);
      controller.resetPartSelection();
      expect(controller.selectedMainPart, isNull);
      expect(controller.containerPositions, isEmpty);
      expect(controller.transportActionsReady, isFalse);
      expect(repository.containerActionCount, 1);
    },
  );

  test(
    'ambiguous transport result preserves main part and retry identity',
    () async {
      final repository = FakePdaRepository();
      final controller = await _controller(repository);
      await controller.selectMainPartOption(testMainPartOption);
      repository.createError = const AmbiguousMutationException(
        idempotencyKey: 'test-ambiguous',
      );
      await controller.submitContainerAction(
        controller.containerPositions.first,
        'FULL',
      );
      final originalKey = repository.lastIdempotencyKey;

      expect(controller.hasAmbiguousSubmission, isTrue);
      expect(controller.canChangePartSelection, isFalse);
      controller.resetPartSelection();
      await controller.selectMainPartOption(testSecondMainPartOption);
      expect(controller.selectedMainPart?.partId, testMainPartId);
      expect(controller.selectedEffectivePart?.partId, testMainPartId);
      expect(controller.hasAmbiguousSubmission, isTrue);
      expect(repository.lastIdempotencyKey, originalKey);
      expect(repository.containerActionCount, 1);
    },
  );

  test(
    'reset discards an old compatibility response without switching variant',
    () async {
      final repository = _DelayedCompatibilityRepository();
      final controller = await _controller(repository);
      final selection = controller.selectMainPartOption(
        testAmbiguousMainPartOption,
      );
      expect(controller.compatibleVariantsLoading, isTrue);

      controller.resetPartSelection();
      expect(controller.compatibleVariantsLoading, isFalse);
      repository.response.complete(
        repository.compatibleVariantsValue[testMainPartId]!,
      );
      await selection;

      expect(controller.selectedMainPart, isNull);
      expect(controller.compatibleVariants, isNull);
      expect(controller.transportActionsReady, isFalse);
      expect(repository.switchCount, 0);
      expect(repository.containerActionCount, 0);
    },
  );
}

Future<AppController> _controller(FakePdaRepository repository) async {
  final controller = AppController(
    repository: repository,
    deviceCodeStore: MemoryDeviceCodeStore(testDeviceCode),
    session: SessionMemory(),
    enablePolling: false,
    // Legacy pre-production-session workflow regression. Default-on production
    // lifecycle is exercised separately by production_flow_test.dart.
    enableProductionWorkflow: false,
  );
  addTearDown(controller.dispose);
  await controller.initialize();
  await controller.login('00123');
  await controller.selectMachine(testMachine);
  return controller;
}

class _DelayedCompatibilityRepository extends FakePdaRepository {
  final response = Completer<List<CompatibleVariant>>();

  @override
  Future<List<CompatibleVariant>> compatibleVariants({
    required String machineId,
    required String mainPartId,
    required String partId,
  }) => response.future;
}
