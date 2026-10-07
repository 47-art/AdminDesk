import { HttpErrorResponse } from '@angular/common/http';
import { ApiErrorBody, FieldError } from './models';

export const NETWORK_ERROR_MESSAGE = 'We could not reach the server. Check your connection and try again.';

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    public readonly code: string,
    message: string,
    public readonly fieldErrors: FieldError[] = [],
    public readonly correlationId: string | null = null,
  ) {
    super(message);
    this.name = 'ApiError';
  }

  static fromBody(status: number, body: ApiErrorBody | null | undefined, headerCorrelationId?: string | null): ApiError {
    return new ApiError(
      status,
      body?.code ?? 'UNKNOWN',
      body?.message ?? 'Something went wrong.',
      body?.fieldErrors ?? [],
      body?.correlationId ?? headerCorrelationId ?? null,
    );
  }

  static fromHttp(err: HttpErrorResponse): ApiError {
    if (err.status === 0) {
      return new ApiError(0, 'NETWORK', NETWORK_ERROR_MESSAGE);
    }
    const body = (err.error && typeof err.error === 'object' ? err.error?.error : null) as ApiErrorBody | null;
    return ApiError.fromBody(err.status, body, err.headers?.get('X-Correlation-ID'));
  }

  /** Message for a given field, when the server reported one. */
  fieldMessage(field: string): string | null {
    return this.fieldErrors.find((f) => f.field.toLowerCase() === field.toLowerCase())?.message ?? null;
  }
}

export function userMessage(err: unknown): string {
  if (err instanceof ApiError) {
    if (err.status === 0) return NETWORK_ERROR_MESSAGE;
    if (err.status >= 500) {
      const ref = err.correlationId ? ` quote reference ${err.correlationId}.` : ' contact support.';
      return `Something went wrong on our side. Try again, and if it keeps happening,${ref}`;
    }
    return err.message;
  }
  return 'Something went wrong. Try again.';
}
