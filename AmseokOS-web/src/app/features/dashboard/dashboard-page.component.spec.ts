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
    const dashboard = { load: vi.fn(() => of(snapshot)) };
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
    expect(element.textContent).toContain('运行正常');
    expect(element.textContent).toContain('系统查询暂不可用');
    element.querySelector<HTMLButtonElement>('button')?.click();
    await vi.waitFor(() => expect(dashboard.load).toHaveBeenCalledTimes(2));
    fixture.destroy();
  });
});
