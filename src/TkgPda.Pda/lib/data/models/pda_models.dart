import 'dart:convert';

typedef JsonMap = Map<String, dynamic>;

class AuthenticatedUser {
  const AuthenticatedUser({
    required this.id,
    required this.username,
    required this.fullName,
    required this.employeeNo,
    required this.roles,
  });

  final String id;
  final String username;
  final String fullName;
  final String? employeeNo;
  final List<String> roles;

  factory AuthenticatedUser.fromJson(JsonMap json) => AuthenticatedUser(
    id: json['id'] as String,
    username: json['username'] as String? ?? '',
    fullName: json['fullName'] as String? ?? '',
    employeeNo: json['employeeNo'] as String?,
    roles: _strings(json['roles']),
  );
}

class PdaDevice {
  const PdaDevice({
    required this.id,
    required this.code,
    required this.name,
    required this.type,
  });

  final String id;
  final String code;
  final String name;
  final String type;

  factory PdaDevice.fromJson(JsonMap json) => PdaDevice(
    id: json['id'] as String,
    code: json['code'] as String? ?? '',
    name: json['name'] as String? ?? '',
    type: json['type'] as String? ?? '',
  );
}

class LoginSession {
  const LoginSession({
    required this.accessToken,
    required this.expiresAt,
    required this.user,
    required this.device,
  });

  final String accessToken;
  final DateTime expiresAt;
  final AuthenticatedUser user;
  final PdaDevice device;

  factory LoginSession.fromJson(JsonMap json) => LoginSession(
    accessToken: json['accessToken'] as String,
    expiresAt: DateTime.parse(json['expiresAt'] as String),
    user: AuthenticatedUser.fromJson(_map(json['user'])),
    device: PdaDevice.fromJson(_map(json['device'])),
  );
}

class MachinePermissions {
  const MachinePermissions({
    required this.canView,
    required this.canOperate,
    required this.canChangeVariant,
    required this.canManageSlots,
  });

  final bool canView;
  final bool canOperate;
  final bool canChangeVariant;
  final bool canManageSlots;

  factory MachinePermissions.fromJson(JsonMap json) => MachinePermissions(
    canView: json['canView'] as bool? ?? false,
    canOperate: json['canOperate'] as bool? ?? false,
    canChangeVariant: json['canChangeVariant'] as bool? ?? false,
    canManageSlots: json['canManageSlots'] as bool? ?? false,
  );
}

class MachineSummary {
  const MachineSummary({
    required this.id,
    required this.code,
    required this.displayName,
    required this.displayOrder,
    required this.permissions,
  });

  final String id;
  final String code;
  final String displayName;
  final int displayOrder;
  final MachinePermissions permissions;

  factory MachineSummary.fromJson(JsonMap json) => MachineSummary(
    id: json['id'] as String,
    code: json['code'] as String? ?? '',
    displayName: json['displayName'] as String? ?? '',
    displayOrder: _integer(json['displayOrder']),
    permissions: MachinePermissions.fromJson(_map(json['permissions'])),
  );
}

class VariantLayoutOption {
  const VariantLayoutOption({
    required this.variantId,
    required this.variantCode,
    required this.variantName,
    required this.layoutId,
    required this.layoutCode,
    required this.layoutName,
    required this.isDefault,
  });

  final String variantId;
  final String variantCode;
  final String variantName;
  final String layoutId;
  final String layoutCode;
  final String layoutName;
  final bool isDefault;

  factory VariantLayoutOption.fromJson(JsonMap json) => VariantLayoutOption(
    variantId: json['variantId'] as String,
    variantCode: json['variantCode'] as String? ?? '',
    variantName: json['variantName'] as String? ?? '',
    layoutId: json['layoutId'] as String,
    layoutCode: json['layoutCode'] as String? ?? '',
    layoutName: json['layoutName'] as String? ?? '',
    isDefault: json['isDefault'] as bool? ?? false,
  );
}

class RdsWorkSiteProjection {
  const RdsWorkSiteProjection({
    required this.isMapped,
    required this.siteId,
    required this.area,
    required this.groupName,
    required this.type,
    required this.filled,
    required this.locked,
    required this.disabled,
    required this.syncFailed,
    required this.content,
    required this.agvId,
    required this.lastSyncAt,
  });

  final bool isMapped;
  final String? siteId;
  final String? area;
  final String? groupName;
  final int? type;
  final int? filled;
  final int? locked;
  final int? disabled;
  final int? syncFailed;
  final String? content;
  final String? agvId;
  final DateTime? lastSyncAt;

  bool get hasSyncProblem => syncFailed != null && syncFailed != 0;
  bool get isDisabled => disabled != null && disabled != 0;
  bool get isLocked => locked != null && locked != 0;

  factory RdsWorkSiteProjection.fromJson(JsonMap json) => RdsWorkSiteProjection(
    isMapped: json['isMapped'] as bool? ?? false,
    siteId: json['siteId'] as String?,
    area: json['area'] as String?,
    groupName: json['groupName'] as String?,
    type: _nullableInteger(json['type']),
    filled: _nullableInteger(json['filled']),
    locked: _nullableInteger(json['locked']),
    disabled: _nullableInteger(json['disabled']),
    syncFailed: _nullableInteger(json['syncFailed']),
    content: json['content'] as String?,
    agvId: json['agvId'] as String?,
    lastSyncAt: _date(json['lastSyncAt']),
  );
}

class ExecutionReadiness {
  const ExecutionReadiness({
    required this.rdsWorkSiteMapped,
    required this.isExecutionReady,
    required this.readinessCode,
    required this.readinessMessage,
  });

  final bool rdsWorkSiteMapped;
  final bool isExecutionReady;
  final String? readinessCode;
  final String? readinessMessage;

  factory ExecutionReadiness.fromJson(JsonMap json) => ExecutionReadiness(
    rdsWorkSiteMapped: json['rdsWorkSiteMapped'] as bool? ?? false,
    isExecutionReady: json['isExecutionReady'] as bool? ?? false,
    readinessCode: json['readinessCode'] as String?,
    readinessMessage: json['readinessMessage'] as String?,
  );
}

class AvailableAction {
  const AvailableAction({
    required this.action,
    required this.enabled,
    required this.disabledCode,
    required this.disabledMessage,
  });

  final String action;
  final bool enabled;
  final String? disabledCode;
  final String? disabledMessage;

  factory AvailableAction.fromJson(JsonMap json) => AvailableAction(
    action: json['action'] as String? ?? '',
    enabled: json['enabled'] as bool? ?? false,
    disabledCode: json['disabledCode'] as String?,
    disabledMessage: json['disabledMessage'] as String?,
  );
}

class LayoutSlot {
  const LayoutSlot({
    required this.id,
    required this.code,
    required this.displayName,
    required this.role,
    required this.isEnabled,
    required this.transportEligible,
    required this.defaultContainerTypeId,
    required this.defaultContainerCode,
    required this.x,
    required this.y,
    required this.width,
    required this.height,
    required this.displayOrder,
    required this.occupancyState,
    required this.loadState,
    required this.currentPartId,
    required this.currentPartCode,
    required this.currentContainerTypeId,
    required this.currentContainerCode,
    required this.rowVersion,
    required this.rdsWorkSite,
    required this.executionReadiness,
    required this.availableActions,
    required this.loadStateActions,
  });

  final String id;
  final String code;
  final String displayName;
  final String role;
  final bool isEnabled;
  final bool transportEligible;
  final String? defaultContainerTypeId;
  final String? defaultContainerCode;
  final double? x;
  final double? y;
  final double? width;
  final double? height;
  final int displayOrder;
  final String occupancyState;
  final String loadState;
  final String? currentPartId;
  final String? currentPartCode;
  final String? currentContainerTypeId;
  final String? currentContainerCode;
  final int rowVersion;
  final RdsWorkSiteProjection rdsWorkSite;
  final ExecutionReadiness executionReadiness;
  final List<AvailableAction> availableActions;
  final List<AvailableAction> loadStateActions;

  bool get isMachineRegion => role == 'MACHINE';

  factory LayoutSlot.fromJson(JsonMap json) => LayoutSlot(
    id: json['id'] as String,
    code: json['code'] as String? ?? '',
    displayName: json['displayName'] as String? ?? '',
    role: json['role'] as String? ?? 'OTHER',
    isEnabled: json['isEnabled'] as bool? ?? false,
    transportEligible: json['transportEligible'] as bool? ?? false,
    defaultContainerTypeId: json['defaultContainerTypeId'] as String?,
    defaultContainerCode: json['defaultContainerCode'] as String?,
    x: _decimal(json['x']),
    y: _decimal(json['y']),
    width: _decimal(json['width']),
    height: _decimal(json['height']),
    displayOrder: _integer(json['displayOrder']),
    occupancyState: json['occupancyState'] as String? ?? 'UNKNOWN',
    loadState: json['loadState'] as String? ?? 'UNKNOWN',
    currentPartId: json['currentPartId'] as String?,
    currentPartCode: json['currentPartCode'] as String?,
    currentContainerTypeId: json['currentContainerTypeId'] as String?,
    currentContainerCode: json['currentContainerCode'] as String?,
    rowVersion: _integer(json['rowVersion']),
    rdsWorkSite: RdsWorkSiteProjection.fromJson(_map(json['rdsWorkSite'])),
    executionReadiness: ExecutionReadiness.fromJson(
      _map(json['executionReadiness']),
    ),
    availableActions: _maps(
      json['availableActions'],
    ).map(AvailableAction.fromJson).toList(growable: false),
    loadStateActions: _maps(
      json['loadStateActions'],
    ).map(AvailableAction.fromJson).toList(growable: false),
  );
}

class ProductGroupPart {
  const ProductGroupPart({
    required this.id,
    required this.partId,
    required this.partCode,
    required this.containerTypeId,
    required this.containerCode,
    required this.relationType,
    required this.lotQuantity,
    required this.displayOrder,
  });

  final String id;
  final String partId;
  final String partCode;
  final String? containerTypeId;
  final String? containerCode;
  final String relationType;
  final double? lotQuantity;
  final int displayOrder;

  factory ProductGroupPart.fromJson(JsonMap json) => ProductGroupPart(
    id: json['id'] as String,
    partId: json['partId'] as String,
    partCode: json['partCode'] as String? ?? '',
    containerTypeId: json['containerTypeId'] as String?,
    containerCode: json['containerCode'] as String?,
    relationType: json['relationType'] as String? ?? '',
    lotQuantity: _decimal(json['lotQuantity']),
    displayOrder: _integer(json['displayOrder']),
  );
}

class ProductGroup {
  const ProductGroup({
    required this.id,
    required this.mainPartId,
    required this.mainPartCode,
    required this.name,
    required this.displayOrder,
    required this.parts,
  });

  final String id;
  final String mainPartId;
  final String mainPartCode;
  final String? name;
  final int displayOrder;
  final List<ProductGroupPart> parts;

  factory ProductGroup.fromJson(JsonMap json) => ProductGroup(
    id: json['id'] as String,
    mainPartId: json['mainPartId'] as String,
    mainPartCode: json['mainPartCode'] as String? ?? '',
    name: json['name'] as String?,
    displayOrder: _integer(json['displayOrder']),
    parts: _maps(
      json['parts'],
    ).map(ProductGroupPart.fromJson).toList(growable: false),
  );
}

class TaskTemplate {
  const TaskTemplate({
    required this.id,
    required this.variantId,
    required this.productGroupId,
    required this.operation,
    required this.rdsTaskLabel,
    required this.rdsTaskLabelConfigured,
    required this.priority,
  });

  final String id;
  final String variantId;
  final String? productGroupId;
  final String operation;
  final String? rdsTaskLabel;
  final bool rdsTaskLabelConfigured;
  final int priority;

  factory TaskTemplate.fromJson(JsonMap json) => TaskTemplate(
    id: json['id'] as String,
    variantId: json['variantId'] as String,
    productGroupId: json['productGroupId'] as String?,
    operation: json['operation'] as String? ?? '',
    rdsTaskLabel: json['rdsTaskLabel'] as String?,
    rdsTaskLabelConfigured: json['rdsTaskLabelConfigured'] as bool? ?? false,
    priority: _integer(json['priority']),
  );
}

class ExecutionProjection {
  const ExecutionProjection({
    required this.state,
    required this.rdsTaskRecordId,
    required this.rdsTaskLabel,
    required this.outOrderNo,
    required this.agvId,
    required this.rdsStatus,
    required this.lastSyncAt,
    required this.failureCode,
    required this.failureCategory,
    required this.operatorMessage,
    required this.technicalMessage,
    required this.correlationId,
  });

  final String state;
  final String? rdsTaskRecordId;
  final String? rdsTaskLabel;
  final String? outOrderNo;
  final String? agvId;
  final int? rdsStatus;
  final DateTime? lastSyncAt;
  final String? failureCode;
  final String? failureCategory;
  final String? operatorMessage;
  final String? technicalMessage;
  final String correlationId;

  factory ExecutionProjection.fromJson(JsonMap json) => ExecutionProjection(
    state: json['state'] as String? ?? 'REQUESTED',
    rdsTaskRecordId: json['rdsTaskRecordId'] as String?,
    rdsTaskLabel: json['rdsTaskLabel'] as String?,
    outOrderNo: json['outOrderNo'] as String?,
    agvId: json['agvId'] as String?,
    rdsStatus: _nullableInteger(json['rdsStatus']),
    lastSyncAt: _date(json['lastSyncAt']),
    failureCode: json['failureCode'] as String?,
    failureCategory: json['failureCategory'] as String?,
    operatorMessage: json['operatorMessage'] as String?,
    technicalMessage: json['technicalMessage'] as String?,
    correlationId: json['correlationId'] as String? ?? '',
  );
}

class ActiveTransportRequest {
  const ActiveTransportRequest({
    required this.requestId,
    required this.requestNo,
    required this.operation,
    required this.status,
    required this.requestedBy,
    required this.requestedByUsername,
    required this.isMine,
    required this.sourceSlotId,
    required this.destinationSlotId,
    required this.productGroupId,
    required this.partId,
    required this.containerTypeId,
    required this.priority,
    required this.createdAt,
    required this.canCancel,
    required this.cancelDisabledCode,
    required this.cancelDisabledMessage,
    required this.execution,
  });

  final String requestId;
  final int requestNo;
  final String operation;
  final String status;
  final String? requestedBy;
  final String? requestedByUsername;
  final bool isMine;
  final String? sourceSlotId;
  final String? destinationSlotId;
  final String? productGroupId;
  final String? partId;
  final String? containerTypeId;
  final int priority;
  final DateTime createdAt;
  final bool canCancel;
  final String? cancelDisabledCode;
  final String? cancelDisabledMessage;
  final ExecutionProjection execution;

  factory ActiveTransportRequest.fromJson(JsonMap json) =>
      ActiveTransportRequest(
        requestId: json['requestId'] as String,
        requestNo: _integer(json['requestNo']),
        operation: json['operation'] as String? ?? '',
        status: json['status'] as String? ?? '',
        requestedBy: json['requestedBy'] as String?,
        requestedByUsername: json['requestedByUsername'] as String?,
        isMine: json['isMine'] as bool? ?? false,
        sourceSlotId: json['sourceSlotId'] as String?,
        destinationSlotId: json['destinationSlotId'] as String?,
        productGroupId: json['productGroupId'] as String?,
        partId: json['partId'] as String?,
        containerTypeId: json['containerTypeId'] as String?,
        priority: _integer(json['priority']),
        createdAt: DateTime.parse(json['createdAt'] as String),
        canCancel: json['canCancel'] as bool? ?? false,
        cancelDisabledCode: json['cancelDisabledCode'] as String?,
        cancelDisabledMessage: json['cancelDisabledMessage'] as String?,
        execution: ExecutionProjection.fromJson(_map(json['execution'])),
      );
}

class OperationalContext {
  const OperationalContext({
    required this.machine,
    required this.activeVariantId,
    required this.activeVariantCode,
    required this.activeVariantName,
    required this.activeLayoutId,
    required this.activeLayoutCode,
    required this.activeLayoutName,
    required this.activeNavigationProfileId,
    required this.runtimeRowVersion,
    required this.variantLayouts,
    required this.slots,
    required this.productGroups,
    required this.activeTransportRequests,
    required this.taskTemplates,
  });

  final MachineSummary machine;
  final String? activeVariantId;
  final String? activeVariantCode;
  final String? activeVariantName;
  final String? activeLayoutId;
  final String? activeLayoutCode;
  final String? activeLayoutName;
  final String? activeNavigationProfileId;
  final int runtimeRowVersion;
  final List<VariantLayoutOption> variantLayouts;
  final List<LayoutSlot> slots;
  final List<ProductGroup> productGroups;
  final List<ActiveTransportRequest> activeTransportRequests;
  final List<TaskTemplate> taskTemplates;

  factory OperationalContext.fromJson(JsonMap json) => OperationalContext(
    machine: MachineSummary.fromJson(_map(json['machine'])),
    activeVariantId: json['activeVariantId'] as String?,
    activeVariantCode: json['activeVariantCode'] as String?,
    activeVariantName: json['activeVariantName'] as String?,
    activeLayoutId: json['activeLayoutId'] as String?,
    activeLayoutCode: json['activeLayoutCode'] as String?,
    activeLayoutName: json['activeLayoutName'] as String?,
    activeNavigationProfileId: json['activeNavigationProfileId'] as String?,
    runtimeRowVersion: _integer(json['runtimeRowVersion']),
    variantLayouts: _maps(
      json['variantLayouts'],
    ).map(VariantLayoutOption.fromJson).toList(growable: false),
    slots: _maps(
      json['slots'],
    ).map(LayoutSlot.fromJson).toList(growable: false),
    productGroups: _maps(
      json['productGroups'],
    ).map(ProductGroup.fromJson).toList(growable: false),
    activeTransportRequests: _maps(
      json['activeTransportRequests'],
    ).map(ActiveTransportRequest.fromJson).toList(growable: false),
    taskTemplates: _maps(
      json['taskTemplates'],
    ).map(TaskTemplate.fromJson).toList(growable: false),
  );
}

class TransportCommand {
  const TransportCommand({
    required this.machineId,
    required this.variantId,
    required this.layoutId,
    required this.navigationProfileId,
    required this.productGroupId,
    required this.partId,
    required this.containerTypeId,
    required this.operation,
    required this.sourceSlotId,
    required this.destinationSlotId,
    required this.priority,
  });

  final String machineId;
  final String variantId;
  final String layoutId;
  final String? navigationProfileId;
  final String? productGroupId;
  final String? partId;
  final String? containerTypeId;
  final String operation;
  final String? sourceSlotId;
  final String? destinationSlotId;
  final int priority;

  JsonMap toJson() => {
    'machineId': machineId,
    'variantId': variantId,
    'layoutId': layoutId,
    'navigationProfileId': navigationProfileId,
    'productGroupId': productGroupId,
    'partId': partId,
    'containerTypeId': containerTypeId,
    'operation': operation,
    'sourceSlotId': sourceSlotId,
    'destinationSlotId': destinationSlotId,
    'priority': priority,
  };

  String get materialSignature => jsonEncode(toJson());
}

class TransportResult {
  const TransportResult({
    required this.id,
    required this.requestNo,
    required this.status,
    required this.operation,
    required this.idempotentReplay,
  });

  final String id;
  final int requestNo;
  final String status;
  final String operation;
  final bool idempotentReplay;

  factory TransportResult.fromJson(JsonMap json) => TransportResult(
    id: json['id'] as String,
    requestNo: _integer(json['requestNo']),
    status: json['status'] as String? ?? '',
    operation: json['operation'] as String? ?? '',
    idempotentReplay: json['idempotentReplay'] as bool? ?? false,
  );
}

JsonMap _map(Object? value) => value is Map<String, dynamic> ? value : {};

List<JsonMap> _maps(Object? value) => value is List
    ? value.whereType<Map<String, dynamic>>().toList(growable: false)
    : const [];

List<String> _strings(Object? value) => value is List
    ? value.whereType<String>().toList(growable: false)
    : const [];

int _integer(Object? value) => value is num ? value.toInt() : 0;

int? _nullableInteger(Object? value) => value is num ? value.toInt() : null;

double? _decimal(Object? value) => value is num ? value.toDouble() : null;

DateTime? _date(Object? value) =>
    value is String && value.isNotEmpty ? DateTime.tryParse(value) : null;
