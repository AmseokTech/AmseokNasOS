import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

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
    await TestBed.configureTestingModule({
      imports: [SettingsPageComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting()
      ]
    }).compileComponents();
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
});
