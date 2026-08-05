//--------------------------//
//--------展示桌面顶部的产品、当前应用与系统摘要---------//
//--------Presents product, active app, and system summary in the desktop top bar--------//
//-------------------------//
import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnDestroy, input, signal } from '@angular/core';

@Component({
  selector: 'app-top-bar',
  imports: [DatePipe],
  templateUrl: './top-bar.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './top-bar.component.scss'
})
export class TopBarComponent implements OnDestroy {
  readonly activeApp = input.required<string>();
  readonly currentTime = signal(new Date());

  private readonly clockInterval = window.setInterval(() => this.currentTime.set(new Date()), 30_000);

  ngOnDestroy(): void {
    window.clearInterval(this.clockInterval);
  }
}
