//--------------------------//
//--------公开系统设置只读查询边界---------//
//--------Exposes the read-only system settings boundary--------//
//-------------------------//

export { SystemSettingsService } from './system-settings.service';
export { formatBytes, formatUptime, storageUsagePercentage } from './settings-format';
export type {
  NetworkInterfaceInformation,
  SystemAbout
} from './system-settings.models';
