import { Routes } from '@angular/router';

export const NEW_REQUEST_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () => import('./catalogue.page').then((m) => m.CataloguePage),
  },
];
