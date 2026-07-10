import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';

export interface ConfiguracionPagos {
  proveedor: string;
  publicKey?: string | null;
  secretKeyNueva?: string | null;
  activo: boolean;
  tieneSecretKey: boolean;
}

/** Configuración (ADMIN) de la pasarela de pagos online del negocio. */
@Injectable({ providedIn: 'root' })
export class PagosService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/pagos`;

  obtenerConfiguracion() {
    return this.http.get<ConfiguracionPagos>(`${this.base}/configuracion`);
  }

  guardarConfiguracion(c: ConfiguracionPagos) {
    return this.http.put<void>(`${this.base}/configuracion`, c);
  }
}
