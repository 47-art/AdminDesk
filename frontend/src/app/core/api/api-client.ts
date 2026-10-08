import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, from, map, throwError } from 'rxjs';
import { ApiError } from './api-error';
import { ApiErrorBody, ApiResponse } from './models';

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

  put<T>(url: string, body?: unknown): Observable<T> {
    return this.http
      .put<ApiResponse<T>>(url, body ?? {}, { observe: 'response' })
      .pipe(map((r) => this.unwrap<T>(r.body, r.status)), catchError((e) => this.fail(e)));
  }

  /** Multipart upload; the browser adds the content type with its boundary. */
  postForm<T>(url: string, form: FormData): Observable<T> {
    return this.http
      .post<ApiResponse<T>>(url, form, { observe: 'response' })
      .pipe(map((r) => this.unwrap<T>(r.body, r.status)), catchError((e) => this.fail(e)));
  }

  delete<T = void>(url: string): Observable<T> {
    return this.http
      .delete<ApiResponse<T>>(url, { observe: 'response' })
      .pipe(map((r) => this.unwrap<T>(r.body, r.status)), catchError((e) => this.fail(e)));
  }

  /** Fetches a file with the normal auth header; an error body is read back into an ApiError. */
  getBlob(url: string): Observable<Blob> {
    return this.http.get(url, { responseType: 'blob' }).pipe(
      catchError((e) => {
        if (e instanceof HttpErrorResponse && e.error instanceof Blob) {
          return from(e.error.text()).pipe(
            map((text): never => {
              let body: ApiErrorBody | null = null;
              try {
                body = (JSON.parse(text) as { error?: ApiErrorBody }).error ?? null;
              } catch {
                body = null;
              }
              throw ApiError.fromBody(e.status, body, e.headers?.get('X-Correlation-ID'));
            }),
          );
        }
        return this.fail(e);
      }),
    );
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
