import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import type { NetworkInterfaceInformation } from '../system-settings.models';
import { NetworkSettingsComponent } from './network-settings.component';

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

describe('NetworkSettingsComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [NetworkSettingsComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();
  });

  it('previews, applies and confirms a static IPv4 configuration', () => {
    const fixture = TestBed.createComponent(NetworkSettingsComponent);
    const component = fixture.componentInstance;
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    http.expectOne('/api/network/interfaces').flush([network]);

    component.openEditor(network);
    component.form.setValue({
      mode: 'static',
      ipAddress: '192.168.1.20',
      subnetMask: '255.255.255.0',
      gateway: '192.168.1.1',
      password: 'Secret123!'
    });
    component.createPreview(network.id);
    const preview = http.expectOne('/api/network/configuration-previews');
    expect(preview.request.body.password).toBe('Secret123!');
    preview.flush({
      interfaceId: network.id,
      interfaceName: network.name,
      currentMode: 'dhcp',
      currentAddresses: network.addresses,
      currentGateway: network.gateway,
      requestedMode: 'static',
      requestedIpAddress: '192.168.1.20',
      requestedSubnetMask: '255.255.255.0',
      requestedPrefixLength: 24,
      requestedGateway: '192.168.1.1',
      canApply: true,
      blockingReasons: [],
      warnings: ['network.management_connection_may_be_interrupted']
    });

    component.apply(network.id);
    http.expectOne('/api/network/configuration-operations').flush({
      operationId: '11111111-1111-1111-1111-111111111111',
      state: 'awaitingConfirmation',
      confirmationDeadline: '2026-08-12T12:02:00Z'
    });
    expect(component.form.controls.password.value).toBe('');

    component.confirm();
    http.expectOne(
      '/api/network/configuration-operations/11111111-1111-1111-1111-111111111111/confirm'
    ).flush({
      operationId: '11111111-1111-1111-1111-111111111111',
      state: 'confirmed',
      confirmationDeadline: null
    });
    http.expectOne('/api/network/interfaces').flush([network]);
    expect(component.operation()?.state).toBe('confirmed');
    http.verify();
  });

  it('can explicitly roll back a pending DHCP change', () => {
    const fixture = TestBed.createComponent(NetworkSettingsComponent);
    const component = fixture.componentInstance;
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    http.expectOne('/api/network/interfaces').flush([network]);
    component.operation.set({
      operationId: '22222222-2222-2222-2222-222222222222',
      state: 'awaitingConfirmation',
      confirmationDeadline: '2026-08-12T12:02:00Z'
    });

    component.rollback();

    http.expectOne(
      '/api/network/configuration-operations/22222222-2222-2222-2222-222222222222/rollback'
    ).flush({
      operationId: '22222222-2222-2222-2222-222222222222',
      state: 'rolledBack',
      confirmationDeadline: null
    });
    http.expectOne('/api/network/interfaces').flush([network]);
    expect(component.operation()?.state).toBe('rolledBack');
    http.verify();
  });
});
