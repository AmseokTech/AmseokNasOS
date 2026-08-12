export type NetworkAddressingMode = 'dhcp' | 'static';

export interface NetworkConfigurationRequest {
  readonly interfaceId: string;
  readonly mode: NetworkAddressingMode;
  readonly ipAddress: string | null;
  readonly subnetMask: string | null;
  readonly gateway: string | null;
  readonly password: string;
}

export interface NetworkConfigurationPreview {
  readonly interfaceId: string;
  readonly interfaceName: string;
  readonly currentMode: string;
  readonly currentAddresses: readonly string[];
  readonly currentGateway: string | null;
  readonly requestedMode: NetworkAddressingMode;
  readonly requestedIpAddress: string | null;
  readonly requestedSubnetMask: string | null;
  readonly requestedPrefixLength: number | null;
  readonly requestedGateway: string | null;
  readonly canApply: boolean;
  readonly blockingReasons: readonly string[];
  readonly warnings: readonly string[];
}

export interface NetworkConfigurationOperation {
  readonly operationId: string;
  readonly state: 'awaitingConfirmation' | 'confirmed' | 'rolledBack';
  readonly confirmationDeadline: string | null;
}
