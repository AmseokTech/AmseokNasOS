//--------------------------//
//--------提供内置应用的安全尝鲜展示---------//
//--------Provides a safe preview for built-in app experiences--------//
//-------------------------//
import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

@Component({
  selector: 'app-app-store-preview-center',
  templateUrl: './app-store-preview-center.component.html',
  styleUrl: './app-store-preview-center.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class AppStorePreviewCenterComponent {
  readonly studioSyncJoined = input(false);
  readonly joinStudioSync = output<void>();

  requestStudioSyncPreview(): void {
    this.joinStudioSync.emit();
  }
}
