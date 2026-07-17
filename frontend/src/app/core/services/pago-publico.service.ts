import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';

export interface PasoSeguimiento {
  codigo: string;
  nombre: string;
  alcanzado: boolean;
  actual: boolean;
}

export interface SeguimientoPedido {
  nombreNegocio: string;
  logoUrl?: string | null;
  colorPrimario: string;
  telefonoNegocio?: string | null;
  direccionNegocio?: string | null;
  numeroPedido: number;
  modalidad: string;
  resumenEstado: string;
  fechaCompromiso?: string | null;
  etiquetaFechaCompromiso: string;
  pasos: PasoSeguimiento[];
  items: Array<{ nombre: string; cantidad: number }>;
  anulado: boolean;
  total: number;
  montoPagado: number;
  saldo: number;
  requierePago: boolean;
  publicKeyCulqi?: string | null;
  motorizadoNombre?: string | null;
  motorizadoCelular?: string | null;
  puedeReprogramar: boolean;
}

export interface ResultadoCobro {
  exito: boolean;
  mensaje?: string | null;
  saldoPendiente: number;
}

/** Consumido por la página pública de seguimiento/pago — sin sesión de empleado. */
@Injectable({ providedIn: 'root' })
export class PagoPublicoService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/pago-publico`;

  obtener(token: string) {
    return this.http.get<SeguimientoPedido>(`${this.base}/${token}`);
  }

  cobrar(token: string, culqiTokenId: string, email: string) {
    return this.http.post<ResultadoCobro>(`${this.base}/${token}/cobrar`, { culqiTokenId, email });
  }

  reprogramar(token: string, nuevaFecha: string) {
    return this.http.post<void>(`${this.base}/${token}/reprogramar`, { nuevaFecha });
  }
}
