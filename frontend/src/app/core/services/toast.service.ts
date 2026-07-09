import { Injectable, signal } from '@angular/core';

export type ToastTipo = 'exito' | 'error' | 'info' | 'advertencia';

export interface Toast {
  id: number;
  tipo: ToastTipo;
  titulo?: string;
  mensaje: string;
}

@Injectable({ providedIn: 'root' })
export class ToastService {
  readonly toasts = signal<Toast[]>([]);
  private nextId = 1;

  exito(mensaje: string, titulo = 'Listo') {
    this.mostrar('exito', mensaje, titulo);
  }

  error(mensaje: string, titulo = 'Ups') {
    this.mostrar('error', mensaje, titulo, 6000);
  }

  info(mensaje: string, titulo?: string) {
    this.mostrar('info', mensaje, titulo);
  }

  advertencia(mensaje: string, titulo = 'Atención') {
    this.mostrar('advertencia', mensaje, titulo);
  }

  cerrar(id: number) {
    this.toasts.update(list => list.filter(t => t.id !== id));
  }

  private mostrar(tipo: ToastTipo, mensaje: string, titulo?: string, duracionMs = 3800) {
    const id = this.nextId++;
    this.toasts.update(list => [...list, { id, tipo, titulo, mensaje }]);
    setTimeout(() => this.cerrar(id), duracionMs);
  }
}
