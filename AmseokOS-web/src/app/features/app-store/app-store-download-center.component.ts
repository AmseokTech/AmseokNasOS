//--------------------------//
//--------提供安全的静态更新与本地下载演示---------//
//--------Provides a safe static update and local download demonstration--------//
//-------------------------//
import { DOCUMENT } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnDestroy, inject, signal } from '@angular/core';

type DemoDownloadState = 'idle' | 'downloading' | 'completed';

const DEMO_DOWNLOAD_URL = '/downloads/studio-sync-demo.txt';
const DEMO_DOWNLOAD_FILE_NAME = 'studio-sync-demo.txt';
const DEMO_DOWNLOAD_STEP = 10;
const DEMO_DOWNLOAD_INTERVAL_MS = 100;

@Component({
  selector: 'app-app-store-download-center',
  templateUrl: './app-store-download-center.component.html',
  styleUrl: './app-store-download-center.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class AppStoreDownloadCenterComponent implements OnDestroy {
  private readonly document = inject(DOCUMENT);
  private downloadTimer: number | undefined;

  readonly downloadState = signal<DemoDownloadState>('idle');
  readonly downloadProgress = signal(0);
  readonly downloadNotice = signal('');

  startDemoDownload(): void {
    if (this.downloadState() === 'downloading') {
      return;
    }

    this.stopDownloadAnimation();
    this.downloadState.set('downloading');
    this.downloadProgress.set(0);
    this.downloadNotice.set('正在准备 Studio Sync 演示包。');
    this.downloadTimer = window.setInterval(() => this.advanceDemoDownload(), DEMO_DOWNLOAD_INTERVAL_MS);
  }

  ngOnDestroy(): void {
    this.stopDownloadAnimation();
  }

  private advanceDemoDownload(): void {
    const progress = Math.min(this.downloadProgress() + DEMO_DOWNLOAD_STEP, 100);
    this.downloadProgress.set(progress);
    this.downloadNotice.set(`正在下载 Studio Sync 演示包：${progress}%。`);

    if (progress === 100) {
      this.stopDownloadAnimation();
      this.triggerBrowserDownload();
      this.downloadState.set('completed');
      this.downloadNotice.set('演示包已交给浏览器下载，将保存到浏览器默认下载目录。');
    }
  }

  private triggerBrowserDownload(): void {
    const downloadLink = this.document.createElement('a');
    downloadLink.href = DEMO_DOWNLOAD_URL;
    downloadLink.download = DEMO_DOWNLOAD_FILE_NAME;
    downloadLink.hidden = true;
    this.document.body.append(downloadLink);
    downloadLink.click();
    downloadLink.remove();
  }

  private stopDownloadAnimation(): void {
    if (this.downloadTimer === undefined) {
      return;
    }

    window.clearInterval(this.downloadTimer);
    this.downloadTimer = undefined;
  }
}
