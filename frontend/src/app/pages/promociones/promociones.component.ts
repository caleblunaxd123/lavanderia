import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Servicio } from '../../core/models/models';
import { CatalogosService } from '../../core/services/catalogos.service';
import { Promocion, PromocionesService } from '../../core/services/promociones.service';
import { ToastService } from '../../core/services/toast.service';
import { EmptyStateComponent } from '../../shared/empty-state/empty-state.component';
import { PaginacionComponent } from '../../shared/paginacion/paginacion.component';
import { IconComponent } from '../../shared/icon/icon.component';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';

type FormPromocion = Partial<Promocion> & { servicioId: number | null };

@Component({
  selector: 'app-promociones',
  imports: [CommonModule, FormsModule, EmptyStateComponent, PaginacionComponent, IconComponent, PageHeaderComponent],
  templateUrl: './promociones.component.html',
  styleUrl: './promociones.component.scss'
})
export class PromocionesComponent implements OnInit {
  private readonly svc = inject(PromocionesService);
  private readonly catalogos = inject(CatalogosService);
  private readonly toast = inject(ToastService);

  readonly promociones = signal<Promocion[]>([]);
  readonly servicios = signal<Servicio[]>([]);
  readonly cargando = signal(false);
  readonly error = signal<string | null>(null);

  readonly pagina = signal(1);
  readonly tamanoPagina = signal(15);
  readonly promocionesPaginadas = computed(() => {
    const inicio = (this.pagina() - 1) * this.tamanoPagina();
    return this.promociones().slice(inicio, inicio + this.tamanoPagina());
  });
  cambiarPagina(p: number) { this.pagina.set(p); }
  cambiarTamanoPagina(t: number) { this.tamanoPagina.set(t); this.pagina.set(1); }

  readonly modalAbierto = signal(false);
  readonly editando = signal<Promocion | null>(null);
  readonly confirmarEliminar = signal<Promocion | null>(null);
  readonly confirmarDesactivar = signal<Promocion | null>(null);
  form: FormPromocion = this.formVacio();
  errorForm = signal<string | null>(null);
  guardando = signal(false);

  readonly tipos = [
    { valor: 'VOLUMEN', etiqueta: 'Por volumen (cantidad mínima)' },
    { valor: 'FRECUENCIA', etiqueta: 'Cliente frecuente' },
    { valor: 'FIJA', etiqueta: 'Fecha fija / temporada' },
  ];

  ngOnInit() {
    this.cargar();
    this.catalogos.servicios().subscribe(s => this.servicios.set(s));
  }

  cargar() {
    this.cargando.set(true);
    this.error.set(null);
    this.pagina.set(1);
    this.svc.listar().subscribe({
      next: list => { this.promociones.set(list); this.cargando.set(false); },
      error: (err: HttpErrorResponse) => {
        this.cargando.set(false);
        this.error.set(err.status === 0
          ? 'No se pudo conectar con el servidor.'
          : (err.error?.mensaje ?? 'Error al cargar promociones.'));
      }
    });
  }

  abrirCrear() {
    this.editando.set(null);
    this.form = this.formVacio();
    this.errorForm.set(null);
    this.modalAbierto.set(true);
  }

  abrirEditar(p: Promocion) {
    this.editando.set(p);
    this.form = { ...p };
    this.errorForm.set(null);
    this.modalAbierto.set(true);
  }

  cerrar() { this.modalAbierto.set(false); }

  guardar() {
    if (!this.form.descripcion?.trim()) {
      this.errorForm.set('La descripción es obligatoria.');
      return;
    }
    if (!this.form.descuentoPct && !this.form.descuentoMonto) {
      this.errorForm.set('Debes indicar un descuento en % o en soles.');
      return;
    }
    if (this.form.descuentoPct && this.form.descuentoMonto) {
      this.errorForm.set('Usa solo un tipo de descuento: porcentaje o monto fijo.');
      return;
    }
    if (this.form.fechaInicio && this.form.fechaFin && this.form.fechaFin < this.form.fechaInicio) {
      this.errorForm.set('La fecha final no puede ser anterior a la fecha inicial.');
      return;
    }
    this.guardando.set(true);
    this.errorForm.set(null);

    const edit = this.editando();
    const payload: Partial<Promocion> = {
      tipo: this.form.tipo || 'VOLUMEN',
      descripcion: this.form.descripcion!.trim(),
      descuentoPct: this.form.descuentoPct || null,
      descuentoMonto: this.form.descuentoMonto || null,
      servicioId: this.form.servicioId || null,
      cantidadMinima: this.form.cantidadMinima || 1,
      fechaInicio: this.form.fechaInicio || null,
      fechaFin: this.form.fechaFin || null,
      activa: this.form.activa ?? true,
      codigo: this.form.codigo?.trim().toUpperCase() || null,
    };

    const obs$: import('rxjs').Observable<any> = edit
      ? this.svc.actualizar(edit.id, payload)
      : this.svc.crear(payload);

    obs$.subscribe({
      next: () => {
        this.guardando.set(false);
        this.modalAbierto.set(false);
        this.toast.exito(edit ? 'Promoción actualizada' : 'Promoción creada');
        this.cargar();
      },
      error: (err: HttpErrorResponse) => {
        this.guardando.set(false);
        const msg = err.error?.mensaje ?? (err.error?.errors ? 'Revisa los datos ingresados.' : 'No se pudo guardar la promoción.');
        this.errorForm.set(msg);
        this.toast.desdeHttp(err, msg);
      }
    });
  }

  pedirEliminar(p: Promocion) { this.confirmarEliminar.set(p); }

  eliminar() {
    const p = this.confirmarEliminar();
    if (!p) return;
    this.guardando.set(true);
    this.svc.eliminar(p.id).subscribe({
      next: () => {
        this.guardando.set(false);
        this.confirmarEliminar.set(null);
        this.toast.exito('Promoción eliminada');
        this.cargar();
      },
      error: (err: HttpErrorResponse) => {
        this.guardando.set(false);
        this.toast.desdeHttp(err, 'No se pudo eliminar.');
      }
    });
  }

  toggleActiva(p: Promocion) {
    if (p.activa) { this.confirmarDesactivar.set(p); return; }
    this.aplicarCambioEstado(p, true);
  }

  confirmarDesactivarOk() {
    const p = this.confirmarDesactivar();
    if (!p) return;
    this.aplicarCambioEstado(p, false);
    this.confirmarDesactivar.set(null);
  }

  private aplicarCambioEstado(p: Promocion, activa: boolean) {
    this.svc.cambiarEstado(p.id, activa).subscribe({
      next: () => {
        this.toast.info(activa ? 'Promoción activada' : 'Promoción desactivada');
        this.cargar();
      },
      error: () => this.toast.error('No se pudo cambiar el estado.')
    });
  }

  servicioNombre(id: number | null | undefined): string {
    if (!id) return 'Todos los servicios';
    return this.servicios().find(s => s.id === id)?.nombre ?? '—';
  }

  descuentoTexto(p: Promocion): string {
    if (p.descuentoPct) return `${p.descuentoPct}%`;
    if (p.descuentoMonto) return `S/ ${p.descuentoMonto.toFixed(2)}`;
    return '—';
  }

  vigenciaTexto(p: Promocion): string {
    if (!p.fechaInicio && !p.fechaFin) return 'Sin fecha límite';
    const ini = p.fechaInicio ? new Date(p.fechaInicio).toLocaleDateString('es-PE', { day: '2-digit', month: '2-digit' }) : '…';
    const fin = p.fechaFin ? new Date(p.fechaFin).toLocaleDateString('es-PE', { day: '2-digit', month: '2-digit' }) : '…';
    return `${ini} - ${fin}`;
  }

  private formVacio(): FormPromocion {
    return {
      tipo: 'VOLUMEN', descripcion: '', descuentoPct: null, descuentoMonto: null,
      servicioId: null, cantidadMinima: 1, fechaInicio: null, fechaFin: null, activa: true, codigo: null
    };
  }
}
