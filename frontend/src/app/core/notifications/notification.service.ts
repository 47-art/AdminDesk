import { Injectable, inject } from '@angular/core';
import { MessageService } from 'primeng/api';

@Injectable({ providedIn: 'root' })
export class NotificationService {
  private readonly messages = inject(MessageService);

  success(detail: string, summary = 'Done'): void {
    this.messages.add({ severity: 'success', summary, detail, life: 4000 });
  }

  info(detail: string, summary = 'Note'): void {
    this.messages.add({ severity: 'info', summary, detail, life: 4000 });
  }

  warn(detail: string, summary = 'Check this'): void {
    this.messages.add({ severity: 'warn', summary, detail, life: 6000 });
  }

  /** Error toasts stay until dismissed. */
  error(detail: string, summary = 'Something went wrong'): void {
    this.messages.add({ severity: 'error', summary, detail, sticky: true });
  }
}
