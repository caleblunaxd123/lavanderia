import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';
import { CrearPedidoRequest, DestinoDeliveryRequest, Pedido, PedidoAbandonado } from '../models/models';
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
  cobradoDelDia: number | null;
  saldoPorCobrar: number | null;
  cajaEsperadaHoy: number | null;
  pedidosEntregadosHoy: number;
  pedidosEntregadosTiendaHoy: number;
  pedidosEntregadosDomicilioHoy: number;
  pedidosEntregadosSemana: number;
  pedidosEntregadosMes: number;
  totalPendientes: number;
  totalEnProceso: number;
  totalListos: number;
  pedidosDelMes: number;
  metaMensual: number;
  insumosBajoStock: number | null;
  comprobantesPendientes: number | null;
  comprobantesRechazados: number | null;
  totalPedidosEstancados: number;
  totalPedidosAbandonados: number;
  slaPorArea: Array<{
    areaId: number;
    areaNombre: string;
    orden: number;
    tiempoEstMinutos: number;
    minutosPromedioReal: number;
    pedidosProcesados: number;
  }>;
  pedidosEstancados: Array<{
    pedidoId: number;
    numero: number;
    clienteNombre: string;
    areaId: number;
    areaNombre: string;
    minutosEnArea: number;
    tiempoEstMinutos: number;
  }>;
  pedidosAbandonados: Array<{
    pedidoId: number;
    numero: number;
    clienteNombre: string;
    clienteCelular?: string | null;
    total: number;
    montoPagado: number;
    fechaListo: string;
    diasEsperando: number;
  }>;
  actualizadoEn: string;
}

export interface PedidoContadores {
  pedidosDelMes: number;
  totalPendientes: number;
  totalOtros: number;
  totalUltimos: number;
}

@Injectable({ providedIn: 'root' })
export class PedidosService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/pedidos`;

  listar(
    filtro?: string,
    pagina = 1,
    tamanoPagina = 15,
    busqueda?: string,
    desde?: string,
    hasta?: string,
    campoFecha?: 'ingreso' | 'entrega'
  ) {
    let params = new HttpParams().set('pagina', pagina).set('tamanoPagina', tamanoPagina);
    if (busqueda) params = params.set('busqueda', busqueda);
    else if (filtro) params = params.set('filtro', filtro);
    if (desde) params = params.set('desde', desde);
    if (hasta) params = params.set('hasta', hasta);
    if (campoFecha) params = params.set('campoFecha', campoFecha);
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

  contadores() {
    return this.http.get<PedidoContadores>(`${this.base}/contadores`);
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

  convertirDelivery(id: number, destino: DestinoDeliveryRequest) {
    return this.http.post<void>(`${this.base}/${id}/convertir-delivery`, destino);
  }

  linkSeguimiento(id: number) {
    return this.http.get<{ token: string }>(`${this.base}/${id}/link-seguimiento`);
  }

  linkRepartidor(id: number) {
    return this.http.get<{ token: string }>(`${this.base}/${id}/link-repartidor`);
  }

  asignarMotorizado(id: number, motorizadoId: number | null) {
    return this.http.put<void>(`${this.base}/${id}/motorizado`, { motorizadoId });
  }
}
