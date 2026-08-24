export type RoleCode = 'ADMIN' | 'SUPERVISOR' | 'LINE_OPERATOR' | 'WAREHOUSE_OPERATOR'

export interface AuthenticatedUser {
  id: string
  username: string
  fullName: string
  employeeNo: string | null
  roles: RoleCode[]
}

export interface AdminSession {
  accessToken: string
  tokenType: 'Bearer'
  expiresAt: string
  clientType: 'ADMIN_WEB'
  user: AuthenticatedUser
}

export interface Role {
  id: string
  code: RoleCode
  name: string
  isSystem: boolean
  isActive: boolean
}

export interface MachinePermission {
  machineId: string
  canView: boolean
  canOperate: boolean
  canChangeVariant: boolean
  canManageSlots: boolean
}

export interface AdminUser extends AuthenticatedUser {
  isActive: boolean
  lastLoginAt: string | null
  machinePermissions: MachinePermission[]
  concurrencyToken: number
}

export interface Device {
  id: string
  code: string
  name: string
  type: 'PDA' | 'ADMIN_WEB' | 'SERVICE' | 'OTHER'
  isActive: boolean
  metadata: Record<string, unknown>
  concurrencyToken: number
}

export interface Machine {
  id: string
  code: string
  displayName: string
  displayOrder: number
  isActive: boolean
  concurrencyToken: number
}

export interface Variant {
  id: string
  machineId: string
  code: string
  name: string
  displayOrder: number
  isActive: boolean
  defaultLayoutId: string | null
  concurrencyToken: number
}

export type LayoutStatus = 'DRAFT' | 'PUBLISHED' | 'ARCHIVED'

export interface Layout {
  id: string
  machineId: string
  code: string
  name: string
  version: number
  status: LayoutStatus
  slotCount: number
  assignedVariantCount: number
  publishedAt: string | null
  concurrencyToken: number
}

export type SlotRole = 'MACHINE' | 'STAGING' | 'EMPTY_STORAGE' | 'FULL_STORAGE' | 'WAREHOUSE' | 'PICKUP' | 'DROP' | 'SHARED' | 'OTHER'
export type ContainerCategory = 'CONTAINER' | 'PALLET' | 'CART' | 'BOX' | 'MIX_PALLET' | 'OTHER'
export type PartRelationType = 'ALT_PART' | 'RELATED' | 'CONSUMABLE' | 'OTHER'

export interface Slot {
  id: string
  layoutId: string
  code: string
  displayName: string
  role: SlotRole
  defaultContainerTypeId: string | null
  x: number | null
  y: number | null
  width: number | null
  height: number | null
  displayOrder: number
  allowsEmptyLoad: boolean
  allowsFullLoad: boolean
  isEnabled: boolean
  metadata: Record<string, unknown>
  concurrencyToken: number
}

export interface Part {
  id: string
  code: string
  description: string | null
  isActive: boolean
  concurrencyToken: number
}

export interface ProductGroupPart {
  partId: string
  partCode: string
  containerTypeId: string | null
  containerCode: string | null
  relationType: PartRelationType
  lotQuantity: number | null
  displayOrder: number
  isActive: boolean
}

export interface ProductGroup {
  id: string
  variantId: string
  mainPartId: string
  mainPartCode: string
  name: string | null
  displayOrder: number
  isActive: boolean
  parts: ProductGroupPart[]
  concurrencyToken: number
}

export interface ContainerType {
  id: string
  code: string
  name: string
  category: ContainerCategory
  lengthMm: number | null
  widthMm: number | null
  heightMm: number | null
  capacityQuantity: number | null
  isActive: boolean
  concurrencyToken: number
}

export interface DataIssue {
  kind: string
  severity: 'HIGH' | 'MEDIUM' | 'LOW' | string
  entityType: string
  entityId: string
  code: string
  message: string
}

export interface AuditRecord {
  id: number
  actorUserId: string | null
  actorUsername: string | null
  action: string
  entityType: string
  entityId: string | null
  before: unknown
  after: unknown
  ipAddress: string | null
  userAgent: string | null
  correlationId: string | null
  createdAt: string
}

export interface ProblemDetails {
  type?: string
  title: string
  status: number
  detail: string
  instance?: string
  code: string
  correlationId?: string
}

export type JsonObject = Record<string, unknown>

export interface UserDraft {
  username: string
  password: string
  fullName: string
  employeeNo: string | null
  roles: RoleCode[]
}

export interface DeviceDraft {
  code: string
  name: string
  type: Device['type']
  isActive: boolean
  metadata: JsonObject
}

export interface SlotDraft {
  code: string
  displayName: string
  role: SlotRole
  defaultContainerTypeId: string | null
  x: number | null
  y: number | null
  width: number | null
  height: number | null
  displayOrder: number
  allowsEmptyLoad: boolean
  allowsFullLoad: boolean
  isEnabled: boolean
  metadata: JsonObject
}

export interface RdsConnectionStatus {
  enabled: boolean
  reachable: boolean
  stale: boolean
  lastSuccessfulRead: string | null
  baseAddressDisplaySafe: string | null
  businessStatus: number | null
  businessMessage: string | null
  errorCode: string | null
  errorMessage: string | null
}

export interface RdsNavigationProfile {
  id: string
  machineId: string
  machineCode: string
  code: string
  name: string
  version: number
  status: 'DRAFT' | 'PUBLISHED' | 'ARCHIVED'
}

export interface RdsWorkSite {
  id: string
  navigationProfileId: string
  siteId: string
  area: string | null
  groupName: string | null
  type: number | null
  filled: number | null
  locked: number | null
  disabled: number | null
  syncFailed: number | null
  content: string | null
  agvId: string | null
  lastSyncAt: string | null
  executionReady: boolean
}

export interface RdsSlotMapping {
  slotId: string
  layoutId: string
  slotCode: string
  slotDisplayName: string
  slotRole: SlotRole
  machineId: string
  rdsWorkSiteId: string | null
  rdsSiteId: string | null
  navigationProfileId: string | null
  rdsType: number | null
  filled: number | null
  locked: number | null
  disabled: number | null
  syncFailed: number | null
  lastSyncAt: string | null
  executionReady: boolean
}

export interface RdsSyncResult {
  navigationProfileId: string
  received: number
  created: number
  updated: number
  syncedAt: string
  businessCode: number
  businessMessage: string
}

export interface RdsTaskTemplate {
  id: string
  variantId: string
  variantCode: string
  productGroupId: string | null
  productGroupCode: string | null
  operation: string
  rdsTaskLabel: string | null
  priority: number
  config: JsonObject
  isActive: boolean
}

export interface RdsTaskTemplateValidation {
  validated: boolean
  taskLabelExists: boolean
  taskRecordExists: boolean
  code: number
  message: string
}
