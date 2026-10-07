import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, map, throwError } from 'rxjs';
import { ApiError } from './api-error';
import { ApiResponse } from './models';

export type QueryValue = string | number | boolean | null | undefined | readonly (string | number | boolean)[];
export type QueryParams = Record<string, QueryValue>;

/** The single entry point for server calls: unwraps the envelope or throws ApiError. */
@Injectable({ providedIn: 'root' })
export class ApiClient {
  private readonly http = inject(HttpClient);

  get<T>(url: string, query?: QueryParams): Observable<T> {
    return this.http
      .get<ApiResponse<T>>(url, { params: this.toParams(query), observe: 'response' })
      .pipe(map((r) => this.unwrap<T>(r.body, r.status)), catchError((e) => this.fail(e)));
  }

  post<T>(url: string, body?: unknown): Observable<T> {
    return this.http
      .post<ApiResponse<T>>(url, body ?? {}, { observe: 'response' })
      .pipe(map((r) => this.unwrap<T>(r.body, r.status)), catchError((e) => this.fail(e)));
  }

  private unwrap<T>(body: ApiResponse<T> | null, status: number): T {
    if (status === 204 || body === null) {
      return undefined as T;
    }
    if (!body.success) {
      throw ApiError.fromBody(status, body.error);
    }
    return body.data as T;
  }

  private fail(e: unknown): Observable<never> {
    if (e instanceof ApiError) return throwError(() => e);
    if (e instanceof HttpErrorResponse) return throwError(() => ApiError.fromHttp(e));
    return throwError(() => e);
  }

  private toParams(query?: QueryParams): HttpParams {
    let params = new HttpParams();
    if (!query) return params;
    for (const [key, value] of Object.entries(query)) {
      if (value === null || value === undefined || value === '') continue;
      if (Array.isArray(value)) {
        for (const v of value) params = params.append(key, String(v));
      } else {
        params = params.append(key, String(value));
      }
    }
    return params;
  }
}
