import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CajaService } from '../../core/services/caja.service';
import { Insumo, InsumosService, MovimientoInsumo } from '../../core/services/insumos.service';
import { ToastService } from '../../core/services/toast.service';
import { TipoGasto } from '../../core/models/models';
import { EmptyStateComponent } from '../../shared/empty-state/empty-state.component';
import { PaginacionComponent } from '../../shared/paginacion/paginacion.component';
import { IconComponent } from '../../shared/icon/icon.component';

@Component({
  selector: 'app-inventario',
  imports: [CommonModule, FormsModule, EmptyStateComponent, PaginacionComponent, IconComponent],
  templateUrl: './inventario.component.html',
  styleUrl: './inventario.component.scss'
})
export class InventarioComponent implements OnInit {
  private readonly svc = inject(InsumosService);
  private readonly cajaSvc = inject(CajaService);
  private readonly toast = inject(ToastService);

  readonly tab = signal<'insumos' | 'historial'>('insumos');

  readonly insumos = signal<Insumo[]>([]);
  readonly cargando = signal(false);
  readonly error = signal<string | null>(null);
  readonly tiposGasto = signal<TipoGasto[]>([]);

  // ---------- Paginación ----------
  readonly paginaInsumos = signal(1);
  readonly tamanoPaginaInsumos = signal(15);
  readonly insumosPaginados = computed(() => {
    const inicio = (this.paginaInsumos() - 1) * this.tamanoPaginaInsumos();
    return this.insumos().slice(inicio, inicio + this.tamanoPaginaInsumos());
  });
  cambiarPaginaInsumos(p: number) { this.paginaInsumos.set(p); }
  cambiarTamanoPaginaInsumos(t: number) { this.tamanoPaginaInsumos.set(t); this.paginaInsumos.set(1); }

  readonly paginaHistorial = signal(1);
  readonly tamanoPaginaHistorial = signal(15);
  readonly movimientosPaginados = computed(() => {
    const inicio = (this.paginaHistorial() - 1) * this.tamanoPaginaHistorial();
    return this.movimientos().slice(inicio, inicio + this.tamanoPaginaHistorial());
  });
  cambiarPaginaHistorial(p: number) { this.paginaHistorial.set(p); }
  cambiarTamanoPaginaHistorial(t: number) { this.tamanoPaginaHistorial.set(t); this.paginaHistorial.set(1); }

  // ---------- Alta/edición de insumo ----------
  readonly modalInsumo = signal(false);
  readonly editandoInsumo = signal<Insumo | null>(null);
  formInsumo: Partial<Insumo> = this.formInsumoVacio();
  errorFormInsumo = signal<string | null>(null);
  guardandoInsumo = signal(false);

  readonly confirmarEliminar = signal<Insumo | null>(null);
  readonly confirmarDesactivar = signal<Insumo | null>(null);

  // ---------- Registrar movimiento ----------
  readonly modalMovimiento = signal(false);
  insumoMovimiento: Insumo | null = null;
  movTipo: 'COMPRA' | 'CONSUMO' | 'AJUSTE' = 'COMPRA';
  movCantidad = 0;
  movCosto = 0;
  movMetodoPago: 'EFECTIVO' | 'YAPE' | 'PLIN' | 'TRANSFERENCIA' | 'POS' = 'EFECTIVO';
  movTipoGastoId: number | '' = '';
  movDescripcion = '';
  movFecha = '';  // fecha de la compra (opcional, solo COMPRA)
  guardandoMovimiento = signal(false);

  // ---------- Historial ----------
  readonly movimientos = signal<MovimientoInsumo[]>([]);
  readonly cargandoHistorial = signal(false);
  desdeHistorial = this.formatoFecha(new Date(Date.now() - 30 * 24 * 60 * 60 * 1000));
  hastaHistorial = this.formatoFecha(new Date());

  private formatoFecha(d: Date): string {
    const pad = (n: number) => String(n).padStart(2, '0');
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
  }

  ngOnInit() {
    this.cargar();
    this.cajaSvc.tiposGasto().subscribe(t => this.tiposGasto.set(t));
  }

  cambiarTab(t: 'insumos' | 'historial') {
    this.tab.set(t);
    if (t === 'historial' && this.movimientos().length === 0) this.cargarHistorial();
  }

  cargar() {
    this.cargando.set(true);
    this.error.set(null);
    this.paginaInsumos.set(1);
    this.svc.listar().subscribe({
      next: list => { this.insumos.set(list); this.cargando.set(false); },
      error: (err: HttpErrorResponse) => {
        this.cargando.set(false);
        this.error.set(err.status === 0
          ? 'No se pudo conectar con el servidor.'
          : (err.error?.mensaje ?? 'Error al cargar el inventario.'));
      }
    });
  }

  cargarHistorial() {
    this.cargandoHistorial.set(true);
    this.paginaHistorial.set(1);
    this.svc.movimientos(undefined, this.desdeHistorial, this.hastaHistorial).subscribe({
      next: list => { this.movimientos.set(list); this.cargandoHistorial.set(false); },
      error: () => this.cargandoHistorial.set(false)
    });
  }

  bajoStock(i: Insumo): boolean { return i.stockActual <= i.stockMinimo; }

  // ---------- Alta/edición ----------
  abrirCrearInsumo() {
    this.editandoInsumo.set(null);
    this.formInsumo = this.formInsumoVacio();
    this.errorFormInsumo.set(null);
    this.modalInsumo.set(true);
  }

  abrirEditarInsumo(i: Insumo) {
    this.editandoInsumo.set(i);
    this.formInsumo = { ...i };
    this.errorFormInsumo.set(null);
    this.modalInsumo.set(true);
  }

  cerrarModalInsumo() { this.modalInsumo.set(false); }

  guardarInsumo() {
    if (!this.formInsumo.nombre?.trim() || !this.formInsumo.unidadMedida?.trim()) {
      this.errorFormInsumo.set('Nombre y unidad de medida son obligatorios.');
      return;
    }
    this.guardandoInsumo.set(true);
    this.errorFormInsumo.set(null);

    const edit = this.editandoInsumo();
    const obs$: import('rxjs').Observable<any> = edit
      ? this.svc.actualizar(edit.id, this.formInsumo)
      : this.svc.crear(this.formInsumo);

    obs$.subscribe({
      next: () => {
        this.guardandoInsumo.set(false);
        this.modalInsumo.set(false);
        this.toast.exito(edit ? 'Insumo actualizado' : 'Insumo registrado');
        this.cargar();
      },
      error: (err: HttpErrorResponse) => {
        this.guardandoInsumo.set(false);
        const msg = err.error?.mensaje ?? 'No se pudo guardar el insumo.';
        this.errorFormInsumo.set(msg);
        this.toast.desdeHttp(err, msg);
      }
    });
  }

  pedirEliminar(i: Insumo) { this.confirmarEliminar.set(i); }

  eliminar() {
    const i = this.confirmarEliminar();
    if (!i) return;
    this.guardandoInsumo.set(true);
    this.svc.desactivar(i.id).subscribe({
      next: res => {
        this.guardandoInsumo.set(false);
        this.confirmarEliminar.set(null);
        this.toast.exito(res.mensaje);
        this.cargar();
      },
      error: (err: HttpErrorResponse) => {
        this.guardandoInsumo.set(false);
        this.toast.desdeHttp(err, 'No se pudo desactivar.');
      }
    });
  }

  toggleActivo(i: Insumo) {
    if (i.activo) { this.confirmarDesactivar.set(i); return; }
    this.aplicarCambioEstado(i, true);
  }

  confirmarDesactivarOk() {
    const i = this.confirmarDesactivar();
    if (!i) return;
    this.aplicarCambioEstado(i, false);
    this.confirmarDesactivar.set(null);
  }

  private aplicarCambioEstado(i: Insumo, activo: boolean) {
    this.svc.cambiarEstado(i.id, activo).subscribe({
      next: () => {
        this.toast.info(activo ? 'Insumo reactivado' : 'Insumo desactivado');
        this.cargar();
      },
      error: () => this.toast.error('No se pudo cambiar el estado.')
    });
  }

  private formInsumoVacio(): Partial<Insumo> {
    return { nombre: '', unidadMedida: '', stockActual: 0, stockMinimo: 0, activo: true };
  }

  // ---------- Registrar movimiento ----------
  abrirModalMovimiento(i: Insumo) {
    this.insumoMovimiento = i;
    this.movTipo = 'COMPRA';
    this.movCantidad = 0;
    this.movCosto = 0;
    this.movMetodoPago = 'EFECTIVO';
    this.movTipoGastoId = '';
    this.movDescripcion = '';
    this.movFecha = this.formatoFecha(new Date());  // por defecto hoy
    this.modalMovimiento.set(true);
  }

  cerrarModalMovimiento() { this.modalMovimiento.set(false); }

  get puedeRegistrarMovimiento(): boolean {
    if (this.movCantidad === 0) return false;
    if (this.movTipo !== 'AJUSTE' && this.movCantidad <= 0) return false;
    return !this.guardandoMovimiento();
  }

  confirmarMovimiento() {
    const i = this.insumoMovimiento;
    if (!i || !this.puedeRegistrarMovimiento) return;
    this.guardandoMovimiento.set(true);

    this.svc.registrarMovimiento(i.id, {
      tipo: this.movTipo,
      cantidad: this.movCantidad,
      costoTotal: this.movTipo === 'COMPRA' && this.movCosto > 0 ? this.movCosto : null,
      metodoPago: this.movTipo === 'COMPRA' && this.movCosto > 0 ? this.movMetodoPago : null,
      tipoGastoId: this.movTipoGastoId ? (this.movTipoGastoId as number) : null,
      descripcion: this.movDescripcion.trim() || null,
      fecha: this.movTipo === 'COMPRA' && this.movFecha ? this.movFecha : null
    }).subscribe({
      next: () => {
        this.guardandoMovimiento.set(false);
        this.modalMovimiento.set(false);
        this.toast.exito('Movimiento registrado');
        this.cargar();
      },
      error: (err: HttpErrorResponse) => {
        this.guardandoMovimiento.set(false);
        this.toast.desdeHttp(err, 'No se pudo registrar el movimiento.');
      }
    });
  }

  etiquetaTipo(tipo: string): string {
    return ({ COMPRA: 'Compra', CONSUMO: 'Consumo', AJUSTE: 'Ajuste' } as Record<string, string>)[tipo] ?? tipo;
  }

  claseTipo(tipo: string): string {
    return ({ COMPRA: 'badge badge--verde', CONSUMO: 'badge badge--gris', AJUSTE: 'badge badge--azul' } as Record<string, string>)[tipo] ?? 'badge badge--gris';
  }
}
