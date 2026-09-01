import 'package:flutter_test/flutter_test.dart';
import 'package:tkg_pda/core/errors/api_problem.dart';

void main() {
  test('known slot-resolution failures use concise operational Turkish', () {
    const problem = ApiProblem(
      status: 422,
      code: 'BRING_EMPTY_SLOT_NOT_FOUND',
      detail: 'WorkflowRuleException: destinationSlotId has no WorkSite.',
      correlationId: '00000000-0000-0000-0000-000000000001',
    );

    expect(problem.operatorMessage, 'Bu parça için boş konum bulunamadı.');
    expect(problem.operatorMessage, isNot(contains('WorkflowRuleException')));
    expect(problem.operatorMessage, isNot(contains('WorkSite')));
  });

  test('unknown conflicts never expose server or database detail', () {
    const problem = ApiProblem(
      status: 409,
      code: 'UNRECOGNIZED_CONFLICT',
      detail: 'database row sourceSlotId conflict for UUID 123',
      correlationId: null,
    );

    expect(
      problem.operatorMessage,
      'İşlem mevcut durumla çakışıyor. Durumu yenileyin.',
    );
    expect(problem.operatorMessage, isNot(contains('sourceSlotId')));
    expect(problem.operatorMessage, isNot(contains('database')));
  });
}
