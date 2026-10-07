import { ChangeDetectionStrategy, Component } from '@angular/core';
import { Routes } from '@angular/router';

@Component({
  selector: 'app-new-request-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<h1 class="text-heading" style="margin: 0">New request</h1>`,
})
class NewRequestStubPage {}

export const NEW_REQUEST_ROUTES: Routes = [{ path: '', component: NewRequestStubPage }];
