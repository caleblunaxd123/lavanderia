import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';

export interface ReporteResult {
  columnas: string[];
  filas: Record<string, string>[];
}

export type ReporteKey =
  | 'ordenes-pendientes' | 'gastos' | 'general' | 'servicios' | 'cuadres-caja'
  | 'ordenes-mensual' | 'almacen' | 'anulados' | 'registro-entregas' | 'pagos' | 'descuento-directo';

@Injectable({ providedIn: 'root' })
export class ReportesService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/reportes`;

  obtener(key: ReporteKey, desde?: string, hasta?: string) {
    let params = new HttpParams();
    if (desde) params = params.set('desde', desde);
    if (hasta) params = params.set('hasta', hasta);
    return this.http.get<ReporteResult>(`${this.base}/${key}`, { params });
  }
}
