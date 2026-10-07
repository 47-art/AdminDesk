import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { providePrimeNG } from 'primeng/config';

import { routes } from './app.routes';
import { AdminDeskPreset } from './core/theme/admindesk-preset';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideHttpClient(),
    providePrimeNG({
      theme: { preset: AdminDeskPreset, options: { darkModeSelector: false, cssLayer: false } },
      translation: { firstDayOfWeek: 1, dateFormat: 'dd/mm/yy' },
    }),
  ],
};
