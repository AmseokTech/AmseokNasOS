//--------------------------//
//--------管理桌面控制中心的快捷控制与系统面板主题---------//
//--------Manages desktop quick controls and the system panel theme--------//
//-------------------------//
import { DOCUMENT } from '@angular/common';
import { ChangeDetectionStrategy, Component, effect, inject, input, output, signal } from '@angular/core';

type OptionalControlId = 'battery' | 'display' | 'networkSpeed' | 'sound';

@Component({
  selector: 'app-control-center',
  templateUrl: './control-center.component.html',
  styleUrl: './control-center.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ControlCenterComponent {
  private readonly document = inject(DOCUMENT);

  readonly open = input.required<boolean>();
  readonly wifiEnabled = input.required<boolean>();
  readonly selectedWifiName = input.required<string>();
  readonly wifiRequested = output<void>();

  readonly darkMode = signal(this.readStoredTheme());
  readonly bluetoothEnabled = signal(true);
  readonly editingControls = signal(false);
  readonly addingControls = signal(false);
  readonly visibleControls = signal<Record<OptionalControlId, boolean>>({
    battery: true,
    display: true,
    networkSpeed: true,
    sound: true
  });
  readonly brightness = signal(78);
  readonly volume = signal(58);
  readonly batteryLevel = signal(27);
  readonly lowPowerModeEnabled = signal(false);
  readonly downloadRate = signal('24.8 MB/s');
  readonly uploadRate = signal('3.2 MB/s');

  constructor() {
    this.applyTheme(this.darkMode());

    effect((onCleanup) => {
      const browserWindow = this.document.defaultView;
      if (!browserWindow || !this.open() || !this.isControlVisible('networkSpeed')) {
        return;
      }

      this.updateNetworkRates();
      const timer = browserWindow.setInterval(() => this.updateNetworkRates(), 2400);
      onCleanup(() => browserWindow.clearInterval(timer));
    });
  }

  setDarkMode(enabled: boolean): void {
    this.darkMode.set(enabled);
    this.applyTheme(enabled);

    try {
      this.document.defaultView?.localStorage.setItem('amseokos-color-theme', enabled ? 'dark' : 'light');
    } catch {
      // Theme switching remains available when browser storage is blocked
    }
  }

  toggleBluetooth(): void {
    this.bluetoothEnabled.update((isEnabled) => !isEnabled);
  }

  toggleLowPowerMode(): void {
    this.lowPowerModeEnabled.update((isEnabled) => !isEnabled);
  }

  toggleEditingControls(): void {
    this.editingControls.update((isEditing) => {
      if (isEditing) {
        this.addingControls.set(false);
      }

      return !isEditing;
    });
  }

  toggleAddingControls(): void {
    this.addingControls.update((isAdding) => !isAdding);
  }

  isControlVisible(id: OptionalControlId): boolean {
    return this.visibleControls()[id];
  }

  setControlVisible(id: OptionalControlId, visible: boolean): void {
    this.visibleControls.update((controls) => ({ ...controls, [id]: visible }));

    if (visible) {
      this.addingControls.set(false);
    }
  }

  setBrightness(event: Event): void {
    this.brightness.set(this.readRangeValue(event));
  }

  setVolume(event: Event): void {
    this.volume.set(this.readRangeValue(event));
  }

  toggleMute(): void {
    this.volume.update((level) => (level === 0 ? 58 : 0));
  }

  private updateNetworkRates(): void {
    const download = 18 + Math.random() * 18;
    const upload = 1.2 + Math.random() * 5.6;
    this.downloadRate.set(`${download.toFixed(1)} MB/s`);
    this.uploadRate.set(`${upload.toFixed(1)} MB/s`);
  }

  private readStoredTheme(): boolean {
    try {
      const stored = this.document.defaultView?.localStorage.getItem('amseokos-color-theme');
      return stored ? stored === 'dark' : true;
    } catch {
      return true;
    }
  }

  private applyTheme(dark: boolean): void {
    this.document.documentElement.dataset['theme'] = dark ? 'dark' : 'light';
    this.document.documentElement.style.colorScheme = dark ? 'dark' : 'light';
  }

  private readRangeValue(event: Event): number {
    return Number((event.target as HTMLInputElement).value);
  }
}
