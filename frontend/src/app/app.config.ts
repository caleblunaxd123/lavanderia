import { registerLocaleData } from '@angular/common';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import localeEsPe from '@angular/common/locales/es-PE';
import { ApplicationConfig, LOCALE_ID, provideZoneChangeDetection } from '@angular/core';
import { provideRouter, withComponentInputBinding, UrlSerializer } from '@angular/router';

import { authInterceptor } from './core/interceptors/auth.interceptor';
import { TenantUrlSerializer } from './core/routing/tenant-url-serializer';
import { routes } from './app.routes';

registerLocaleData(localeEsPe, 'es-PE');

export const appConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(withInterceptors([authInterceptor])),
    { provide: LOCALE_ID, useValue: 'es-PE' },
    { provide: UrlSerializer, useClass: TenantUrlSerializer },
  ]
};
