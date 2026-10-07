import { Injectable, inject, signal } from '@angular/core';
import { RequestsApi } from '../api/requests.api';

@Injectable({ providedIn: 'root' })
export class BadgeCountsService {
  private readonly requests = inject(RequestsApi);

  readonly inboxCount = signal(0);

  refreshInbox(): void {
    this.requests.inboxCount().subscribe({
      next: (r) => this.inboxCount.set(r.count),
      error: () => {
        // The badge is a convenience; a failed refresh leaves the last value in place.
      },
    });
  }
}
