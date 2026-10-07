import { ChangeDetectionStrategy, Component } from '@angular/core';
import { Routes } from '@angular/router';

@Component({
  selector: 'app-inbox-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<h1 class="text-heading" style="margin: 0">Waiting for me</h1>`,
})
class InboxStubPage {}

export const INBOX_ROUTES: Routes = [{ path: '', component: InboxStubPage }];
