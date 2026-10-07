import { ChangeDetectionStrategy, Component } from '@angular/core';
import { Router } from '@angular/router';
import { inject } from '@angular/core';
import { Button } from 'primeng/button';

import { ROUTE_PATHS } from '../../core/constants/routes';

@Component({
  selector: 'app-forbidden-page',
  imports: [Button],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section style="max-width: 480px; margin: var(--space-3xl) auto; text-align: center">
      <h1 class="text-heading" style="margin: 0 0 var(--space-sm)">You do not have access to this page</h1>
      <p style="margin: 0 0 var(--space-lg)">Ask an administrator if you think this is a mistake.</p>
      <p-button label="Go to dashboard" (onClick)="goHome()" />
    </section>
  `,
})
export class ForbiddenPage {
  private readonly router = inject(Router);

  protected goHome(): void {
    void this.router.navigate(['/', ROUTE_PATHS.Dashboard]);
  }
}
