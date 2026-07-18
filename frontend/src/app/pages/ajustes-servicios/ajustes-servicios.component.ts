import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ServicioEditable, ServiciosAdminService } from '../../core/services/servicios-admin.service';
import { Categoria, CategoriasService } from '../../core/services/categorias.service';
import { ToastService } from '../../core/services/toast.service';
import { EmptyStateComponent } from '../../shared/empty-state/empty-state.component';
import { PaginacionComponent } from '../../shared/paginacion/paginacion.component';
import { IconComponent } from '../../shared/icon/icon.component';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';

@Component({
  selector: 'app-ajustes-servicios',
  imports: [PageHeaderComponent, CommonModule, FormsModule, EmptyStateComponent, PaginacionComponent, IconComponent],
  templateUrl: './ajustes-servicios.component.html',
  styleUrl: './ajustes-servicios.component.scss'
})
export class AjustesServiciosComponent implements OnInit {
  private readonly svc = inject(ServiciosAdminService);
  private readonly categoriasSvc = inject(CategoriasService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  readonly servicios = signal<ServicioEditable[]>([]);
  readonly categorias = signal<Categoria[]>([]);
  readonly cargando = signal(false);
  readonly error = signal<string | null>(null);

  readonly pagina = signal(1);
  readonly tamanoPagina = signal(15);
  readonly serviciosPaginados = computed(() => {
    const inicio = (this.pagina() - 1) * this.tamanoPagina();
    return this.servicios().slice(inicio, inicio + this.tamanoPagina());
  });
  cambiarPagina(p: number) { this.pagina.set(p); }
  cambiarTamanoPagina(t: number) { this.tamanoPagina.set(t); this.pagina.set(1); }

  readonly modalAbierto = signal(false);
  readonly editando = signal<ServicioEditable | null>(null);
  readonly confirmarEliminar = signal<ServicioEditable | null>(null);
  readonly confirmarDesactivar = signal<ServicioEditable | null>(null);
  form: Partial<ServicioEditable> = this.formVacio();
  errorForm = signal<string | null>(null);
  guardando = signal(false);

  unidades = ['kg', 'prenda', 'pieza', 'und', 'servicio'];

  ngOnInit() {
    this.cargar();
    this.categoriasSvc.listar().subscribe(list => this.categorias.set(list));
  }

  cargar() {
    this.cargando.set(true);
    this.pagina.set(1);
    this.svc.listar().subscribe({
      next: list => { this.servicios.set(list); this.cargando.set(false); },
      error: (err: HttpErrorResponse) => {
        this.cargando.set(false);
        this.error.set(err.status === 0
          ? 'No se pudo conectar con el servidor.'
          : (err.error?.mensaje ?? 'Error al cargar servicios.'));
      }
    });
  }

  abrirCrear() {
    this.editando.set(null);
    this.form = this.formVacio();
    this.errorForm.set(null);
    this.modalAbierto.set(true);
  }

  abrirEditar(s: ServicioEditable) {
    this.editando.set(s);
    this.form = { ...s };
    this.errorForm.set(null);
    this.modalAbierto.set(true);
  }

  cerrar() { this.modalAbierto.set(false); }

  guardar() {
    if (!this.form.nombre?.trim() || !this.form.unidad?.trim() || (this.form.precio ?? 0) <= 0) {
      this.errorForm.set('Nombre, unidad y precio (> 0) son obligatorios.');
      return;
    }
    this.guardando.set(true);
    this.errorForm.set(null);

    const edit = this.editando();
    const obs$: import('rxjs').Observable<any> = edit
      ? this.svc.actualizar(edit.id, { ...edit, ...this.form } as ServicioEditable)
      : this.svc.crear(this.form);

    obs$.subscribe({
      next: () => {
        this.guardando.set(false);
        this.modalAbierto.set(false);
        this.toast.exito(edit ? 'Servicio actualizado' : 'Servicio creado');
        this.cargar();
      },
      error: (err: HttpErrorResponse) => {
        this.guardando.set(false);
        const msg = err.error?.mensaje ?? 'No se pudo guardar el servicio.';
        this.errorForm.set(msg);
        this.toast.desdeHttp(err, msg);
      }
    });
  }

  pedirEliminar(s: ServicioEditable) { this.confirmarEliminar.set(s); }

  eliminar() {
    const s = this.confirmarEliminar();
    if (!s) return;
    this.guardando.set(true);
    this.svc.desactivar(s.id).subscribe({
      next: res => {
        this.guardando.set(false);
        this.confirmarEliminar.set(null);
        this.toast.exito(res.mensaje);
        this.cargar();
      },
      error: (err: HttpErrorResponse) => {
        this.guardando.set(false);
        this.toast.desdeHttp(err, 'No se pudo desactivar.');
      }
    });
  }

  toggleActivo(s: ServicioEditable) {
    if (s.activo) { this.confirmarDesactivar.set(s); return; }
    this.aplicarCambioEstado(s, true);
  }

  confirmarDesactivarOk() {
    const s = this.confirmarDesactivar();
    if (!s) return;
    this.aplicarCambioEstado(s, false);
    this.confirmarDesactivar.set(null);
  }

  private aplicarCambioEstado(s: ServicioEditable, activo: boolean) {
    const actualizado = { ...s, activo };
    this.svc.actualizar(s.id, actualizado).subscribe({
      next: () => {
        this.toast.info(activo ? 'Servicio reactivado' : 'Servicio desactivado');
        this.cargar();
      },
      error: () => this.toast.error('No se pudo cambiar el estado.')
    });
  }

  volver() { this.router.navigate(['/ajustes']); }

  private formVacio(): Partial<ServicioEditable> {
    return { nombre: '', precio: 0, unidad: 'prenda', categoriaId: null, activo: true };
  }
}
