import { ChangeDetectionStrategy, Component } from '@angular/core';
import { Routes } from '@angular/router';

@Component({
  selector: 'app-admin-pages-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<h1 class="text-heading" style="margin: 0">Team</h1>`,
})
class TeamStubPage {}

export const TEAM_ROUTES: Routes = [{ path: '', component: TeamStubPage }];
