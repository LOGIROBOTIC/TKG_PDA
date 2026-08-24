import '../../core/network/api_client.dart';
import '../models/pda_models.dart';

abstract interface class PdaDataSource {
  Future<LoginSession> login(String username, String password);
  Future<AuthenticatedUser> me();
  Future<List<MachineSummary>> machines();
  Future<OperationalContext> operationalContext(String machineId);
  Future<void> switchVariant({
    required String machineId,
    required String variantId,
    required String layoutId,
    required String? navigationProfileId,
  });
  Future<TransportResult> createTransport(
    TransportCommand command,
    String idempotencyKey,
  );
  Future<TransportResult> transport(String requestId);
  Future<TransportResult> cancel(String requestId);
}

class PdaRepository implements PdaDataSource {
  const PdaRepository(this._api);

  final ApiClient _api;

  @override
  Future<LoginSession> login(String username, String password) async {
    final response = await _api.post(
      '/api/v1/auth/login',
      body: {'username': username, 'password': password},
      mutation: false,
    );
    return LoginSession.fromJson(_json(response.body));
  }

  @override
  Future<AuthenticatedUser> me() async {
    final response = await _api.get('/api/v1/auth/me');
    return AuthenticatedUser.fromJson(_json(response.body));
  }

  @override
  Future<List<MachineSummary>> machines() async {
    final response = await _api.get('/api/v1/machines');
    final values = response.body is List ? response.body as List : const [];
    return values
        .whereType<Map<String, dynamic>>()
        .map(MachineSummary.fromJson)
        .toList(growable: false);
  }

  @override
  Future<OperationalContext> operationalContext(String machineId) async {
    final response = await _api.get(
      '/api/v1/machines/$machineId/operational-context',
    );
    return OperationalContext.fromJson(_json(response.body));
  }

  @override
  Future<void> switchVariant({
    required String machineId,
    required String variantId,
    required String layoutId,
    required String? navigationProfileId,
  }) async {
    await _api.post(
      '/api/v1/machines/$machineId/variant-selection',
      body: {
        'variantId': variantId,
        'layoutId': layoutId,
        'navigationProfileId': navigationProfileId,
      },
    );
  }

  @override
  Future<TransportResult> createTransport(
    TransportCommand command,
    String idempotencyKey,
  ) async {
    final response = await _api.post(
      '/api/v1/transport-requests',
      body: command.toJson(),
      idempotencyKey: idempotencyKey,
    );
    return TransportResult.fromJson(_json(response.body));
  }

  @override
  Future<TransportResult> transport(String requestId) async {
    final response = await _api.get('/api/v1/transport-requests/$requestId');
    return TransportResult.fromJson(_json(response.body));
  }

  @override
  Future<TransportResult> cancel(String requestId) async {
    final response = await _api.post(
      '/api/v1/transport-requests/$requestId/cancel',
    );
    return TransportResult.fromJson(_json(response.body));
  }

  static JsonMap _json(Object? body) =>
      body is Map<String, dynamic> ? body : const {};
}
