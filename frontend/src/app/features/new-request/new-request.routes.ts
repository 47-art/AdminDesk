import { Routes } from '@angular/router';

import { unsavedChangesGuard } from '../../core/forms/unsaved-changes.guard';

export const NEW_REQUEST_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () => import('./catalogue.page').then((m) => m.CataloguePage),
  },
  {
    path: ':code',
    loadComponent: () => import('./request-form.page').then((m) => m.RequestFormPage),
    canDeactivate: [unsavedChangesGuard],
  },
];
