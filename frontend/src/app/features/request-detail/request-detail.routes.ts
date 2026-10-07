import { ChangeDetectionStrategy, Component } from '@angular/core';
import { Routes } from '@angular/router';

@Component({
  selector: 'app-request-detail-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<h1 class="text-heading" style="margin: 0">Request</h1>`,
})
class RequestDetailStubPage {}

export const REQUEST_DETAIL_ROUTES: Routes = [{ path: '', component: RequestDetailStubPage }];
