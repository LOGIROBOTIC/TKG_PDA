import 'dart:async';

import 'package:flutter_test/flutter_test.dart';
import 'package:tkg_pda/core/errors/api_problem.dart';
import 'package:tkg_pda/data/models/pda_models.dart';
import 'package:tkg_pda/features/transport/submission_coordinator.dart';
import 'package:tkg_pda/shared/presentation.dart';

void main() {
  var sequence = 0;
  late SubmissionCoordinator coordinator;

  setUp(() {
    sequence = 0;
    coordinator = SubmissionCoordinator(
      keyFactory: () => 'idempotency-${++sequence}',
    );
  });

  test('ambiguous retry reuses the same idempotency key', () async {
    final command = _command();
    final observed = <String>[];

    await expectLater(
      coordinator.submit(command, (command, key) async {
        observed.add(key);
        throw AmbiguousMutationException(idempotencyKey: key);
      }),
      throwsA(isA<AmbiguousMutationException>()),
    );
    final result = await coordinator.submit(command, (command, key) async {
      observed.add(key);
      return _result;
    });

    expect(result.id, _result.id);
    expect(observed, ['idempotency-1', 'idempotency-1']);
  });

  test('material command change generates a new idempotency key', () {
    final first = coordinator.keyFor(_command());
    final second = coordinator.keyFor(_command(operation: 'REMOVE_FULL'));

    expect(first, 'idempotency-1');
    expect(second, 'idempotency-2');
  });

  test('same material command keeps one idempotency key', () {
    expect(coordinator.keyFor(_command()), 'idempotency-1');
    expect(coordinator.keyFor(_command()), 'idempotency-1');
  });

  test('double tap cannot start a second concurrent submission', () async {
    final completer = Completer<TransportResult>();
    final first = coordinator.submit(_command(), (_, _) => completer.future);

    await expectLater(
      coordinator.submit(_command(), (_, _) async => _result),
      throwsA(isA<StateError>()),
    );
    completer.complete(_result);
    await first;
  });

  test('execution states map to stable Turkish operator wording', () {
    expect(executionStateLabel('REQUESTED'), 'Talep oluşturuldu');
    expect(executionStateLabel('WAITING_FOR_ROBOT'), 'Robot bekleniyor');
    expect(executionStateLabel('ROBOT_ASSIGNED'), 'Robot atandı');
    expect(executionStateLabel('RUNNING'), 'Taşıma devam ediyor');
    expect(executionStateLabel('FAILED'), 'Tamamlanamadı');
  });

  test('failure presentation prefers safe backend operator message', () {
    expect(
      failureLabel('EXECUTION_FAILED', 'Güvenli özel mesaj'),
      'Güvenli özel mesaj',
    );
    expect(
      failureLabel('PATH_UNAVAILABLE', null),
      'Robot hedef noktaya ulaşamıyor.',
    );
    expect(
      failureLabel('TRAFFIC_WAITING', null),
      'Robot trafiği nedeniyle görev bekliyor.',
    );
  });

  test('server availableActions are consumed without local recalculation', () {
    final slot = LayoutSlot.fromJson({
      'id': 'slot-1',
      'code': 'S1',
      'displayName': 'Slot 1',
      'role': 'STAGING',
      'isEnabled': true,
      'transportEligible': true,
      'occupancyState': 'VACANT',
      'loadState': 'UNKNOWN',
      'displayOrder': 1,
      'rowVersion': 0,
      'rdsWorkSite': {'isMapped': false},
      'executionReadiness': {'rdsWorkSiteMapped': false},
      'availableActions': [
        {
          'action': 'BRING_EMPTY',
          'enabled': false,
          'disabledCode': 'SLOT_BUSY',
          'disabledMessage': 'Aktif talep var.',
        },
      ],
    });

    expect(slot.occupancyState, 'VACANT');
    expect(slot.availableActions.single.enabled, isFalse);
    expect(slot.availableActions.single.disabledCode, 'SLOT_BUSY');
  });
}

TransportCommand _command({String operation = 'BRING_EMPTY'}) =>
    TransportCommand(
      machineId: 'machine',
      variantId: 'variant',
      layoutId: 'layout',
      navigationProfileId: null,
      productGroupId: null,
      partId: null,
      containerTypeId: null,
      operation: operation,
      sourceSlotId: operation == 'BRING_EMPTY' ? null : 'slot',
      destinationSlotId: operation == 'BRING_EMPTY' ? 'slot' : null,
      priority: 100,
    );

const _result = TransportResult(
  id: 'request',
  requestNo: 42,
  status: 'QUEUED',
  operation: 'BRING_EMPTY',
  idempotentReplay: false,
);
