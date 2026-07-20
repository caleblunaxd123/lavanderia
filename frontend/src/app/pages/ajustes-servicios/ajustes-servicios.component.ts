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

type FiltroEstadoServicio = 'todos' | 'activos' | 'inactivos';

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
  readonly busqueda = signal('');
  readonly filtroEstado = signal<FiltroEstadoServicio>('todos');
  readonly filtroCategoria = signal<number | 'todas'>('todas');

  readonly serviciosFiltrados = computed(() => {
    const texto = this.normalizar(this.busqueda());
    const estado = this.filtroEstado();
    const categoria = this.filtroCategoria();
    return [...this.servicios()]
      .filter(s =>
        (!texto || this.normalizar(`${s.nombre} ${s.unidad} ${s.categoriaNombre ?? ''}`).includes(texto)) &&
        (estado === 'todos' || (estado === 'activos' ? s.activo : !s.activo)) &&
        (categoria === 'todas' || s.categoriaId === categoria)
      )
      .sort((a, b) => Number(b.activo) - Number(a.activo) || a.nombre.localeCompare(b.nombre, 'es'));
  });
  readonly totalActivos = computed(() => this.servicios().filter(s => s.activo).length);
  readonly totalInactivos = computed(() => this.servicios().length - this.totalActivos());
  readonly precioPromedio = computed(() => {
    const activos = this.servicios().filter(s => s.activo);
    return activos.length ? activos.reduce((suma, s) => suma + s.precio, 0) / activos.length : 0;
  });

  readonly pagina = signal(1);
  readonly tamanoPagina = signal(15);
  readonly serviciosPaginados = computed(() => {
    const inicio = (this.pagina() - 1) * this.tamanoPagina();
    return this.serviciosFiltrados().slice(inicio, inicio + this.tamanoPagina());
  });
  cambiarPagina(p: number) { this.pagina.set(p); }
  cambiarTamanoPagina(t: number) { this.tamanoPagina.set(t); this.pagina.set(1); }
  actualizarBusqueda(valor: string) { this.busqueda.set(valor); this.pagina.set(1); }
  actualizarEstado(valor: FiltroEstadoServicio) { this.filtroEstado.set(valor); this.pagina.set(1); }
  actualizarCategoria(valor: number | 'todas') { this.filtroCategoria.set(valor); this.pagina.set(1); }
  limpiarFiltros() {
    this.busqueda.set('');
    this.filtroEstado.set('todos');
    this.filtroCategoria.set('todas');
    this.pagina.set(1);
  }

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
    this.error.set(null);
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

  cerrar() {
    if (this.guardando()) return;
    this.modalAbierto.set(false);
  }

  guardar() {
    if (this.guardando()) return;
    const nombre = this.form.nombre?.trim() ?? '';
    const unidad = this.form.unidad?.trim() ?? '';
    const precio = Number(this.form.precio ?? 0);

    if (nombre.length < 2 || nombre.length > 120) {
      this.errorForm.set('El nombre debe tener entre 2 y 120 caracteres.');
      return;
    }
    if (!unidad) {
      this.errorForm.set('Selecciona la unidad de cobro del servicio.');
      return;
    }
    if (!Number.isFinite(precio) || precio <= 0 || precio > 10_000) {
      this.errorForm.set('Ingresa un precio mayor a S/ 0.00 y menor o igual a S/ 10,000.00.');
      return;
    }
    const editandoId = this.editando()?.id;
    const duplicado = this.servicios().some(s =>
      s.id !== editandoId && this.normalizar(s.nombre) === this.normalizar(nombre)
    );
    if (duplicado) {
      this.errorForm.set(`Ya existe un servicio llamado “${nombre}”. Edita el existente o usa un nombre diferente.`);
      return;
    }
    this.form = { ...this.form, nombre, unidad, precio: Math.round(precio * 100) / 100 };
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
    if (this.guardando()) return;
    this.guardando.set(true);
    const actualizado = { ...s, activo };
    this.svc.actualizar(s.id, actualizado).subscribe({
      next: () => {
        this.guardando.set(false);
        this.toast.info(activo ? 'Servicio reactivado' : 'Servicio desactivado');
        this.cargar();
      },
      error: (err: HttpErrorResponse) => {
        this.guardando.set(false);
        this.toast.desdeHttp(err, 'No se pudo cambiar el estado.');
      }
    });
  }

  volver() { this.router.navigate(['/ajustes']); }

  private formVacio(): Partial<ServicioEditable> {
    return { nombre: '', precio: 0, unidad: 'prenda', categoriaId: null, activo: true };
  }

  private normalizar(valor: string): string {
    return valor.normalize('NFD').replace(/[\u0300-\u036f]/g, '').trim().toLowerCase();
  }
}
