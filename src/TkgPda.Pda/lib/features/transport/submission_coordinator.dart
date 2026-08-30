import 'dart:convert';

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

typedef BatchSubmitter =
    Future<TransportBatchResult> Function(
      String batchIdempotencyKey,
      Map<String, String> itemIdempotencyKeysByCartItemId,
    );

/// Same one-key-per-material-signature discipline as [SubmissionCoordinator],
/// extended to a batch idempotency key plus one independent key per cart
/// item. Keys are keyed by a stable client-side cart item id (not the
/// command's own material signature) so unrelated items never collide; a
/// key regenerates only when that specific item's content changes, and the
/// batch key regenerates only when the overall item set/content changes —
/// never merely because an HTTP retry occurred.
class BatchSubmissionCoordinator {
  BatchSubmissionCoordinator({String Function()? keyFactory})
    : _keyFactory = keyFactory ?? const Uuid().v4;

  final String Function() _keyFactory;
  String? _batchSignature;
  String? _batchKey;
  final Map<String, String> _itemSignatures = {};
  final Map<String, String> _itemKeys = {};
  bool _inFlight = false;
  bool _ambiguous = false;

  bool get inFlight => _inFlight;
  bool get hasAmbiguousRequest => _ambiguous;
  String? get pendingBatchIdempotencyKey => _batchKey;

  ({String batchKey, Map<String, String> itemKeys}) keysFor(
    Map<String, TransportCommand> commandsByCartItemId,
  ) {
    final orderedIds = commandsByCartItemId.keys.toList()..sort();
    final signature = jsonEncode({
      for (final id in orderedIds) id: commandsByCartItemId[id]!.toJson(),
    });
    if (_batchSignature != signature) {
      _batchSignature = signature;
      _batchKey = _keyFactory();
      _ambiguous = false;
    }
    for (final id in commandsByCartItemId.keys) {
      final itemSignature = commandsByCartItemId[id]!.materialSignature;
      if (_itemSignatures[id] != itemSignature) {
        _itemSignatures[id] = itemSignature;
        _itemKeys[id] = _keyFactory();
      }
    }
    _itemKeys.removeWhere((id, _) => !commandsByCartItemId.containsKey(id));
    _itemSignatures.removeWhere(
      (id, _) => !commandsByCartItemId.containsKey(id),
    );
    return (batchKey: _batchKey!, itemKeys: Map.unmodifiable(_itemKeys));
  }

  Future<TransportBatchResult> submit(
    Map<String, TransportCommand> commandsByCartItemId,
    BatchSubmitter submitter,
  ) async {
    if (_inFlight) {
      throw StateError('Aynı anda yalnız bir grup gönderilebilir.');
    }
    final keys = keysFor(commandsByCartItemId);
    _inFlight = true;
    try {
      final result = await submitter(keys.batchKey, keys.itemKeys);
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
    _batchSignature = null;
    _batchKey = null;
    _itemSignatures.clear();
    _itemKeys.clear();
    _ambiguous = false;
  }
}
