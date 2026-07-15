//--------------------------//
//--------核心服务封装控制面 API 连接检查---------//
//--------Core services encapsulate control-plane API health checks--------//
//-------------------------//
import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { HealthStatus } from '../../shared/models/health-status';

@Injectable({ providedIn: 'root' })
export class ApiHealthService {
  private readonly http = inject(HttpClient);

  getHealth(): Observable<HealthStatus> {
    return this.http.get<HealthStatus>('/api/health');
  }
}
