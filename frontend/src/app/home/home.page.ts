import { ChangeDetectionStrategy, Component } from '@angular/core';

import { RequestStatus, STATUS_SORT_ORDER } from '../core/constants/statuses';
import { formatDate } from '../shared/formatters/dates';
import { formatInr } from '../shared/formatters/money';
import { StatusBadgeComponent } from '../shared/status-badge/status-badge.component';

/** Temporary landing page that shows the theme, status badges and formatters. */
@Component({
  selector: 'app-home-page',
  imports: [StatusBadgeComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a class="skip-link" href="#main">Skip to main content</a>
    <main id="main" tabindex="-1" style="padding: var(--space-lg)">
      <h1 class="text-display" style="margin: 0 0 var(--space-md)">AdminDesk</h1>
      <p class="text-section">Request statuses</p>
      <div style="display: flex; gap: var(--space-sm); flex-wrap: wrap">
        @for (status of statuses; track status) {
          <app-status-badge [status]="status" />
        }
      </div>
      <p style="margin-top: var(--space-lg)">Sample amount: {{ sampleMoney }}</p>
      <p>Sample date: {{ sampleDate }}</p>
    </main>
  `,
})
export class HomePage {
  protected readonly statuses: readonly RequestStatus[] = STATUS_SORT_ORDER;
  protected readonly sampleMoney = formatInr(1234567);
  protected readonly sampleDate = formatDate(new Date());
}
