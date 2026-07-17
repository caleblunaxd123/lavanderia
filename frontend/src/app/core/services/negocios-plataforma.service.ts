import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';
import {
  CambiarSuscripcionRequest, CrearNegocioRequest, EditarNegocioRequest,
  NegocioDetalle, NegocioResumen, PlataformaResumen
} from '../models/models';

@Injectable({ providedIn: 'root' })
export class NegociosPlataformaService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/negocios`;

  listar() { return this.http.get<NegocioResumen[]>(this.base); }
  resumen() { return this.http.get<PlataformaResumen>(`${this.base}/resumen`); }
  detalle(id: number) { return this.http.get<NegocioDetalle>(`${this.base}/${id}`); }
  crear(req: CrearNegocioRequest) { return this.http.post<NegocioResumen>(this.base, req); }
  editar(id: number, req: EditarNegocioRequest) { return this.http.put<void>(`${this.base}/${id}`, req); }
  cambiarSuscripcion(id: number, req: CambiarSuscripcionRequest) { return this.http.put<void>(`${this.base}/${id}/suscripcion`, req); }
  resetPasswordAdmin(id: number, nuevaPassword: string) { return this.http.post<{ usuario: string }>(`${this.base}/${id}/reset-password-admin`, { nuevaPassword }); }
  cambiarEstado(id: number, activo: boolean) { return this.http.patch<void>(`${this.base}/${id}/estado`, { activo }); }
}
