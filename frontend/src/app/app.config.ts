import { ApplicationConfig, inject, provideAppInitializer, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { MessageService } from 'primeng/api';
import { providePrimeNG } from 'primeng/config';

import { AuthService } from './core/auth/auth.service';
import { authInterceptor } from './core/auth/auth.interceptor';

import { routes } from './app.routes';
import { AdminDeskPreset } from './core/theme/admindesk-preset';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideHttpClient(withInterceptors([authInterceptor])),
    MessageService,
    provideAppInitializer(() => inject(AuthService).restore()),
    providePrimeNG({
      theme: { preset: AdminDeskPreset, options: { darkModeSelector: false, cssLayer: false } },
      translation: { firstDayOfWeek: 1, dateFormat: 'dd/mm/yy' },
    }),
  ],
};
