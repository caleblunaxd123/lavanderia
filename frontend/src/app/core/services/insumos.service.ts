import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';

export interface Insumo {
  id: number;
  nombre: string;
  unidadMedida: string;
  stockActual: number;
  stockMinimo: number;
  activo: boolean;
}

export interface MovimientoInsumo {
  id: number;
  insumoId: number;
  insumoNombre?: string;
  tipo: 'COMPRA' | 'CONSUMO' | 'AJUSTE';
  cantidad: number;
  costoTotal: number | null;
  fecha: string;
  usuarioNombre?: string;
  descripcion?: string | null;
}

export interface RegistrarMovimientoInsumoRequest {
  tipo: 'COMPRA' | 'CONSUMO' | 'AJUSTE';
  cantidad: number;
  costoTotal?: number | null;
  metodoPago?: string | null;
  tipoGastoId?: number | null;
  descripcion?: string | null;
}

@Injectable({ providedIn: 'root' })
export class InsumosService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/insumos`;

  listar() { return this.http.get<Insumo[]>(this.base); }
  bajoStock() { return this.http.get<Insumo[]>(`${this.base}/bajo-stock`); }
  crear(i: Partial<Insumo>) { return this.http.post<Insumo>(this.base, i); }
  actualizar(id: number, i: Partial<Insumo>) { return this.http.put<void>(`${this.base}/${id}`, i); }
  desactivar(id: number) { return this.http.delete<{ mensaje: string }>(`${this.base}/${id}`); }
  cambiarEstado(id: number, activo: boolean) { return this.http.patch<void>(`${this.base}/${id}/estado`, { activo }); }

  registrarMovimiento(insumoId: number, req: RegistrarMovimientoInsumoRequest) {
    return this.http.post<{ id: number; mensaje: string }>(`${this.base}/${insumoId}/movimientos`, req);
  }

  movimientos(insumoId?: number, desde?: string, hasta?: string) {
    let params = new HttpParams();
    if (insumoId) params = params.set('insumoId', insumoId);
    if (desde) params = params.set('desde', desde);
    if (hasta) params = params.set('hasta', hasta);
    return this.http.get<MovimientoInsumo[]>(`${this.base}/movimientos`, { params });
  }
}
