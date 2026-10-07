import { FieldType, Priority, RequestAction, StepType } from '../constants/field-types';
import { ApprovalStatus, RequestStatus, StepState } from '../constants/statuses';

export type { FieldType, Priority, RequestAction, StepType, ApprovalStatus, RequestStatus, StepState };

export interface FieldError {
  field: string;
  message: string;
}

export interface ApiErrorBody {
  code: string;
  message: string;
  fieldErrors: FieldError[] | null;
  correlationId: string | null;
}

export interface ApiResponse<T> {
  success: boolean;
  data: T | null;
  error: ApiErrorBody | null;
}

export interface Paged<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export interface AuthUser {
  id: string;
  name: string;
  email: string;
  employeeId: number | null;
  roles: string[];
}

export interface LoginResponse {
  accessToken: string;
  expiresAtUtc: string;
  user: AuthUser;
}

export interface LoginBody {
  email: string;
  password: string;
}

export interface MeResponse {
  id: string;
  name: string;
  email: string;
  employeeId: number | null;
  employeeCode: string | null;
  department: string | null;
  designation: string | null;
  roles: string[];
}

export interface DemoAccount {
  role: string;
  name: string;
  email: string;
  description: string;
}

export interface PublicConfig {
  demoMode: boolean;
  demoPassword: string | null;
  demoAccounts: DemoAccount[];
}

export interface ModuleSummary {
  code: string;
  name: string;
  description: string;
  category: string;
  icon: string;
  prefix: string;
}

export interface FieldOption {
  value: string;
  label: string;
}

export interface FieldDto {
  key: string;
  label: string;
  type: FieldType;
  required: boolean;
  maxLength: number | null;
  min: number | null;
  max: number | null;
  helpText: string | null;
  fullWidth: boolean;
  options: FieldOption[];
  lookupKind: string | null;
}

export interface SectionDto {
  title: string;
  fields: FieldDto[];
}

export interface StepSummary {
  key: string;
  name: string;
  type: StepType;
  actorLabel: string | null;
  actionLabel: string | null;
  captureFields: FieldDto[];
}

export interface ModuleDefinitionDto {
  definitionId: number;
  code: string;
  version: number;
  name: string;
  description: string;
  category: string;
  icon: string;
  prefix: string;
  sections: SectionDto[];
  steps: StepSummary[];
}

export interface CommonFieldsInput {
  projectId: number | null;
  locationId: number | null;
  costCentreId: number | null;
  requiredDate: string | null;
  priority: Priority | null;
  remarks: string | null;
}

export interface CreateRequestBody {
  moduleCode: string;
  definitionId: number;
  common: CommonFieldsInput;
  payload: Record<string, unknown>;
}

export interface ActionRequest {
  action: RequestAction;
  /** Sent only for Reject and Cancel. */
  comment?: string | null;
  rowVersion: number;
  /** Sent only for Complete, keyed by capture field key. */
  captured?: Record<string, unknown> | null;
}

export interface RequesterDto {
  employeeId: number;
  employeeCode: string;
  name: string;
  department: string | null;
}

export interface LabelRef {
  id: number;
  label: string;
}

export interface ResponsibleDto {
  name: string | null;
  role: string | null;
}

export interface RequestStepDto {
  seq: number;
  key: string;
  name: string;
  type: StepType;
  state: StepState;
  actorLabel: string | null;
  actedByName: string | null;
  actedUtc: string | null;
  comment: string | null;
  isCurrent: boolean;
  captured: Record<string, unknown> | null;
  captureFields: FieldDto[];
}

export interface RequestDetail {
  id: number;
  requestNo: string;
  moduleCode: string;
  moduleName: string;
  definitionId: number;
  subject: string;
  rowVersion: number;
  requester: RequesterDto;
  department: string | null;
  project: LabelRef | null;
  location: LabelRef | null;
  costCentre: LabelRef | null;
  requestDate: string;
  requiredDate: string | null;
  priority: Priority | null;
  approvalStatus: ApprovalStatus;
  currentStatus: RequestStatus;
  currentStepKey: string | null;
  currentStepName: string | null;
  responsible: ResponsibleDto | null;
  remarks: string | null;
  payload: Record<string, unknown>;
  lookupLabels: Record<string, string>;
  ageDays: number;
  createdUtc: string;
  closedUtc: string | null;
  definition: ModuleDefinitionDto;
  steps: RequestStepDto[];
  allowedActions: RequestAction[];
  primaryActionLabel: string | null;
  cancelReason: string | null;
  cancelledUtc: string | null;
}

export interface AuditEventDto {
  id: number;
  eventType: string;
  actorName: string | null;
  actorRole: string | null;
  stepKey: string | null;
  fromStatus: string | null;
  toStatus: string | null;
  comment: string | null;
  createdUtc: string;
}

export interface RequestListItem {
  id: number;
  requestNo: string;
  moduleCode: string;
  moduleName: string;
  subject: string;
  requestDate: string;
  requiredDate: string | null;
  priority: Priority | null;
  currentStatus: RequestStatus;
  currentStepName: string | null;
  currentStepType: StepType | null;
  responsibleName: string | null;
  responsibleRole: string | null;
  updatedUtc: string;
  requesterName: string;
  requesterDepartment: string | null;
  ageDays: number;
  primaryActionLabel: string | null;
  captureFields: FieldDto[];
}

export interface DashboardSummary {
  waitingForMe: number;
  total: number;
  pending: number;
  approved: number;
  rejected: number;
  completed: number;
  recent: RequestListItem[];
}

export interface MineQuery {
  page?: number;
  pageSize?: number;
  sort?: 'requestNo' | 'requestDate' | 'status' | 'updatedUtc';
  dir?: 'asc' | 'desc';
  q?: string;
  /** May repeat. */
  status?: RequestStatus[];
  approvalStatus?: ApprovalStatus;
  module?: string;
  from?: string;
  to?: string;
}

export interface InboxQuery {
  page?: number;
  pageSize?: number;
  module?: string;
  requester?: string;
  priority?: Priority;
}

export interface LookupItem {
  id: number;
  code: string;
  label: string;
  secondary: string | null;
}

export interface TeamRow {
  employeeId: number;
  code: string;
  name: string;
  designation: string | null;
  department: string | null;
  location: string | null;
}

export interface PageQuery {
  page?: number;
  pageSize?: number;
  q?: string;
}
