import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { catchError, tap, throwError } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ConfiguracionNegocio } from '../models/models';

const FALLBACK: ConfiguracionNegocio = {
  id: 0,
  nombreNegocio: 'Mi Lavandería',
  logoUrl: null,
  colorPrimario: '#0b57d0',
  colorSecundario: '#29b6f6',
  colorAcento: '#f5a623',
  igv: 18,
  metaMensual: 0,
  solesPorPunto: 1,
  anchoTicketMm: 80,
  mensajePieTicket: 'Gracias por su preferencia.',
  costoDelivery: 0,
  valorPuntoCanje: 0,
  maxDescuentoPct: 0,
};

@Injectable({ providedIn: 'root' })
export class ConfiguracionService {
  private readonly http = inject(HttpClient);
  readonly configuracion = signal<ConfiguracionNegocio>(FALLBACK);

  cargar() {
    return this.http.get<ConfiguracionNegocio>(`${environment.apiUrl}/configuracion`).pipe(
      tap(c => {
        this.configuracion.set(c);
        this.aplicarTema(c);
      }),
      catchError(err => this.resetearAFallback(err))
    );
  }

  /** Marca de la empresa identificada por el slug de su URL (antes de iniciar sesión). */
  cargarPorSlug(slug: string) {
    return this.http.get<ConfiguracionNegocio>(`${environment.apiUrl}/configuracion/publico/${encodeURIComponent(slug)}`).pipe(
      tap(c => {
        this.configuracion.set(c);
        this.aplicarTema(c);
      }),
      catchError(err => this.resetearAFallback(err))
    );
  }

  /**
   * Si la carga falla (ej. una empresa que aún no guardó su configuración), se vuelve al
   * genérico neutral en vez de dejar pegada la marca de la última empresa que sí cargó bien.
   */
  private resetearAFallback(err: unknown) {
    this.configuracion.set(FALLBACK);
    this.aplicarTema(FALLBACK);
    return throwError(() => err);
  }

  actualizar(c: ConfiguracionNegocio) {
    return this.http.put<void>(`${environment.apiUrl}/configuracion`, c).pipe(
      tap(() => {
        this.configuracion.set(c);
        this.aplicarTema(c);
      })
    );
  }

  private aplicarTema(c: ConfiguracionNegocio) {
    const root = document.documentElement;
    root.style.setProperty('--azul-oscuro', c.colorPrimario);
    root.style.setProperty('--azul-claro', c.colorSecundario);
    root.style.setProperty('--naranja', c.colorAcento);
    document.title = c.nombreNegocio;
  }
}
