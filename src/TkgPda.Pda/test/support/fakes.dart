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

OperationalContext testContext({
  List<ActiveTransportRequest> activeRequests = const [],
}) => OperationalContext.fromJson({
  'machine': _machineJson,
  'activeVariantId': '40000000-0000-0000-0000-000000000001',
  'activeVariantCode': 'V1',
  'activeVariantName': 'Varyant 1',
  'activeLayoutId': '50000000-0000-0000-0000-000000000001',
  'activeLayoutCode': 'LAYOUT-A',
  'activeLayoutName': 'SH-01-A Yerleşimi',
  'activeNavigationProfileId': null,
  'runtimeRowVersion': 1,
  'variantLayouts': [
    {
      'variantId': '40000000-0000-0000-0000-000000000001',
      'variantCode': 'V1',
      'variantName': 'Varyant 1',
      'layoutId': '50000000-0000-0000-0000-000000000001',
      'layoutCode': 'LAYOUT-A',
      'layoutName': 'SH-01-A Yerleşimi',
      'isDefault': true,
    },
    {
      'variantId': '40000000-0000-0000-0000-000000000002',
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
      'id': '60000000-0000-0000-0000-000000000001',
      'mainPartId': '70000000-0000-0000-0000-000000000001',
      'mainPartCode': '71550BN000-10-HAOS',
      'name': 'Ana Parça',
      'displayOrder': 1,
      'parts': [
        {
          'id': '80000000-0000-0000-0000-000000000001',
          'partId': '70000000-0000-0000-0000-000000000002',
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

class FakePdaRepository implements PdaDataSource {
  List<MachineSummary> machineValues = [testMachine];
  OperationalContext contextValue = testContext();
  ApiProblem? loginProblem;
  Object? contextError;
  Object? createError;
  int loginCount = 0;
  String? lastEmployeeNumber;
  String? lastPassword;
  int createCount = 0;
  int cancelCount = 0;
  int switchCount = 0;
  int loadStateCount = 0;
  int contextCount = 0;
  int meCount = 0;
  String? lastLoadState;
  String? lastIdempotencyKey;
  TransportCommand? lastCommand;
  Completer<TransportResult>? createCompleter;
  Completer<AuthenticatedUser>? meCompleter;

  @override
  Future<LoginSession> login(String employeeNumber, String password) async {
    loginCount++;
    lastEmployeeNumber = employeeNumber;
    lastPassword = password;
    if (loginProblem case final problem?) throw problem;
    return LoginSession(
      accessToken: 'runtime-only-test-token',
      expiresAt: DateTime.now().toUtc().add(const Duration(minutes: 10)),
      user: testUser,
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
  Future<void> updateLoadState({
    required String machineId,
    required String slotId,
    required String loadState,
  }) async {
    loadStateCount++;
    lastLoadState = loadState;
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
