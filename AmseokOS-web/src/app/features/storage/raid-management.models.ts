//--------------------------//
//--------定义 RAID 生命周期预检与持久化操作契约---------//
//--------Defines RAID lifecycle preview and persistent-operation contracts--------//
//-------------------------//

export type RaidAction =
  | 'create'
  | 'delete'
  | 'addDevice'
  | 'removeDevice'
  | 'replaceDevice'
  | 'grow'
  | 'shrink';

export interface RaidOperationRequest {
  readonly action: RaidAction;
  readonly arrayId: string | null;
  readonly arrayName: string | null;
  readonly level: string | null;
  readonly deviceIds: readonly string[];
  readonly sourceDeviceId: string | null;
  readonly targetDeviceCount: number | null;
}

export interface RaidOperationIssue {
  readonly code: string;
  readonly field: string;
  readonly message: string;
}

export interface RaidOperationPreview extends RaidOperationRequest {
  readonly arrayDisplayName: string | null;
  readonly expectedMemberDeviceIds: readonly string[];
  readonly canExecute: boolean;
  readonly previewToken: string | null;
  readonly expiresAt: string | null;
  readonly confirmationPhrase: string;
  readonly blockingIssues: readonly RaidOperationIssue[];
  readonly warnings: readonly string[];
}

export interface RaidOperation {
  readonly operationId: string;
  readonly action: RaidAction;
  readonly status: string;
  readonly resourceId: string;
  readonly arrayId: string | null;
  readonly errorCode: string | null;
  readonly retryable: boolean;
  readonly progressPercentage: number | null;
  readonly createdAt: string;
  readonly updatedAt: string | null;
}
