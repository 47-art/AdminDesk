import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';

import { NavigationHistoryService } from './core/navigation/navigation-history.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  template: `<router-outlet />`,
})
export class App {
  // Created with the app so the very first navigation is counted.
  private readonly history = inject(NavigationHistoryService);
}
