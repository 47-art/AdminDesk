import { CanDeactivateFn } from '@angular/router';

/** A page that can ask the user before it is left. Unsaved input exists in the browser only; nothing is stored on the server. */
export interface HasUnsavedChanges {
  /** Resolves true when leaving is fine (nothing typed, already submitted, or the user chose to discard). */
  canLeave(): boolean | Promise<boolean>;
}

export const unsavedChangesGuard: CanDeactivateFn<HasUnsavedChanges> = (component) => component.canLeave();
