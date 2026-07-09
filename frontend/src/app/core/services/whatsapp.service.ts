import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { environment } from '../../../environments/environment';

export interface PlantillaWhatsappActiva {
  evento: string;
  mensaje: string;
}

@Injectable({ providedIn: 'root' })
export class WhatsappService {
  private readonly http = inject(HttpClient);
  private readonly plantillas = signal<PlantillaWhatsappActiva[]>([]);
  private cargado = false;

  cargar() {
    if (this.cargado) return;
    this.cargado = true;
    this.http.get<PlantillaWhatsappActiva[]>(`${environment.apiUrl}/plantillas-whatsapp`)
      .subscribe({ next: list => this.plantillas.set(list), error: () => {} });
  }

  mensaje(evento: string, vars: Record<string, string>, fallback: string): string {
    const plantilla = this.plantillas().find(p => p.evento === evento);
    let texto = plantilla?.mensaje ?? fallback;
    for (const [clave, valor] of Object.entries(vars)) {
      texto = texto.split(`{${clave}}`).join(valor);
    }
    return texto;
  }

  enviar(celular: string, mensaje: string) {
    const numero = celular.replace(/\D/g, '');
    window.open(`https://wa.me/51${numero}?text=${encodeURIComponent(mensaje)}`, '_blank');
  }
}
