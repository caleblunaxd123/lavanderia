import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';
import { PagedResult } from './pedidos.service';

export interface ConfiguracionFacturacion {
  razonSocial?: string | null;
  rucEmisor?: string | null;
  ambiente: 'BETA' | 'PRODUCCION';
  solUsuario?: string | null;
  solClaveNueva?: string | null;
  certificadoPfxBase64?: string | null;
  certificadoPasswordNueva?: string | null;
  serieBoleta: string;
  serieFactura: string;
  activo: boolean;
  tieneCertificado: boolean;
  tieneCredencialesSol: boolean;
}

export interface Comprobante {
  id: number;
  pedidoId: number;
  pedidoNumero?: number | null;
  tipo: 'BOLETA' | 'FACTURA';
  serie: string;
  correlativo: number;
  numeroCompleto: string;
  clienteNombre: string;
  clienteTipoDoc: string;
  clienteNumDoc?: string | null;
  opGravada: number;
  igv: number;
  total: number;
  estado: 'PENDIENTE' | 'ACEPTADO' | 'RECHAZADO' | 'ANULADO' | 'ERROR';
  descripcionRespuestaSunat?: string | null;
  fechaEmision: string;
}

@Injectable({ providedIn: 'root' })
export class FacturacionService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/facturacion`;

  obtenerConfiguracion() {
    return this.http.get<ConfiguracionFacturacion>(`${this.base}/configuracion`);
  }

  guardarConfiguracion(c: ConfiguracionFacturacion) {
    return this.http.put<void>(`${this.base}/configuracion`, c);
  }

  emitirComprobante(pedidoId: number, tipo: 'BOLETA' | 'FACTURA') {
    return this.http.post<Comprobante>(`${environment.apiUrl}/pedidos/${pedidoId}/comprobante`, { tipo });
  }

  listarComprobantes(pagina = 1, tamanoPagina = 15) {
    const params = new HttpParams().set('pagina', pagina).set('tamanoPagina', tamanoPagina);
    return this.http.get<PagedResult<Comprobante>>(`${this.base}/comprobantes`, { params });
  }

  obtenerComprobante(id: number) {
    return this.http.get<Comprobante>(`${this.base}/comprobantes/${id}`);
  }

  /** Blob del PDF: se pide vía HttpClient (no un link directo) para que el interceptor adjunte el token. */
  descargarPdf(id: number) {
    return this.http.get(`${this.base}/comprobantes/${id}/pdf`, { responseType: 'blob' });
  }

  anular(id: number) {
    return this.http.post<{ mensaje: string }>(`${this.base}/comprobantes/${id}/anular`, {});
  }

  reenviar(id: number) {
    return this.http.post<Comprobante>(`${this.base}/comprobantes/${id}/reenviar`, {});
  }
}
