import { Routes } from '@angular/router';

import { LimitsConditionsPage } from './limits-conditions.page';
import { ModuleDefinitionsPage } from './module-definitions.page';

export const LIMITS_ROUTES: Routes = [{ path: '', component: LimitsConditionsPage }];
export const DEFINITIONS_ROUTES: Routes = [{ path: '', component: ModuleDefinitionsPage }];
