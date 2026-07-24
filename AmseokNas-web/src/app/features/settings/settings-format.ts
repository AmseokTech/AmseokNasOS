//--------------------------//
//--------提供系统设置使用的纯格式化函数---------//
//--------Provides pure formatting helpers used by system settings--------//
//-------------------------//

const BYTE_UNITS = ['B', 'KB', 'MB', 'GB', 'TB', 'PB'] as const;

export function formatBytes(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes < 0) {
    return '—';
  }
  if (bytes === 0) {
    return '0 B';
  }

  const unitIndex = Math.min(
    Math.floor(Math.log(bytes) / Math.log(1024)),
    BYTE_UNITS.length - 1
  );
  const value = bytes / 1024 ** unitIndex;
  return `${new Intl.NumberFormat('zh-CN', {
    maximumFractionDigits: value >= 100 ? 0 : 1
  }).format(value)} ${BYTE_UNITS[unitIndex]}`;
}

export function formatUptime(seconds: number): string {
  if (!Number.isFinite(seconds) || seconds < 0) {
    return '—';
  }

  const totalMinutes = Math.floor(seconds / 60);
  const days = Math.floor(totalMinutes / 1440);
  const hours = Math.floor(totalMinutes % 1440 / 60);
  const minutes = totalMinutes % 60;
  const parts = [
    days ? `${days} 天` : '',
    hours ? `${hours} 小时` : '',
    !days && minutes ? `${minutes} 分钟` : ''
  ].filter(Boolean);
  return parts.join(' ') || '不足 1 分钟';
}

export function storageUsagePercentage(totalBytes: number, usedBytes: number): number {
  if (!Number.isFinite(totalBytes) || !Number.isFinite(usedBytes) || totalBytes <= 0) {
    return 0;
  }
  return Math.min(100, Math.max(0, usedBytes / totalBytes * 100));
}
