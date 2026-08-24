import 'package:uuid/uuid.dart';

import '../../core/errors/api_problem.dart';
import '../../data/models/pda_models.dart';

typedef TransportSubmitter =
    Future<TransportResult> Function(
      TransportCommand command,
      String idempotencyKey,
    );

class SubmissionCoordinator {
  SubmissionCoordinator({String Function()? keyFactory})
    : _keyFactory = keyFactory ?? const Uuid().v4;

  final String Function() _keyFactory;
  String? _materialSignature;
  String? _idempotencyKey;
  bool _inFlight = false;
  bool _ambiguous = false;

  bool get inFlight => _inFlight;
  bool get hasAmbiguousRequest => _ambiguous;
  String? get pendingIdempotencyKey => _idempotencyKey;

  String keyFor(TransportCommand command) {
    if (_materialSignature != command.materialSignature) {
      _materialSignature = command.materialSignature;
      _idempotencyKey = _keyFactory();
      _ambiguous = false;
    }
    return _idempotencyKey!;
  }

  Future<TransportResult> submit(
    TransportCommand command,
    TransportSubmitter submitter,
  ) async {
    if (_inFlight) {
      throw StateError('Aynı anda yalnız bir taşıma isteği gönderilebilir.');
    }
    final key = keyFor(command);
    _inFlight = true;
    try {
      final result = await submitter(command, key);
      clear();
      return result;
    } on AmbiguousMutationException {
      _ambiguous = true;
      rethrow;
    } finally {
      _inFlight = false;
    }
  }

  void resolveByRefresh() => clear();

  void clear() {
    _materialSignature = null;
    _idempotencyKey = null;
    _ambiguous = false;
  }
}
