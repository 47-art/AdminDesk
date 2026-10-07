import { ChangeDetectionStrategy, Component, computed, inject, output } from '@angular/core';
import { MenuItem } from 'primeng/api';
import { Menu } from 'primeng/menu';
import { Popover } from 'primeng/popover';

import { AuthService } from '../core/auth/auth.service';
import { NotificationService } from '../core/notifications/notification.service';

@Component({
  selector: 'app-topbar',
  imports: [Menu, Popover],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    .bar {
      display: flex;
      align-items: center;
      gap: var(--space-sm);
      height: var(--topbar-height);
      padding: 0 var(--space-md);
      background: #ffffff;
      border-bottom: 1px solid var(--p-content-border-color, #d8e2ee);
      box-sizing: border-box;
    }
    .icon-btn {
      border: 0;
      background: transparent;
      width: 40px;
      height: 40px;
      border-radius: 6px;
      cursor: pointer;
      color: inherit;
    }
    .icon-btn:hover {
      background: var(--p-highlight-background, #eaf1f9);
    }
    .name {
      font-size: 20px;
      font-weight: 600;
      flex: 1;
    }
    .user-btn {
      display: flex;
      align-items: center;
      gap: var(--space-sm);
      border: 0;
      background: transparent;
      min-height: 40px;
      padding: 0 var(--space-sm);
      border-radius: 6px;
      cursor: pointer;
      color: inherit;
      font: inherit;
    }
    .user-btn:hover {
      background: var(--p-highlight-background, #eaf1f9);
    }
    .empty {
      padding: var(--space-sm);
    }
    @media (max-width: 767px) {
      .user-name {
        display: none;
      }
    }
  `,
  template: `
    <div class="bar">
      <button type="button" class="icon-btn" aria-label="Toggle navigation" (click)="menuToggle.emit()">
        <i class="pi pi-bars" aria-hidden="true"></i>
      </button>
      <span class="name">AdminDesk</span>

      <button type="button" class="icon-btn" aria-label="Notifications" (click)="bell.toggle($event)">
        <i class="pi pi-bell" aria-hidden="true"></i>
      </button>
      <p-popover #bell>
        <div class="empty">You are all caught up</div>
      </p-popover>

      <button
        type="button"
        class="user-btn"
        aria-haspopup="menu"
        [attr.aria-label]="'Account menu for ' + userName()"
        (click)="userMenu.toggle($event)"
      >
        <i class="pi pi-user" aria-hidden="true"></i>
        <span class="user-name">{{ userName() }}</span>
      </button>
      <p-menu #userMenu [model]="menuItems()" [popup]="true" appendTo="body" />
    </div>
  `,
})
export class TopbarComponent {
  private readonly auth = inject(AuthService);
  private readonly notifications = inject(NotificationService);

  readonly menuToggle = output<void>();

  protected readonly userName = computed(() => this.auth.user()?.name ?? '');
  protected readonly menuItems = computed<MenuItem[]>(() => {
    const user = this.auth.user();
    const role = user?.roles?.[0] ?? '';
    return [
      { label: `Signed in as ${user?.name ?? ''}, ${role}`, disabled: true },
      { separator: true },
      {
        label: 'Sign out',
        icon: 'pi pi-sign-out',
        command: () => {
          this.auth.logout();
          this.notifications.success('You have been signed out.', 'Signed out');
        },
      },
    ];
  });
}
