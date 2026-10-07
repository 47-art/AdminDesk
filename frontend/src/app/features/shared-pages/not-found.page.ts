import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Router } from '@angular/router';
import { Button } from 'primeng/button';

import { ROUTE_PATHS } from '../../core/constants/routes';

@Component({
  selector: 'app-not-found-page',
  imports: [Button],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section style="max-width: 480px; margin: var(--space-3xl) auto; text-align: center">
      <h1 class="text-heading" style="margin: 0 0 var(--space-lg)">We could not find that page</h1>
      <p-button label="Go to dashboard" (onClick)="goHome()" />
    </section>
  `,
})
export class NotFoundPage {
  private readonly router = inject(Router);

  protected goHome(): void {
    void this.router.navigate(['/', ROUTE_PATHS.Dashboard]);
  }
}
