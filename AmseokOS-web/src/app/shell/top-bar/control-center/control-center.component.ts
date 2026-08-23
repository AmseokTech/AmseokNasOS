//--------------------------//
//--------管理桌面控制中心的快捷控制与系统面板主题---------//
//--------Manages desktop quick controls and the system panel theme--------//
//-------------------------//
import { DOCUMENT } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, input, output, signal } from '@angular/core';

type OptionalControlId = 'battery' | 'display' | 'screenMirroring' | 'sound' | 'stageManager';

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
  readonly airdropEnabled = signal(false);
  readonly focusEnabled = signal(false);
  readonly stageManagerEnabled = signal(false);
  readonly screenMirroringEnabled = signal(false);
  readonly nightShiftEnabled = signal(false);
  readonly lowPowerModeEnabled = signal(false);
  readonly editingControls = signal(false);
  readonly addingControls = signal(false);
  readonly visibleControls = signal<Record<OptionalControlId, boolean>>({
    battery: true,
    display: true,
    screenMirroring: true,
    sound: true,
    stageManager: true
  });
  readonly brightness = signal(78);
  readonly volume = signal(58);
  readonly batteryLevel = signal(27);

  constructor() {
    this.applyTheme(this.darkMode());
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

  toggleAirdrop(): void {
    this.airdropEnabled.update((isEnabled) => !isEnabled);
  }

  toggleFocus(): void {
    this.focusEnabled.update((isEnabled) => !isEnabled);
  }

  toggleStageManager(): void {
    this.stageManagerEnabled.update((isEnabled) => !isEnabled);
  }

  toggleScreenMirroring(): void {
    this.screenMirroringEnabled.update((isEnabled) => !isEnabled);
  }

  toggleNightShift(): void {
    this.nightShiftEnabled.update((isEnabled) => !isEnabled);
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
