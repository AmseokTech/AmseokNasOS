//--------------------------//
//--------定义数据卷、目录权限、读写校验与共享管理契约---------//
//--------Defines volume, directory-permission, verification, and share contracts--------//
//-------------------------//

export type StorageAction =
  | 'provisionVolume'
  | 'updatePermissions'
  | 'configureShares'
  | 'verifyReadWrite';

export interface SmbShareSettings {
  readonly enabled: boolean;
  readonly shareName: string | null;
  readonly readOnly: boolean;
  readonly guestAccess: boolean;
  readonly allowedNetwork: string | null;
}

export interface NfsShareSettings {
  readonly enabled: boolean;
  readonly clientNetwork: string | null;
  readonly readOnly: boolean;
}

export interface StorageOperationRequest {
  readonly action: StorageAction;
  readonly arrayId: string | null;
  readonly volumeId: string | null;
  readonly volumeName: string | null;
  readonly ownerName: string | null;
  readonly groupName: string | null;
  readonly directoryMode: string | null;
  readonly smb: SmbShareSettings | null;
  readonly nfs: NfsShareSettings | null;
}

export interface ManagedVolume {
  readonly id: string;
  readonly name: string;
  readonly arrayId: string;
  readonly arrayPath: string;
  readonly fileSystemUuid: string;
  readonly fileSystemType: string;
  readonly mountPath: string;
  readonly mounted: boolean;
  readonly persistentMountEnabled: boolean;
  readonly ownerName: string;
  readonly groupName: string;
  readonly directoryMode: string;
  readonly readWriteVerified: boolean;
  readonly smb: SmbShareSettings;
  readonly nfs: NfsShareSettings;
}

export interface StorageOperationIssue {
  readonly code: string;
  readonly field: string;
  readonly message: string;
}

export interface StorageOperationPreview {
  readonly action: StorageAction;
  readonly requested: Omit<StorageOperationRequest, 'action'> & { readonly kind: number };
  readonly existingVolume: ManagedVolume | null;
  readonly canExecute: boolean;
  readonly previewToken: string | null;
  readonly expiresAt: string | null;
  readonly confirmationPhrase: string;
  readonly blockingIssues: readonly StorageOperationIssue[];
  readonly warnings: readonly string[];
}

export interface StorageOperation {
  readonly operationId: string;
  readonly action: StorageAction;
  readonly status: string;
  readonly resourceId: string;
  readonly volume: ManagedVolume | null;
  readonly errorCode: string | null;
  readonly retryable: boolean;
  readonly createdAt: string;
  readonly updatedAt: string | null;
}
