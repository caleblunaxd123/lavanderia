import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';
import { CrearPedidoRequest, Pedido, PedidoAbandonado } from '../models/models';
import { PromocionValida } from './promociones.service';

export interface PagedResult<T> {
  items: T[];
  total: number;
  pagina: number;
  tamanoPagina: number;
}

export interface PedidoHistorial {
  id: number;
  areaId: number | null;
  areaNombre: string | null;
  estadoProceso: string;
  fecha: string;
  nota: string | null;
  notificadoWsp: boolean;
}

export interface Dashboard {
  pedidosPorEstado: Record<string, number>;
  pedidosPorArea: Array<{ areaId: number; areaNombre: string; cantidad: number }>;
  ventasDelDia: number;
  totalPendientes: number;
  totalEnProceso: number;
  totalListos: number;
  pedidosDelMes: number;
  totalPendientesTab: number;
  totalOtrosTab: number;
  totalUltimosTab: number;
}

@Injectable({ providedIn: 'root' })
export class PedidosService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/pedidos`;

  listar(filtro?: string, pagina = 1, tamanoPagina = 15, busqueda?: string) {
    let params = new HttpParams().set('pagina', pagina).set('tamanoPagina', tamanoPagina);
    if (busqueda) params = params.set('busqueda', busqueda);
    else if (filtro) params = params.set('filtro', filtro);
    return this.http.get<PagedResult<Pedido>>(this.base, { params });
  }

  obtener(id: number) {
    return this.http.get<Pedido>(`${this.base}/${id}`);
  }

  listarPorCliente(clienteId: number, filtro?: string, pagina = 1, tamanoPagina = 10) {
    let params = new HttpParams().set('pagina', pagina).set('tamanoPagina', tamanoPagina);
    if (filtro) params = params.set('filtro', filtro);
    return this.http.get<PagedResult<Pedido>>(`${this.base}/por-cliente/${clienteId}`, { params });
  }

  crear(req: CrearPedidoRequest) {
    return this.http.post<Pedido>(this.base, req);
  }

  avanzar(id: number, nuevaAreaId: number | null, nuevoEstado: string, nota?: string) {
    return this.http.post<void>(`${this.base}/${id}/avanzar`, { nuevaAreaId, nuevoEstado, nota });
  }

  siguienteArea(id: number, recibidoPor?: string) {
    return this.http.post<void>(`${this.base}/${id}/siguiente-area`, { recibidoPor: recibidoPor || null });
  }

  historial(id: number) {
    return this.http.get<PedidoHistorial[]>(`${this.base}/${id}/historial`);
  }

  dashboard() {
    return this.http.get<Dashboard>(`${this.base}/dashboard`);
  }

  siguienteNumero() {
    return this.http.get<number>(`${this.base}/siguiente-numero`);
  }

  validarCodigoPromocion(codigo: string) {
    return this.http.get<PromocionValida>(`${this.base}/promocion/validar`, { params: { codigo } });
  }

  abandonados(dias = 3) {
    return this.http.get<PedidoAbandonado[]>(`${this.base}/abandonados`, { params: { dias } });
  }

  registrarPago(id: number, monto: number, metodo: string, descripcion?: string) {
    return this.http.post<void>(`${this.base}/${id}/pagos`, { monto, metodo, descripcion });
  }

  agregarItem(id: number, servicioId: number, cantidad: number, descripcion?: string) {
    return this.http.post<void>(`${this.base}/${id}/items`, { servicioId, cantidad, descripcion });
  }

  anular(id: number, motivo: string) {
    return this.http.post<void>(`${this.base}/${id}/anular`, { motivo });
  }

  cambiarFechaEntrega(id: number, fecha: string, motivo?: string) {
    return this.http.put<void>(`${this.base}/${id}/fecha-entrega`, { fecha, motivo });
  }
}
