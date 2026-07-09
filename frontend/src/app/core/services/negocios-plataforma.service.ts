import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';
import { CrearNegocioRequest, NegocioResumen } from '../models/models';

@Injectable({ providedIn: 'root' })
export class NegociosPlataformaService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/negocios`;

  listar() { return this.http.get<NegocioResumen[]>(this.base); }
  crear(req: CrearNegocioRequest) { return this.http.post<NegocioResumen>(this.base, req); }
  cambiarEstado(id: number, activo: boolean) { return this.http.patch<void>(`${this.base}/${id}/estado`, { activo }); }
}
