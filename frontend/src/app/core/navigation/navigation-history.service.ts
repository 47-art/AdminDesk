import { Injectable, inject } from '@angular/core';
import { NavigationEnd, Router } from '@angular/router';

/**
 * Counts the pages visited inside the app since it was loaded. A direct link, a new tab or a
 * refresh starts at one, so there is no earlier in-app page to go back to.
 */
@Injectable({ providedIn: 'root' })
export class NavigationHistoryService {
  private completed = 0;

  constructor() {
    inject(Router).events.subscribe((event) => {
      if (event instanceof NavigationEnd) this.completed++;
    });
  }

  /** True when an earlier in-app page exists. */
  canGoBack(): boolean {
    return this.completed > 1;
  }
}
