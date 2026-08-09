import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import {
  LANGUAGE_SELECTION_STORAGE_KEY,
  LANGUAGE_STORAGE_KEY,
  LanguageService
} from '../../core/i18n';
import { SettingsPageComponent } from './settings-page.component';
import type {
  NetworkInterfaceInformation,
  SystemAbout
} from './system-settings.models';

const about: SystemAbout = {
  hostName: 'nas-test',
  operatingSystem: 'AmseokOS',
  kernelVersion: '6.12.0',
  uptimeSeconds: 90061,
  cpu: {
    model: 'Test CPU',
    physicalCoreCount: 4,
    logicalProcessorCount: 8,
    currentFrequencyMhz: 2400,
    maximumFrequencyMhz: 4200
  },
  memory: { totalBytes: 32 * 1024 ** 3 },
  systemStorage: {
    source: '/dev/test',
    stableId: 'test-disk',
    model: 'Test Disk',
    totalBytes: 1024,
    usedBytes: 512,
    availableBytes: 512
  }
};

const network: NetworkInterfaceInformation = {
  id: 'mac:00:11:22:33:44:55',
  name: 'enp1s0',
  model: 'Test Ethernet',
  driver: 'test-driver',
  macAddress: '00:11:22:33:44:55',
  linkState: 'up',
  speedMbps: 1000,
  duplex: 'full',
  mtu: 1500,
  configurationMode: 'dhcp',
  addresses: ['192.168.1.10/24'],
  gateway: '192.168.1.1',
  dnsServers: ['192.168.1.1']
};

describe('SettingsPageComponent', () => {
  beforeEach(async () => {
    window.localStorage.removeItem(LANGUAGE_STORAGE_KEY);
    window.localStorage.removeItem(LANGUAGE_SELECTION_STORAGE_KEY);
    await TestBed.configureTestingModule({
      imports: [SettingsPageComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting()
      ]
    }).compileComponents();
  });

  afterEach(() => {
    window.localStorage.removeItem(LANGUAGE_STORAGE_KEY);
    window.localStorage.removeItem(LANGUAGE_SELECTION_STORAGE_KEY);
  });

  it('switches the settings interface to English immediately and persists the choice', () => {
    const fixture = TestBed.createComponent(SettingsPageComponent);
    const http = TestBed.inject(HttpTestingController);

    fixture.detectChanges();
    http.expectOne('/api/system/about').flush(about);
    fixture.detectChanges();

    const languageButton = [...fixture.nativeElement.querySelectorAll('nav button')]
      .find((button: HTMLButtonElement) => button.textContent?.includes('语言与地区'));
    languageButton?.click();
    fixture.detectChanges();

    const englishOption = fixture.nativeElement.querySelector(
      'input[name="interface-language"][value="en-US"]'
    ) as HTMLInputElement;
    englishOption.click();
    TestBed.flushEffects();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('nav h1')?.textContent).toContain('System Settings');
    expect(compiled.textContent).toContain('Changes apply to this browser immediately.');
    expect(compiled.textContent).toContain('Interface Preview');
    expect(compiled.querySelector('.language-option--selected')?.textContent).toContain('English');
    expect(compiled.querySelector('.language-option--selected .language-option__check')).not.toBeNull();
    expect(TestBed.inject(LanguageService).language()).toBe('en-US');
    expect(window.localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBe('en-US');
    expect(window.localStorage.getItem(LANGUAGE_SELECTION_STORAGE_KEY)).toBe('true');
    expect(document.documentElement.lang).toBe('en-US');
    http.verify();
  });

  it('loads About first and switches to the read-only Network view', () => {
    const fixture = TestBed.createComponent(SettingsPageComponent);
    const http = TestBed.inject(HttpTestingController);

    fixture.detectChanges();
    http.expectOne('/api/system/about').flush(about);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Test CPU');

    const networkButton = [...fixture.nativeElement.querySelectorAll('nav button')]
      .find((button: HTMLButtonElement) => button.textContent?.includes('网络'));
    networkButton?.click();
    fixture.detectChanges();
    http.expectOne('/api/network/interfaces').flush([network]);
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('192.168.1.10/24');
    const configureButton = [...compiled.querySelectorAll('button')]
      .find((button) => button.textContent?.includes('设置 IP 地址'));
    expect(configureButton?.disabled).toBe(true);
    http.verify();
  });

  it('shows a recoverable error state when the privileged service is unavailable', () => {
    const fixture = TestBed.createComponent(SettingsPageComponent);
    const http = TestBed.inject(HttpTestingController);

    fixture.detectChanges();
    http.expectOne('/api/system/about').flush(
      { detail: '底层系统查询服务当前不可用' },
      { status: 503, statusText: 'Service Unavailable' }
    );
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('底层系统查询服务当前不可用');
    const retry = [...fixture.nativeElement.querySelectorAll('button')]
      .find((button: HTMLButtonElement) => button.textContent?.includes('重新加载'));
    retry?.click();
    http.expectOne('/api/system/about').flush(about);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('nas-test');
    http.verify();
  });

  it('shows physical disks and opens the guarded RAID management workflow', () => {
    const fixture = TestBed.createComponent(SettingsPageComponent);
    const http = TestBed.inject(HttpTestingController);

    fixture.detectChanges();
    http.expectOne('/api/system/about').flush(about);
    fixture.detectChanges();

    const storageButton = [...fixture.nativeElement.querySelectorAll('nav button')]
      .find((button: HTMLButtonElement) => button.textContent?.includes('磁盘管理'));
    storageButton?.click();
    fixture.detectChanges();

    http.expectOne('/api/storage/disks').flush([
      {
        id: 'wwn:test-system-disk',
        stable: true,
        identityConflict: false,
        topologyComplete: true,
        name: 'sda',
        path: '/dev/sda',
        model: 'System SSD',
        serialNumber: 'TEST-SERIAL',
        wwn: 'test-system-disk',
        sizeBytes: 1024 ** 4,
        logicalSectorBytes: 512,
        physicalSectorBytes: 4096,
        rotational: false,
        removable: false,
        readOnly: false,
        partitions: [],
        mountPoints: ['/'],
        systemDevice: true,
        swap: false,
        raidMember: false,
        inUse: true,
        dependentDevices: []
      }
    ]);
    http.expectOne('/api/raid/arrays').flush([
      {
        id: 'md-uuid:test-array',
        name: 'md0',
        path: '/dev/md0',
        uuid: 'test-array',
        level: 'raid1',
        state: 'clean',
        metadataVersion: '1.2',
        sizeBytes: 512 * 1024 ** 3,
        configuredDeviceCount: 2,
        degradedDeviceCount: 0,
        syncAction: 'idle',
        syncCompletedSectors: null,
        syncTotalSectors: null,
        members: [
          { name: 'sdb1', path: '/dev/sdb1', state: 'in_sync', slot: 0 },
          { name: 'sdc1', path: '/dev/sdc1', state: 'in_sync', slot: 1 }
        ]
      }
    ]);
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('md0');
    expect(compiled.textContent).toContain('RAID1');
    expect(compiled.textContent).toContain('System SSD');
    expect(compiled.textContent).toContain('系统盘');

    const createButton = [...compiled.querySelectorAll('button')]
      .find((button) => button.textContent?.includes('创建阵列'));
    const modifyButton = [...compiled.querySelectorAll('button')]
      .find((button) => button.textContent?.includes('修改阵列'));
    expect(createButton?.disabled).toBe(false);
    expect(modifyButton?.disabled).toBe(false);
    modifyButton?.click();
    fixture.detectChanges();
    expect(compiled.textContent).toContain('高风险操作');
    expect(compiled.textContent).toContain('扩容阵列');
    expect(compiled.textContent).toContain('再次核对稳定磁盘身份');
    http.verify();
  });
});
