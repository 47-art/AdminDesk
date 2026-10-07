import { ChangeDetectionStrategy, Component } from '@angular/core';
import { Routes } from '@angular/router';

@Component({
  selector: 'app-dashboard-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<h1 class="text-heading" style="margin: 0">Dashboard</h1>`,
})
class DashboardStubPage {}

export const DASHBOARD_ROUTES: Routes = [{ path: '', component: DashboardStubPage }];
