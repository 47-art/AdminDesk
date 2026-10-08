/** Error codes the API puts in the response envelope; the same names as the server's ErrorCodes. */
export const ERROR_CODES = {
  InvalidCredentials: 'INVALID_CREDENTIALS',
  AccountLocked: 'ACCOUNT_LOCKED',
  DocumentTooLarge: 'DOCUMENT_TOO_LARGE',
  DocumentTypeNotAllowed: 'DOCUMENT_TYPE_NOT_ALLOWED',
  DocumentRequired: 'DOCUMENT_REQUIRED',
} as const;
