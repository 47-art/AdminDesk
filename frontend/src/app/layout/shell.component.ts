import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  NavigationCancel,
  NavigationEnd,
  NavigationError,
  NavigationStart,
  Router,
  RouterOutlet,
} from '@angular/router';
import { ProgressBar } from 'primeng/progressbar';
import { Toast } from 'primeng/toast';

import { AuthService } from '../core/auth/auth.service';
import { STORAGE_KEYS } from '../core/constants/storage-keys';
import { BadgeCountsService } from '../core/state/badge-counts.service';
import { SidebarComponent } from './sidebar.component';
import { TopbarComponent } from './topbar.component';

const PHONE_QUERY = '(max-width: 767px)';

@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, ProgressBar, Toast, SidebarComponent, TopbarComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    :host {
      display: block;
      min-height: 100vh;
    }
    .layout {
      display: flex;
      min-height: 100vh;
    }
    .side {
      position: sticky;
      top: 0;
      height: 100vh;
      flex: none;
    }
    .column {
      flex: 1;
      min-width: 0;
      display: flex;
      flex-direction: column;
    }
    .progress {
      position: fixed;
      top: 0;
      left: 0;
      right: 0;
      height: 3px;
      z-index: 1100;
    }
    main {
      flex: 1;
      width: 100%;
      max-width: 1440px;
      margin: 0 auto;
      padding: var(--space-lg);
      box-sizing: border-box;
      background: #f4f8fc;
      outline: none;
    }
    @media (max-width: 767px) {
      main {
        padding: var(--space-md);
      }
    }
  `,
  template: `
    <a class="skip-link" href="#main" (click)="focusMain($event)">Skip to main content</a>

    @if (navigating()) {
      <p-progressbar class="progress" mode="indeterminate" [style]="{ height: '3px' }" [showValue]="false" />
    }
    <p-toast [position]="phone() ? 'top-center' : 'top-right'" [life]="4000" />

    <div class="layout">
      @if (!phone()) {
        <div class="side">
          <app-sidebar [collapsed]="collapsed()" (collapsedToggle)="toggleCollapsed()" />
        </div>
      } @else {
        <app-sidebar [isPhone]="true" [(drawerOpen)]="drawerOpen" (navigated)="drawerOpen.set(false)" />
      }
      <div class="column">
        <header>
          <app-topbar (menuToggle)="onMenuToggle()" />
        </header>
        <main id="main" tabindex="-1">
          <router-outlet />
        </main>
      </div>
    </div>
  `,
})
export class ShellComponent {
  private readonly router = inject(Router);
  private readonly auth = inject(AuthService);
  private readonly badges = inject(BadgeCountsService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly navigating = signal(false);
  protected readonly phone = signal(false);
  protected readonly drawerOpen = signal(false);
  protected readonly collapsed = signal(this.readCollapsed());

  constructor() {
    this.router.events.pipe(takeUntilDestroyed()).subscribe((e) => {
      if (e instanceof NavigationStart) this.navigating.set(true);
      else if (e instanceof NavigationEnd || e instanceof NavigationCancel || e instanceof NavigationError) {
        this.navigating.set(false);
      }
    });

    const media = window.matchMedia(PHONE_QUERY);
    this.phone.set(media.matches);
    const onChange = (ev: MediaQueryListEvent): void => {
      this.phone.set(ev.matches);
      if (!ev.matches) this.drawerOpen.set(false);
    };
    media.addEventListener('change', onChange);
    this.destroyRef.onDestroy(() => media.removeEventListener('change', onChange));

    // The waiting count is loaded once after sign-in for every user.
    effect(() => {
      if (this.auth.isAuthenticated()) this.badges.refreshInbox();
    });
  }

  protected onMenuToggle(): void {
    if (this.phone()) this.drawerOpen.update((v) => !v);
    else this.toggleCollapsed();
  }

  protected toggleCollapsed(): void {
    const next = !this.collapsed();
    this.collapsed.set(next);
    try {
      localStorage.setItem(STORAGE_KEYS.sidebarCollapsed, next ? '1' : '0');
    } catch {
      // Storage may be blocked; the choice then lasts for this visit only.
    }
  }

  protected focusMain(event: Event): void {
    event.preventDefault();
    document.getElementById('main')?.focus();
  }

  private readCollapsed(): boolean {
    try {
      return localStorage.getItem(STORAGE_KEYS.sidebarCollapsed) === '1';
    } catch {
      return false;
    }
  }
}
