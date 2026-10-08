import { Injectable, inject } from '@angular/core';
import { Observable, tap } from 'rxjs';
import { ApiClient } from './api-client';
import { DocumentDto } from './models';

/** Mirrors the server list; the server stays the authority. */
export const ALLOWED_EXTENSIONS: readonly string[] = [
  'pdf',
  'png',
  'jpg',
  'jpeg',
  'webp',
  'doc',
  'docx',
  'xls',
  'xlsx',
  'ppt',
  'pptx',
];
export const MAX_DOCUMENT_BYTES = 5 * 1024 * 1024;
export const ACCEPT_ATTRIBUTE = ALLOWED_EXTENSIONS.map((e) => `.${e}`).join(',');

/** A message when the file would be refused, otherwise null. */
export function checkDocumentFile(file: File): string | null {
  const dot = file.name.lastIndexOf('.');
  const ext = dot >= 0 ? file.name.slice(dot + 1).toLowerCase() : '';
  if (!ALLOWED_EXTENSIONS.includes(ext)) {
    return `${file.name}: this file type is not allowed. Use ${ALLOWED_EXTENSIONS.join(', ')}.`;
  }
  if (file.size > MAX_DOCUMENT_BYTES) {
    return `${file.name}: the file is larger than 5 MB.`;
  }
  if (file.size === 0) {
    return `${file.name}: the file is empty.`;
  }
  return null;
}

@Injectable({ providedIn: 'root' })
export class DocumentsApi {
  private readonly api = inject(ApiClient);

  list(requestId: number): Observable<DocumentDto[]> {
    return this.api.get<DocumentDto[]>(`/api/requests/${requestId}/documents`);
  }

  upload(requestId: number, file: File, stepKey?: string | null): Observable<DocumentDto> {
    const form = new FormData();
    form.append('file', file, file.name);
    if (stepKey) form.append('stepKey', stepKey);
    return this.api.postForm<DocumentDto>(`/api/requests/${requestId}/documents`, form);
  }

  /** Fetches with the bearer token, then hands the file to the browser under its original name. */
  download(requestId: number, doc: DocumentDto): Observable<Blob> {
    return this.api.getBlob(`/api/requests/${requestId}/documents/${doc.id}/download`).pipe(
      tap((blob) => {
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = doc.originalName;
        link.rel = 'noopener';
        document.body.appendChild(link);
        link.click();
        link.remove();
        setTimeout(() => URL.revokeObjectURL(url), 10000);
      }),
    );
  }

  remove(requestId: number, docId: number): Observable<void> {
    return this.api.delete<void>(`/api/requests/${requestId}/documents/${docId}`);
  }
}
