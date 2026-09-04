import '../../core/network/api_client.dart';
import '../models/pda_models.dart';

abstract interface class PdaDataSource {
  Future<LoginSession> login(String employeeNumber);
  Future<AuthenticatedUser> me();
  Future<List<MachineSummary>> machines();
  Future<OperationalContext> operationalContext(String machineId);
  Future<List<MachineMainPartOption>> machinePartOptions(String machineId);
  Future<List<CompatibleVariant>> compatibleVariants({
    required String machineId,
    required String mainPartId,
    required String partId,
  });
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
  Future<TransportBatchResult> createTransportBatch(
    String machineId,
    String batchIdempotencyKey,
    List<TransportBatchItemCommand> items,
  );
  Future<List<ContainerPosition>> containerPositions({
    required String machineId,
    required String variantId,
    required String layoutId,
    required String productGroupId,
    required String partId,
  });
  Future<ContainerActionResult> createContainerAction({
    required String machineId,
    required String variantId,
    required String layoutId,
    required String? navigationProfileId,
    required String productGroupId,
    required String partId,
    required int positionNumber,
    required String action,
    required String idempotencyKey,
  });
  Future<TransportBatchResult> createContainerActionBatch({
    required String machineId,
    required String variantId,
    required String layoutId,
    required String? navigationProfileId,
    required List<ContainerActionBatchItemCommand> items,
    required String idempotencyKey,
  });
  Future<RemoveAllResult> removeAllContainers({
    required String machineId,
    required String variantId,
    required String layoutId,
    required String? navigationProfileId,
    required String idempotencyKey,
  });
  Future<OperatorQueuePage> operatorQueue({
    bool terminal = false,
    int offset = 0,
    int limit = 50,
  });
  Future<TransportResult> transport(String requestId);
  Future<TransportResult> cancel(String requestId);

  /// Customer UI — Warehouse Preparation Console.
  Future<List<WarehousePreparationRequest>> warehousePreparationRequests();
  Future<void> markTransportReady(String requestId);

  /// Customer Monitoring — Robot Alarm Visibility.
  Future<RobotAlarmSummary> robotAlarmSummary();

  /// Real RDSCore Telemetry & Control (Phase 4).
  Future<List<RobotTelemetry>> robots();
  Future<void> pauseRobot([List<String>? vehicles]);
  Future<void> resumeRobot([List<String>? vehicles]);
  Future<void> clearRobotErrors([List<String>? vehicles]);
  Future<void> setRequestPriority(String requestId, int priority);
}

class PdaRepository implements PdaDataSource {
  const PdaRepository(this._api);

  final ApiClient _api;

  @override
  Future<LoginSession> login(String employeeNumber) async {
    // Production Sicil login is passwordless: identity is established by
    // employeeNumber + the already-authorized device, so no password (and
    // no username) is ever sent for this flow.
    final response = await _api.post(
      '/api/v1/auth/login',
      body: {'employeeNumber': employeeNumber},
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
  Future<List<MachineMainPartOption>> machinePartOptions(
    String machineId,
  ) async {
    final response = await _api.get('/api/v1/machines/$machineId/part-options');
    final values = response.body is List ? response.body as List : const [];
    return values
        .whereType<Map<String, dynamic>>()
        .map(MachineMainPartOption.fromJson)
        .toList(growable: false);
  }

  @override
  Future<List<CompatibleVariant>> compatibleVariants({
    required String machineId,
    required String mainPartId,
    required String partId,
  }) async {
    final response = await _api.get(
      '/api/v1/machines/$machineId/parts/$partId/compatible-variants',
      queryParameters: {'mainPartId': mainPartId},
    );
    final values = response.body is List ? response.body as List : const [];
    return values
        .whereType<Map<String, dynamic>>()
        .map(CompatibleVariant.fromJson)
        .toList(growable: false);
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
  Future<List<ContainerPosition>> containerPositions({
    required String machineId,
    required String variantId,
    required String layoutId,
    required String productGroupId,
    required String partId,
  }) async {
    final response = await _api.get(
      '/api/v1/machines/$machineId/container-positions',
      queryParameters: {
        'variantId': variantId,
        'layoutId': layoutId,
        'productGroupId': productGroupId,
        'partId': partId,
      },
    );
    final values = response.body is List ? response.body as List : const [];
    return values
        .whereType<Map<String, dynamic>>()
        .map(ContainerPosition.fromJson)
        .toList(growable: false);
  }

  @override
  Future<ContainerActionResult> createContainerAction({
    required String machineId,
    required String variantId,
    required String layoutId,
    required String? navigationProfileId,
    required String productGroupId,
    required String partId,
    required int positionNumber,
    required String action,
    required String idempotencyKey,
  }) async {
    final response = await _api.post(
      '/api/v1/machines/$machineId/container-actions',
      idempotencyKey: idempotencyKey,
      body: {
        'variantId': variantId,
        'layoutId': layoutId,
        'navigationProfileId': navigationProfileId,
        'productGroupId': productGroupId,
        'partId': partId,
        'positionNumber': positionNumber,
        'action': action,
      },
    );
    return ContainerActionResult.fromJson(_json(response.body));
  }

  @override
  Future<TransportBatchResult> createContainerActionBatch({
    required String machineId,
    required String variantId,
    required String layoutId,
    required String? navigationProfileId,
    required List<ContainerActionBatchItemCommand> items,
    required String idempotencyKey,
  }) async {
    final response = await _api.post(
      '/api/v1/machines/$machineId/container-actions/batch',
      idempotencyKey: idempotencyKey,
      body: {
        'variantId': variantId,
        'layoutId': layoutId,
        'navigationProfileId': navigationProfileId,
        'items': items.map((item) => item.toJson()).toList(growable: false),
      },
    );
    return TransportBatchResult.fromJson(_json(response.body));
  }

  @override
  Future<RemoveAllResult> removeAllContainers({
    required String machineId,
    required String variantId,
    required String layoutId,
    required String? navigationProfileId,
    required String idempotencyKey,
  }) async {
    final response = await _api.post(
      '/api/v1/machines/$machineId/container-actions/remove-all',
      idempotencyKey: idempotencyKey,
      body: {
        'variantId': variantId,
        'layoutId': layoutId,
        'navigationProfileId': navigationProfileId,
      },
    );
    return RemoveAllResult.fromJson(_json(response.body));
  }

  @override
  Future<OperatorQueuePage> operatorQueue({
    bool terminal = false,
    int offset = 0,
    int limit = 50,
  }) async {
    final response = await _api.get(
      '/api/v1/transport-requests/queue',
      queryParameters: {
        'terminal': '$terminal',
        'offset': '$offset',
        'limit': '$limit',
      },
    );
    return OperatorQueuePage.fromJson(_json(response.body));
  }

  @override
  Future<TransportBatchResult> createTransportBatch(
    String machineId,
    String batchIdempotencyKey,
    List<TransportBatchItemCommand> items,
  ) async {
    final response = await _api.post(
      '/api/v1/transport-batches',
      body: {
        'idempotencyKey': batchIdempotencyKey,
        'machineId': machineId,
        'items': items.map((item) => item.toJson()).toList(),
      },
    );
    return TransportBatchResult.fromJson(_json(response.body));
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

  @override
  Future<List<WarehousePreparationRequest>>
  warehousePreparationRequests() async {
    final response = await _api.get('/api/v1/warehouse/preparation-requests');
    final values = response.body is List ? response.body as List : const [];
    return values
        .whereType<Map<String, dynamic>>()
        .map(WarehousePreparationRequest.fromJson)
        .toList(growable: false);
  }

  @override
  Future<void> markTransportReady(String requestId) async {
    await _api.post('/api/v1/transport-requests/$requestId/ready');
  }

  @override
  Future<RobotAlarmSummary> robotAlarmSummary() async {
    final response = await _api.get('/api/v1/rds/robot-alarm-summary');
    return RobotAlarmSummary.fromJson(_json(response.body));
  }

  @override
  Future<List<RobotTelemetry>> robots() async {
    final response = await _api.get('/api/v1/rds/robots');
    final map = _json(response.body);
    final values = map['robots'] is List ? map['robots'] as List : const [];
    return values
        .whereType<Map<String, dynamic>>()
        .map(RobotTelemetry.fromJson)
        .toList(growable: false);
  }

  @override
  Future<void> pauseRobot([List<String>? vehicles]) async {
    await _api.post('/api/v1/rds/robots/pause', body: {'vehicles': vehicles});
  }

  @override
  Future<void> resumeRobot([List<String>? vehicles]) async {
    await _api.post('/api/v1/rds/robots/resume', body: {'vehicles': vehicles});
  }

  @override
  Future<void> clearRobotErrors([List<String>? vehicles]) async {
    await _api.post('/api/v1/rds/robots/clear-errors', body: {'vehicles': vehicles});
  }

  @override
  Future<void> setRequestPriority(String requestId, int priority) async {
    await _api.post('/api/v1/transport-requests/$requestId/priority', body: {'priority': priority});
  }

  static JsonMap _json(Object? body) =>
      body is Map<String, dynamic> ? body : const {};
}
