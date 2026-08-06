//--------------------------//
//--------提供内置应用的安全尝鲜展示---------//
//--------Provides a safe preview for built-in app experiences--------//
//-------------------------//
import { ChangeDetectionStrategy, Component, signal } from '@angular/core';

@Component({
  selector: 'app-app-store-preview-center',
  templateUrl: './app-store-preview-center.component.html',
  styleUrl: './app-store-preview-center.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class AppStorePreviewCenterComponent {
  readonly studioSyncJoined = signal(false);
  readonly previewNotice = signal('');

  joinStudioSyncPreview(): void {
    this.studioSyncJoined.set(true);
    this.previewNotice.set('已加入 Studio Sync 尝鲜体验。该状态仅保留在当前应用商店窗口，不会安装应用。');
  }
}
