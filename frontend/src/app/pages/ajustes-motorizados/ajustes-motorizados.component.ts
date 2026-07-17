import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Motorizado, MotorizadosService } from '../../core/services/motorizados.service';
import { ToastService } from '../../core/services/toast.service';
import { EmptyStateComponent } from '../../shared/empty-state/empty-state.component';
import { PaginacionComponent } from '../../shared/paginacion/paginacion.component';
import { IconComponent } from '../../shared/icon/icon.component';

@Component({
  selector: 'app-ajustes-motorizados',
  imports: [CommonModule, FormsModule, EmptyStateComponent, PaginacionComponent, IconComponent],
  templateUrl: './ajustes-motorizados.component.html',
  styleUrl: './ajustes-motorizados.component.scss'
})
export class AjustesMotorizadosComponent implements OnInit {
  private readonly svc = inject(MotorizadosService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  readonly motorizados = signal<Motorizado[]>([]);
  readonly cargando = signal(false);
  readonly error = signal<string | null>(null);

  readonly pagina = signal(1);
  readonly tamanoPagina = signal(15);
  readonly motorizadosPaginados = computed(() => {
    const inicio = (this.pagina() - 1) * this.tamanoPagina();
    return this.motorizados().slice(inicio, inicio + this.tamanoPagina());
  });
  cambiarPagina(p: number) { this.pagina.set(p); }
  cambiarTamanoPagina(t: number) { this.tamanoPagina.set(t); this.pagina.set(1); }

  readonly modalAbierto = signal(false);
  readonly editando = signal<Motorizado | null>(null);
  readonly confirmarDesactivar = signal<Motorizado | null>(null);
  form: Partial<Motorizado> = this.formVacio();
  errorForm = signal<string | null>(null);
  guardando = signal(false);

  ngOnInit() { this.cargar(); }

  cargar() {
    this.cargando.set(true);
    this.error.set(null);
    this.pagina.set(1);
    this.svc.listarTodos().subscribe({
      next: list => { this.motorizados.set(list); this.cargando.set(false); },
      error: (err: HttpErrorResponse) => {
        this.cargando.set(false);
        this.error.set(err.status === 0
          ? 'No se pudo conectar con el servidor.'
          : (err.error?.mensaje ?? 'Error al cargar repartidores.'));
      }
    });
  }

  abrirCrear() {
    this.editando.set(null);
    this.form = this.formVacio();
    this.errorForm.set(null);
    this.modalAbierto.set(true);
  }

  abrirEditar(m: Motorizado) {
    this.editando.set(m);
    this.form = { ...m };
    this.errorForm.set(null);
    this.modalAbierto.set(true);
  }

  cerrar() { this.modalAbierto.set(false); }

  guardar() {
    if (!this.form.nombre?.trim()) {
      this.errorForm.set('El nombre es obligatorio.');
      return;
    }
    this.guardando.set(true);
    this.errorForm.set(null);

    const edit = this.editando();
    const obs$: import('rxjs').Observable<any> = edit
      ? this.svc.actualizar(edit.id, this.form)
      : this.svc.crear(this.form);

    obs$.subscribe({
      next: () => {
        this.guardando.set(false);
        this.modalAbierto.set(false);
        this.toast.exito(edit ? 'Repartidor actualizado' : 'Repartidor creado');
        this.cargar();
      },
      error: (err: HttpErrorResponse) => {
        this.guardando.set(false);
        const msg = err.error?.mensaje ?? 'No se pudo guardar el repartidor.';
        this.errorForm.set(msg);
        this.toast.desdeHttp(err, msg);
      }
    });
  }

  toggleActivo(m: Motorizado) {
    if (m.activo) { this.confirmarDesactivar.set(m); return; }
    this.aplicarCambioEstado(m, true);
  }

  confirmarDesactivarOk() {
    const m = this.confirmarDesactivar();
    if (!m) return;
    this.aplicarCambioEstado(m, false);
    this.confirmarDesactivar.set(null);
  }

  private aplicarCambioEstado(m: Motorizado, activo: boolean) {
    this.svc.cambiarEstado(m.id, activo).subscribe({
      next: () => {
        this.toast.info(activo ? 'Repartidor reactivado' : 'Repartidor desactivado');
        this.cargar();
      },
      error: () => this.toast.error('No se pudo cambiar el estado.')
    });
  }

  volver() { this.router.navigate(['/ajustes']); }

  private formVacio(): Partial<Motorizado> {
    return { nombre: '', celular: '', activo: true };
  }
}
