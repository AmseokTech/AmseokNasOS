import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';

import type { DashboardSnapshot } from './dashboard.models';
import { DashboardPageComponent } from './dashboard-page.component';
import { DashboardService } from './dashboard.service';

describe('DashboardPageComponent', () => {
  it('renders partial state and offers recovery refresh', async () => {
    const snapshot: DashboardSnapshot = {
      health: { value: { status: 'Healthy' }, error: null },
      about: { value: null, error: '系统查询暂不可用' },
      network: { value: [], error: null },
      storage: { value: { disks: [], arrays: [] }, error: null },
      volumes: { value: [], error: null },
      smart: { value: { queried: 0, healthy: 0, warning: 0, failing: 0, unsupported: 0, unknown: 0 }, error: null }
    };
    const dashboard = {
      load: vi.fn(() => of(snapshot)),
      samplePerformance: vi.fn(() => of({
        capturedAtUnixMilliseconds: Date.now(),
        cpu: {
          model: 'Test CPU', physicalCoreCount: 1, logicalProcessorCount: 2,
          currentFrequencyMhz: 2500, maximumFrequencyMhz: 3000,
          l1CacheBytes: 64, l2CacheBytes: 1024, l3CacheBytes: 4096,
          utilizationPercent: 35,
          logicalProcessors: [
            { id: 'cpu0', utilizationPercent: 30 },
            { id: 'cpu1', utilizationPercent: 40 }
          ]
        },
        memory: {
          totalBytes: 1024, usedBytes: 512, availableBytes: 512, cachedBytes: 128,
          swapTotalBytes: 0, swapUsedBytes: 0, utilizationPercent: 50
        },
        disks: [], networks: [], gpus: []
      }))
    };
    await TestBed.configureTestingModule({
      imports: [DashboardPageComponent],
      providers: [
        provideNoopAnimations(),
        { provide: DashboardService, useValue: dashboard }
      ]
    }).compileComponents();

    const fixture = TestBed.createComponent(DashboardPageComponent);
    fixture.detectChanges();
    await vi.waitFor(() => expect(dashboard.load).toHaveBeenCalledOnce());
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.textContent).toContain('实时更新');
    expect(element.textContent).toContain('Test CPU');
    expect(element.textContent).toContain('逻辑处理器 0');
    expect(element.textContent).toContain('L3 缓存');
    expect(element.textContent).toContain('系统查询暂不可用');
    expect(element.textContent).not.toContain('SYSTEM OVERVIEW');
    expect(element.textContent).not.toContain('系统概览');
    expect(element.textContent).not.toContain('汇总控制面、主机、网络、磁盘、阵列与数据卷的只读状态。');
    expect(element.querySelector('.dashboard')?.getAttribute('aria-label')).toBe('系统概览');
    element.querySelector<HTMLButtonElement>('button')?.click();
    await vi.waitFor(() => expect(dashboard.load).toHaveBeenCalledTimes(2));
    expect(dashboard.samplePerformance).toHaveBeenCalledTimes(2);
    fixture.destroy();
  });
});
