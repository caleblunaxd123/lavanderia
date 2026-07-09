import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';
import { AreaLavado, Servicio } from '../models/models';

@Injectable({ providedIn: 'root' })
export class CatalogosService {
  private readonly http = inject(HttpClient);

  servicios() {
    return this.http.get<Servicio[]>(`${environment.apiUrl}/servicios`);
  }

  areasLavado() {
    return this.http.get<AreaLavado[]>(`${environment.apiUrl}/areas-lavado`);
  }
}
