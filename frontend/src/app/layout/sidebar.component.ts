import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input, model, output } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { Drawer } from 'primeng/drawer';
import { Tooltip } from 'primeng/tooltip';

import { AuthService } from '../core/auth/auth.service';
import { BadgeCountsService } from '../core/state/badge-counts.service';
import { NAV_ITEMS } from './nav-items';

@Component({
  selector: 'app-sidebar',
  imports: [NgTemplateOutlet, RouterLink, RouterLinkActive, Drawer, Tooltip],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    .rail {
      width: var(--sidebar-width);
      background: #ffffff;
      border-right: 1px solid var(--p-content-border-color, #d8e2ee);
      height: 100%;
      box-sizing: border-box;
      display: flex;
      flex-direction: column;
      transition: width 0.15s ease;
    }
    .rail.collapsed {
      width: var(--sidebar-collapsed);
    }
    .rail-head {
      display: flex;
      align-items: center;
      justify-content: flex-end;
      height: var(--topbar-height);
      padding: 0 var(--space-sm);
    }
    .toggle {
      border: 0;
      background: transparent;
      width: 40px;
      height: 40px;
      border-radius: 6px;
      cursor: pointer;
      color: inherit;
    }
    .toggle:hover,
    .item:hover {
      background: var(--p-highlight-background, #eaf1f9);
    }
    ul {
      list-style: none;
      margin: 0;
      padding: var(--space-sm);
      display: flex;
      flex-direction: column;
      gap: var(--space-xs);
    }
    .item {
      position: relative;
      display: flex;
      align-items: center;
      gap: var(--space-md);
      min-height: 44px;
      padding: 0 var(--space-md);
      border-radius: 6px;
      color: inherit;
      text-decoration: none;
      box-sizing: border-box;
    }
    .item.active {
      background: var(--p-highlight-background, #eaf1f9);
      font-weight: 600;
    }
    .item.active::before {
      content: '';
      position: absolute;
      left: 0;
      top: 6px;
      bottom: 6px;
      width: 3px;
      border-radius: 2px;
      background: var(--p-primary-color, #2f6db8);
    }
    .label {
      flex: 1;
      white-space: nowrap;
    }
    .count {
      min-width: 20px;
      padding: 0 6px;
      border-radius: 10px;
      text-align: center;
      font-size: 12px;
      font-weight: 600;
      line-height: 20px;
      background: var(--p-primary-color, #2f6db8);
      color: var(--p-primary-contrast-color, #ffffff);
    }
    .collapsed .item {
      justify-content: center;
      padding: 0;
    }
    .collapsed .count {
      position: absolute;
      top: 2px;
      right: 4px;
      min-width: 16px;
      line-height: 16px;
      font-size: 11px;
    }
  `,
  template: `
    @if (isPhone()) {
      <p-drawer
        [visible]="drawerOpen()"
        (visibleChange)="drawerOpen.set($event)"
        position="left"
        [modal]="true"
        [dismissible]="true"
        [closeOnEscape]="true"
        [style]="{ width: '280px' }"
        header="AdminDesk"
      >
        <ng-container *ngTemplateOutlet="menu" />
      </p-drawer>
    } @else {
      <aside class="rail" [class.collapsed]="collapsed()">
        <div class="rail-head">
          <button
            type="button"
            class="toggle"
            [attr.aria-expanded]="!collapsed()"
            [attr.aria-label]="collapsed() ? 'Expand sidebar' : 'Collapse sidebar'"
            (click)="toggleCollapsed()"
          >
            <i [class]="collapsed() ? 'pi pi-angle-double-right' : 'pi pi-angle-double-left'"></i>
          </button>
        </div>
        <ng-container *ngTemplateOutlet="menu" />
      </aside>
    }

    <ng-template #menu>
      <nav aria-label="Main">
        <ul>
          @for (item of items(); track item.path) {
            <li>
              <a
                class="item"
                [routerLink]="['/', item.path]"
                routerLinkActive="active"
                [attr.aria-label]="item.label"
                [pTooltip]="collapsed() && !isPhone() ? item.label : ''"
                tooltipPosition="right"
                (click)="navigated.emit()"
              >
                <i [class]="item.icon" aria-hidden="true"></i>
                @if (!collapsed() || isPhone()) {
                  <span class="label">{{ item.label }}</span>
                }
                @if (item.showsInboxCount && inboxCount() > 0) {
                  <span class="count" [attr.aria-label]="inboxCount() + ' waiting'">{{ inboxCount() }}</span>
                }
              </a>
            </li>
          }
        </ul>
      </nav>
    </ng-template>
  `,
})
export class SidebarComponent {
  private readonly auth = inject(AuthService);
  private readonly badges = inject(BadgeCountsService);

  readonly isPhone = input(false);
  readonly drawerOpen = model(false);
  readonly collapsed = input(false);
  readonly collapsedToggle = output<void>();
  readonly navigated = output<void>();

  protected readonly inboxCount = this.badges.inboxCount;
  protected readonly items = computed(() =>
    NAV_ITEMS.filter(
      (i) =>
        this.auth.hasAnyRole(i.roles) &&
        !(i.hiddenForRoles && this.auth.hasAnyRole(i.hiddenForRoles)) &&
        !(i.requiresEmployeeProfile && !this.auth.hasEmployeeProfile()),
    ),
  );

  protected toggleCollapsed(): void {
    this.collapsedToggle.emit();
  }
}
