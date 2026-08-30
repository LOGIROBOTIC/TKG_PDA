import 'dart:async';

import 'package:tkg_pda/core/errors/api_problem.dart';
import 'package:tkg_pda/data/models/pda_models.dart';
import 'package:tkg_pda/data/repositories/pda_repository.dart';

const testDeviceCode = 'PDA-TEST-01';

final testUser = AuthenticatedUser(
  id: '10000000-0000-0000-0000-000000000001',
  username: 'operator',
  fullName: 'Test Operatör',
  employeeNo: null,
  roles: const ['LINE_OPERATOR'],
);

final testDevice = PdaDevice(
  id: '20000000-0000-0000-0000-000000000001',
  code: testDeviceCode,
  name: 'Test PDA',
  type: 'PDA',
);

final testMachine = MachineSummary(
  id: '30000000-0000-0000-0000-000000000001',
  code: 'SH-01-A',
  displayName: 'SH-01-A Hücresi',
  displayOrder: 1,
  permissions: const MachinePermissions(
    canView: true,
    canOperate: true,
    canChangeVariant: true,
    canManageSlots: false,
  ),
);

const testVariantV1Id = '40000000-0000-0000-0000-000000000001';
const testVariantV2Id = '40000000-0000-0000-0000-000000000002';
const testProductGroupId = '60000000-0000-0000-0000-000000000001';
const testMainPartId = '70000000-0000-0000-0000-000000000001';
const testAltPartId = '70000000-0000-0000-0000-000000000002';
const testRelatedPartId = '70000000-0000-0000-0000-000000000003';
const testOrphanPartId = '70000000-0000-0000-0000-000000000004';

OperationalContext testContext({
  List<ActiveTransportRequest> activeRequests = const [],
  String activeVariantId = testVariantV1Id,
}) {
  final isV2 = activeVariantId == testVariantV2Id;
  return OperationalContext.fromJson({
    'machine': _machineJson,
    'activeVariantId': activeVariantId,
    'activeVariantCode': isV2 ? 'V2' : 'V1',
    'activeVariantName': isV2 ? 'Varyant 2' : 'Varyant 1',
    'activeLayoutId': '50000000-0000-0000-0000-000000000001',
    'activeLayoutCode': 'LAYOUT-A',
    'activeLayoutName': 'SH-01-A Yerleşimi',
    'activeNavigationProfileId': null,
    'runtimeRowVersion': 1,
    'variantLayouts': [
      {
        'variantId': testVariantV1Id,
        'variantCode': 'V1',
        'variantName': 'Varyant 1',
        'layoutId': '50000000-0000-0000-0000-000000000001',
        'layoutCode': 'LAYOUT-A',
        'layoutName': 'SH-01-A Yerleşimi',
        'isDefault': true,
      },
      {
        'variantId': testVariantV2Id,
        'variantCode': 'V2',
        'variantName': 'Varyant 2',
        'layoutId': '50000000-0000-0000-0000-000000000001',
        'layoutCode': 'LAYOUT-A',
        'layoutName': 'SH-01-A Yerleşimi',
        'isDefault': true,
      },
    ],
    'slots': [_machineSlot, _vacantSlot, _emptySlot, _fullSlot],
    'productGroups': [
      {
        'id': testProductGroupId,
        'mainPartId': testMainPartId,
        'mainPartCode': '71550BN000-10-HAOS',
        'name': 'Ana Parça',
        'displayOrder': 1,
        'parts': [
          {
            'id': '80000000-0000-0000-0000-000000000001',
            'partId': testAltPartId,
            'partCode': '715A2BN400-50',
            'containerTypeId': null,
            'containerCode': null,
            'relationType': 'ALT_PART',
            'lotQuantity': null,
            'displayOrder': 1,
          },
        ],
      },
    ],
    'activeTransportRequests': activeRequests
        .map(
          (request) => {
            'requestId': request.requestId,
            'requestNo': request.requestNo,
            'operation': request.operation,
            'status': request.status,
            'requestedBy': request.requestedBy,
            'requestedByUsername': request.requestedByUsername,
            'isMine': request.isMine,
            'sourceSlotId': request.sourceSlotId,
            'destinationSlotId': request.destinationSlotId,
            'productGroupId': request.productGroupId,
            'partId': request.partId,
            'containerTypeId': request.containerTypeId,
            'priority': request.priority,
            'createdAt': request.createdAt.toIso8601String(),
            'canCancel': request.canCancel,
            'cancelDisabledCode': request.cancelDisabledCode,
            'cancelDisabledMessage': request.cancelDisabledMessage,
            'execution': {
              'state': request.execution.state,
              'rdsTaskRecordId': request.execution.rdsTaskRecordId,
              'rdsTaskLabel': request.execution.rdsTaskLabel,
              'outOrderNo': request.execution.outOrderNo,
              'agvId': request.execution.agvId,
              'rdsStatus': request.execution.rdsStatus,
              'lastSyncAt': request.execution.lastSyncAt?.toIso8601String(),
              'failureCode': request.execution.failureCode,
              'failureCategory': request.execution.failureCategory,
              'operatorMessage': request.execution.operatorMessage,
              'technicalMessage': request.execution.technicalMessage,
              'correlationId': request.execution.correlationId,
            },
          },
        )
        .toList(),
    'taskTemplates': [],
  });
}

final testActiveRequest = ActiveTransportRequest.fromJson({
  'requestId': '90000000-0000-0000-0000-000000000001',
  'requestNo': 42,
  'operation': 'BRING_EMPTY',
  'status': 'QUEUED',
  'requestedBy': testUser.id,
  'requestedByUsername': testUser.username,
  'isMine': true,
  'sourceSlotId': null,
  'destinationSlotId': 'slot-vacant',
  'productGroupId': null,
  'partId': null,
  'containerTypeId': null,
  'priority': 100,
  'createdAt': '2026-08-24T08:00:00Z',
  'canCancel': true,
  'cancelDisabledCode': null,
  'cancelDisabledMessage': null,
  'execution': {
    'state': 'WAITING_FOR_ROBOT',
    'rdsTaskRecordId': null,
    'rdsTaskLabel': null,
    'outOrderNo': null,
    'agvId': null,
    'rdsStatus': null,
    'lastSyncAt': null,
    'failureCode': null,
    'failureCategory': null,
    'operatorMessage': null,
    'technicalMessage': null,
    'correlationId': 'a0000000-0000-0000-0000-000000000001',
  },
});

// Customer UI — Warehouse Preparation Console fixture.
final testWarehousePreparationRequest = WarehousePreparationRequest.fromJson({
  'requestId': '90000000-0000-0000-0000-000000000002',
  'requestNo': 55,
  'operation': 'BRING_EMPTY',
  'machineId': testMachine.id,
  'machineCode': testMachine.code,
  'machineDisplayName': testMachine.displayName,
  'partCode': '71550BN000-10-HAOS',
  'containerTypeCode': null,
  'sourceSlotId': null,
  'sourceSlotCode': null,
  'sourceSlotDisplayName': null,
  'destinationSlotId': 'slot-vacant',
  'destinationSlotCode': 'SLOT-V',
  'destinationSlotDisplayName': 'Boş Konum',
  'createdAt': '2026-08-24T08:00:00Z',
  'queuedAt': '2026-08-24T08:00:01Z',
});

// WP-03B: part-first fixtures. testMainPartId matches testContext's
// productGroups[0].mainPartId; testAltPartId matches its ALT_PART child —
// so the "already active" auto-resolve and auto-carry-into-material paths
// can be exercised end to end.
final testMainPartOption = MachineMainPartOption(
  partId: testMainPartId,
  partCode: '71550BN000-10-HAOS',
  description: 'Ana parça',
  relatedParts: const [
    MachineRelatedPart(
      partId: testAltPartId,
      partCode: '715A2BN400-50',
      description: null,
      relationType: 'ALT_PART',
    ),
    MachineRelatedPart(
      partId: testRelatedPartId,
      partCode: '715B3CN500-60',
      description: null,
      relationType: 'RELATED',
    ),
    MachineRelatedPart(
      partId: testOrphanPartId,
      partCode: '715C4DN600-70',
      description: null,
      relationType: 'RELATED',
    ),
  ],
);

final testSecondMainPartOption = MachineMainPartOption(
  partId: '70000000-0000-0000-0000-000000000010',
  partCode: '900-OTHER-MAIN',
  description: null,
  relatedParts: const [],
);

final testPartOptions = [testMainPartOption, testSecondMainPartOption];

/// Keyed by partId. Main part -> single, already-active variant (V1, no
/// switch needed). Alt part -> single, DIFFERENT variant (V2, requires
/// switchVariant). Related part -> two variants (requires user selection).
/// Orphan part -> no mapping (empty list).
final testCompatibleVariantsByPart = <String, List<CompatibleVariant>>{
  testMainPartId: [
    const CompatibleVariant(
      variantId: testVariantV1Id,
      variantCode: 'V1',
      variantName: 'Varyant 1',
      machineId: '30000000-0000-0000-0000-000000000001',
      productGroupId: testProductGroupId,
      relationType: 'MAIN',
    ),
  ],
  testAltPartId: [
    const CompatibleVariant(
      variantId: testVariantV2Id,
      variantCode: 'V2',
      variantName: 'Varyant 2',
      machineId: '30000000-0000-0000-0000-000000000001',
      productGroupId: testProductGroupId,
      relationType: 'ALT_PART',
    ),
  ],
  testRelatedPartId: [
    const CompatibleVariant(
      variantId: testVariantV1Id,
      variantCode: 'V1',
      variantName: 'Varyant 1',
      machineId: '30000000-0000-0000-0000-000000000001',
      productGroupId: testProductGroupId,
      relationType: 'RELATED',
    ),
    const CompatibleVariant(
      variantId: testVariantV2Id,
      variantCode: 'V2',
      variantName: 'Varyant 2',
      machineId: '30000000-0000-0000-0000-000000000001',
      productGroupId: testProductGroupId,
      relationType: 'RELATED',
    ),
  ],
  testOrphanPartId: const [],
};

class FakePdaRepository implements PdaDataSource {
  AuthenticatedUser loginUser = testUser;
  List<MachineSummary> machineValues = [testMachine];
  OperationalContext contextValue = testContext();
  ApiProblem? loginProblem;
  Object? contextError;
  Object? createError;
  int loginCount = 0;
  String? lastEmployeeNumber;
  int createCount = 0;
  int batchCreateCount = 0;
  String? lastBatchMachineId;
  String? lastBatchIdempotencyKey;
  List<TransportBatchItemCommand>? lastBatchItems;
  Object? batchCreateError;
  Completer<TransportBatchResult>? batchCreateCompleter;
  int cancelCount = 0;
  List<WarehousePreparationRequest> warehouseRequestsValue = const [];
  Object? warehouseRequestsError;
  int warehouseRequestsCount = 0;
  Object? markReadyError;
  int markReadyCount = 0;
  String? lastMarkReadyRequestId;
  RobotAlarmSummary robotAlarmSummaryValue = const RobotAlarmSummary(
    hasActiveRobotAlarm: false,
    highestSeverity: null,
    activeAlarmCount: 0,
  );
  Object? robotAlarmSummaryError;
  int robotAlarmSummaryCount = 0;
  int switchCount = 0;
  String? lastSwitchVariantId;
  Object? switchVariantError;
  int loadStateCount = 0;
  int contextCount = 0;
  int meCount = 0;
  List<MachineMainPartOption> partOptionsValue = testPartOptions;
  Object? partOptionsError;
  int partOptionsCount = 0;
  Map<String, List<CompatibleVariant>> compatibleVariantsValue =
      testCompatibleVariantsByPart;
  Object? compatibleVariantsError;
  int compatibleVariantsCount = 0;
  final List<String> compatibleVariantsRequestedPartIds = [];
  String? lastLoadState;
  String? lastIdempotencyKey;
  TransportCommand? lastCommand;
  Completer<TransportResult>? createCompleter;
  Completer<AuthenticatedUser>? meCompleter;

  @override
  Future<LoginSession> login(String employeeNumber) async {
    loginCount++;
    lastEmployeeNumber = employeeNumber;
    if (loginProblem case final problem?) throw problem;
    return LoginSession(
      accessToken: 'runtime-only-test-token',
      expiresAt: DateTime.now().toUtc().add(const Duration(minutes: 10)),
      user: loginUser,
      device: testDevice,
    );
  }

  @override
  Future<AuthenticatedUser> me() async {
    meCount++;
    if (meCompleter case final completer?) return completer.future;
    return testUser;
  }

  @override
  Future<List<MachineSummary>> machines() async => machineValues;

  @override
  Future<OperationalContext> operationalContext(String machineId) async {
    contextCount++;
    if (contextError case final error?) throw error;
    return contextValue;
  }

  @override
  Future<void> switchVariant({
    required String machineId,
    required String variantId,
    required String layoutId,
    required String? navigationProfileId,
  }) async {
    switchCount++;
    lastSwitchVariantId = variantId;
    if (switchVariantError case final error?) throw error;
    contextValue = testContext(activeVariantId: variantId);
  }

  @override
  Future<List<MachineMainPartOption>> machinePartOptions(
    String machineId,
  ) async {
    partOptionsCount++;
    if (partOptionsError case final error?) throw error;
    return partOptionsValue;
  }

  @override
  Future<List<CompatibleVariant>> compatibleVariants({
    required String machineId,
    required String partId,
  }) async {
    compatibleVariantsCount++;
    compatibleVariantsRequestedPartIds.add(partId);
    if (compatibleVariantsError case final error?) throw error;
    return compatibleVariantsValue[partId] ?? const [];
  }

  @override
  Future<TransportResult> createTransport(
    TransportCommand command,
    String idempotencyKey,
  ) async {
    createCount++;
    lastCommand = command;
    lastIdempotencyKey = idempotencyKey;
    if (createError case final error?) throw error;
    if (createCompleter case final completer?) return completer.future;
    contextValue = testContext(activeRequests: [testActiveRequest]);
    return const TransportResult(
      id: '90000000-0000-0000-0000-000000000001',
      requestNo: 42,
      status: 'QUEUED',
      operation: 'BRING_EMPTY',
      idempotentReplay: false,
    );
  }

  @override
  Future<TransportBatchResult> createTransportBatch(
    String machineId,
    String batchIdempotencyKey,
    List<TransportBatchItemCommand> items,
  ) async {
    batchCreateCount++;
    lastBatchMachineId = machineId;
    lastBatchIdempotencyKey = batchIdempotencyKey;
    lastBatchItems = items;
    if (batchCreateError case final error?) throw error;
    if (batchCreateCompleter case final completer?) return completer.future;
    contextValue = testContext(activeRequests: [testActiveRequest]);
    return TransportBatchResult(
      batchId: '95000000-0000-0000-0000-000000000001',
      idempotentReplay: false,
      items: [
        for (var i = 0; i < items.length; i++)
          TransportResult(
            id: '9500000$i-0000-0000-0000-00000000000$i',
            requestNo: 100 + i,
            status: 'QUEUED',
            operation: items[i].command.operation,
            idempotentReplay: false,
          ),
      ],
    );
  }

  @override
  Future<TransportResult> transport(String requestId) async =>
      const TransportResult(
        id: '90000000-0000-0000-0000-000000000001',
        requestNo: 42,
        status: 'QUEUED',
        operation: 'BRING_EMPTY',
        idempotentReplay: false,
      );

  @override
  Future<TransportResult> cancel(String requestId) async {
    cancelCount++;
    contextValue = testContext();
    return const TransportResult(
      id: '90000000-0000-0000-0000-000000000001',
      requestNo: 42,
      status: 'CANCELLED',
      operation: 'BRING_EMPTY',
      idempotentReplay: false,
    );
  }

  @override
  Future<List<WarehousePreparationRequest>>
  warehousePreparationRequests() async {
    warehouseRequestsCount++;
    if (warehouseRequestsError case final error?) throw error;
    return warehouseRequestsValue;
  }

  @override
  Future<void> markTransportReady(String requestId) async {
    markReadyCount++;
    lastMarkReadyRequestId = requestId;
    if (markReadyError case final error?) throw error;
    warehouseRequestsValue = warehouseRequestsValue
        .where((request) => request.requestId != requestId)
        .toList(growable: false);
  }

  @override
  Future<void> updateLoadState({
    required String machineId,
    required String slotId,
    required String loadState,
  }) async {
    loadStateCount++;
    lastLoadState = loadState;
  }

  @override
  Future<RobotAlarmSummary> robotAlarmSummary() async {
    robotAlarmSummaryCount++;
    if (robotAlarmSummaryError case final error?) throw error;
    return robotAlarmSummaryValue;
  }
}

const _machineJson = {
  'id': '30000000-0000-0000-0000-000000000001',
  'code': 'SH-01-A',
  'displayName': 'SH-01-A Hücresi',
  'displayOrder': 1,
  'permissions': {
    'canView': true,
    'canOperate': true,
    'canChangeVariant': true,
    'canManageSlots': false,
  },
};

const _workSite = {
  'isMapped': false,
  'siteId': null,
  'area': null,
  'groupName': null,
  'type': null,
  'filled': null,
  'locked': null,
  'disabled': null,
  'syncFailed': null,
  'content': null,
  'agvId': null,
  'lastSyncAt': null,
};

const _readiness = {
  'rdsWorkSiteMapped': false,
  'readinessCode': 'RDS_WORKSITE_NOT_MAPPED',
  'readinessMessage': 'Robot noktası eşlenmemiş.',
};

const _machineSlot = {
  'id': 'slot-machine',
  'code': 'MACHINE-01',
  'displayName': 'Makine',
  'role': 'MACHINE',
  'isEnabled': true,
  'transportEligible': false,
  'defaultContainerTypeId': null,
  'defaultContainerCode': null,
  'x': 0.35,
  'y': 0.35,
  'width': 0.3,
  'height': 0.3,
  'displayOrder': 1,
  'occupancyState': 'UNKNOWN',
  'loadState': 'UNKNOWN',
  'currentPartId': null,
  'currentPartCode': null,
  'currentContainerTypeId': null,
  'currentContainerCode': null,
  'rowVersion': 0,
  'rdsWorkSite': _workSite,
  'executionReadiness': _readiness,
  'availableActions': [
    {
      'action': 'BRING_EMPTY',
      'enabled': false,
      'disabledCode': 'MACHINE_REGION_FORBIDDEN',
      'disabledMessage': 'Makine alanı taşıma dışıdır.',
    },
  ],
  'loadStateActions': [],
};

const _vacantSlot = {
  'id': 'slot-vacant',
  'code': 'SLOT-V',
  'displayName': 'Boş Konum',
  'role': 'STAGING',
  'isEnabled': true,
  'transportEligible': true,
  'defaultContainerTypeId': null,
  'defaultContainerCode': null,
  'x': 0.05,
  'y': 0.05,
  'width': 0.2,
  'height': 0.2,
  'displayOrder': 2,
  'occupancyState': 'VACANT',
  'loadState': 'UNKNOWN',
  'currentPartId': null,
  'currentPartCode': null,
  'currentContainerTypeId': null,
  'currentContainerCode': null,
  'rowVersion': 0,
  'rdsWorkSite': _workSite,
  'executionReadiness': _readiness,
  'availableActions': [
    {
      'action': 'BRING_EMPTY',
      'enabled': true,
      'disabledCode': null,
      'disabledMessage': null,
    },
    {
      'action': 'REMOVE_FULL',
      'enabled': false,
      'disabledCode': 'SLOT_STATE_MISMATCH',
      'disabledMessage': 'Konum durumu uygun değil.',
    },
    {
      'action': 'REMOVE_EMPTY',
      'enabled': false,
      'disabledCode': 'SLOT_STATE_MISMATCH',
      'disabledMessage': 'Konum durumu uygun değil.',
    },
  ],
  'loadStateActions': [],
};

const _emptySlot = {
  'id': 'slot-empty',
  'code': 'SLOT-E',
  'displayName': 'Boş Kasa Konumu',
  'role': 'STAGING',
  'isEnabled': true,
  'transportEligible': true,
  'defaultContainerTypeId': null,
  'defaultContainerCode': '4311',
  'x': 0.7,
  'y': 0.05,
  'width': 0.2,
  'height': 0.2,
  'displayOrder': 3,
  'occupancyState': 'OCCUPIED',
  'loadState': 'EMPTY',
  'currentPartId': null,
  'currentPartCode': null,
  'currentContainerTypeId': null,
  'currentContainerCode': '4311',
  'rowVersion': 0,
  'rdsWorkSite': _workSite,
  'executionReadiness': _readiness,
  'availableActions': [
    {
      'action': 'BRING_EMPTY',
      'enabled': false,
      'disabledCode': 'SLOT_STATE_MISMATCH',
      'disabledMessage': 'Konum durumu uygun değil.',
    },
    {
      'action': 'REMOVE_FULL',
      'enabled': false,
      'disabledCode': 'SLOT_STATE_MISMATCH',
      'disabledMessage': 'Konum durumu uygun değil.',
    },
    {
      'action': 'REMOVE_EMPTY',
      'enabled': true,
      'disabledCode': null,
      'disabledMessage': null,
    },
  ],
  'loadStateActions': [
    {
      'action': 'MARK_FULL',
      'enabled': true,
      'disabledCode': null,
      'disabledMessage': null,
    },
    {
      'action': 'MARK_EMPTY',
      'enabled': false,
      'disabledCode': 'LOAD_STATE_ALREADY_REPORTED',
      'disabledMessage': 'Kasa zaten bu yük durumundadır.',
    },
  ],
};

const _fullSlot = {
  'id': 'slot-full',
  'code': 'SLOT-F',
  'displayName': 'Dolu Kasa Konumu',
  'role': 'STAGING',
  'isEnabled': true,
  'transportEligible': true,
  'defaultContainerTypeId': null,
  'defaultContainerCode': '4322',
  'x': 0.05,
  'y': 0.7,
  'width': 0.2,
  'height': 0.2,
  'displayOrder': 4,
  'occupancyState': 'OCCUPIED',
  'loadState': 'FULL',
  'currentPartId': null,
  'currentPartCode': null,
  'currentContainerTypeId': null,
  'currentContainerCode': '4322',
  'rowVersion': 0,
  'rdsWorkSite': _workSite,
  'executionReadiness': _readiness,
  'availableActions': [
    {
      'action': 'BRING_EMPTY',
      'enabled': false,
      'disabledCode': 'SLOT_STATE_MISMATCH',
      'disabledMessage': 'Konum durumu uygun değil.',
    },
    {
      'action': 'REMOVE_FULL',
      'enabled': true,
      'disabledCode': null,
      'disabledMessage': null,
    },
    {
      'action': 'REMOVE_EMPTY',
      'enabled': false,
      'disabledCode': 'SLOT_STATE_MISMATCH',
      'disabledMessage': 'Konum durumu uygun değil.',
    },
  ],
  'loadStateActions': [
    {
      'action': 'MARK_FULL',
      'enabled': false,
      'disabledCode': 'LOAD_STATE_ALREADY_REPORTED',
      'disabledMessage': 'Kasa zaten bu yük durumundadır.',
    },
    {
      'action': 'MARK_EMPTY',
      'enabled': true,
      'disabledCode': null,
      'disabledMessage': null,
    },
  ],
};
