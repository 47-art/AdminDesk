import { ChangeDetectionStrategy, Component } from '@angular/core';
import { Routes } from '@angular/router';

@Component({
  selector: 'app-my-requests-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<h1 class="text-heading" style="margin: 0">My requests</h1>`,
})
class MyRequestsStubPage {}

export const MY_REQUESTS_ROUTES: Routes = [{ path: '', component: MyRequestsStubPage }];
