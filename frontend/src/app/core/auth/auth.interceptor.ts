import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { LOGIN_URL } from '../api/auth.api';
import { ApiError, NETWORK_ERROR_MESSAGE, userMessage } from '../api/api-error';
import { NotificationService } from '../notifications/notification.service';
import { AuthService } from './auth.service';

/** 403 codes a page shows inline; the generic toast is skipped for these. */
export const HANDLED_403_CODES: readonly string[] = ['NO_EMPLOYEE_PROFILE'];

const isLogin = (url: string): boolean => url.split('?')[0].toLowerCase().endsWith(LOGIN_URL);

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const notifications = inject(NotificationService);

  const loginCall = isLogin(req.url);
  const token = auth.token();
  const outgoing = token && !loginCall ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;

  return next(outgoing).pipe(
    catchError((err: unknown) => {
      if (err instanceof HttpErrorResponse) {
        if (err.status === 401 && !loginCall) {
          auth.expireSession(router.url);
        } else if (err.status === 403) {
          const code = ApiError.fromHttp(err).code;
          if (!HANDLED_403_CODES.includes(code)) {
            notifications.error('You do not have permission to do that.', 'Not allowed');
          }
        } else if (err.status === 0) {
          notifications.error(NETWORK_ERROR_MESSAGE, 'Connection problem');
        } else if (err.status >= 500) {
          notifications.error(userMessage(ApiError.fromHttp(err)));
        }
      }
      return throwError(() => err);
    }),
  );
};
