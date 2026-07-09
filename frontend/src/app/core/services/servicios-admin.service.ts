import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';

export interface ServicioEditable {
  id: number;
  nombre: string;
  precio: number;
  unidad: string;
  categoriaId: number | null;
  categoriaNombre?: string | null;
  activo: boolean;
}

@Injectable({ providedIn: 'root' })
export class ServiciosAdminService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/servicios-admin`;

  listar() { return this.http.get<ServicioEditable[]>(this.base); }
  crear(s: Partial<ServicioEditable>) { return this.http.post<ServicioEditable>(this.base, s); }
  actualizar(id: number, s: ServicioEditable) { return this.http.put<void>(`${this.base}/${id}`, s); }
  desactivar(id: number) { return this.http.delete<{ mensaje: string }>(`${this.base}/${id}`); }
}
