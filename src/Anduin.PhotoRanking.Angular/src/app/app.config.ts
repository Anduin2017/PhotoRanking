import { ApplicationConfig, inject, provideZoneChangeDetection } from '@angular/core';
import { NavigationError, provideRouter, Router, withNavigationErrorHandler } from '@angular/router';
import { DOCUMENT } from '@angular/common';
import { provideHttpClient } from '@angular/common/http';

import { routes } from './app.routes';

// A page kept open across a deployment may still reference a removed lazy chunk.
// Only recover an in-app navigation; an initial failure must not cause a reload loop.
export function recoverChunkNavigation(event: NavigationError) {
  if (inject(Router).navigated && event.error instanceof TypeError &&
      /Failed to fetch dynamically imported module|Importing a module script failed|error loading dynamically imported module/i.test(event.error.message)) {
    inject(DOCUMENT).location?.assign(event.url);
  }
}

export const appConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes, withNavigationErrorHandler(recoverChunkNavigation)),
    provideHttpClient()
  ]
};
