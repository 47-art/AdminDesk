import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { TagModule } from 'primeng/tag';

import { REQUEST_STATUS_STYLES, RequestStatus } from '../../core/constants/statuses';

@Component({
  selector: 'app-status-badge',
  imports: [TagModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p-tag
      [value]="style().label"
      [icon]="'pi ' + style().icon"
      [style]="{
        background: style().background,
        color: style().text,
        'font-size': size() === 'large' ? '14px' : '12px',
        'font-weight': '600',
        padding: '4px 8px',
        'border-radius': '6px'
      }"
    />
  `,
})
export class StatusBadgeComponent {
  readonly status = input.required<RequestStatus>();
  readonly size = input<'normal' | 'large'>('normal');

  protected readonly style = computed(() => REQUEST_STATUS_STYLES[this.status()]);
}
