//--------------------------//
//--------提供安全的静态更新与下载演示---------//
//--------Provides a safe static update and download demonstration--------//
//-------------------------//
import { ChangeDetectionStrategy, Component, signal } from '@angular/core';

@Component({
  selector: 'app-app-store-download-center',
  templateUrl: './app-store-download-center.component.html',
  styleUrl: './app-store-download-center.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class AppStoreDownloadCenterComponent {
  readonly downloadNotice = signal('');

  startDemoDownload(): void {
    this.downloadNotice.set('已开始下载 Studio Sync 演示包。该文件只包含说明文本，不会安装应用。');
  }
}
