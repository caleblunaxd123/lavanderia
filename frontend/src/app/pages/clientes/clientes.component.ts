import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { debounceTime, distinctUntilChanged, Subject } from 'rxjs';
import { Cliente, Pedido } from '../../core/models/models';
import { ClienteFrecuente, ClientesService, MovimientoPuntos } from '../../core/services/clientes.service';
import { PedidosService } from '../../core/services/pedidos.service';
import { ToastService } from '../../core/services/toast.service';
import { EmptyStateComponent } from '../../shared/empty-state/empty-state.component';
import { PaginacionComponent } from '../../shared/paginacion/paginacion.component';
import { IconComponent } from '../../shared/icon/icon.component';

@Component({
  selector: 'app-clientes',
  imports: [CommonModule, FormsModule, RouterLink, EmptyStateComponent, PaginacionComponent, IconComponent],
  templateUrl: './clientes.component.html',
  styleUrl: './clientes.component.scss'
})
export class ClientesComponent implements OnInit {
  private readonly service = inject(ClientesService);
  private readonly pedidosSvc = inject(PedidosService);
  private readonly toast = inject(ToastService);
  private readonly buscar$ = new Subject<void>();

  readonly clientes = signal<Cliente[]>([]);
  readonly cargando = signal(false);
  readonly error = signal<string | null>(null);
  readonly modalAbierto = signal(false);

  // ---------- Paginación (client-side sobre el resultado de búsqueda) ----------
  readonly pagina = signal(1);
  readonly tamanoPagina = signal(15);
  readonly clientesPaginados = computed(() => {
    const inicio = (this.pagina() - 1) * this.tamanoPagina();
    return this.clientes().slice(inicio, inicio + this.tamanoPagina());
  });

  cambiarPagina(p: number) { this.pagina.set(p); }
  cambiarTamanoPagina(t: number) { this.tamanoPagina.set(t); this.pagina.set(1); }

  campoBusqueda: 'Nombre' | 'Celular' | 'DNI' = 'Nombre';
  textoBusqueda = '';

  nuevoCliente: Partial<Cliente> = { nombre: '', celular: '', dni: '', direccion: '' };
  editando = signal<Cliente | null>(null);
  confirmarEliminar = signal<Cliente | null>(null);
  guardando = signal(false);
  errorNuevo = signal<string | null>(null);

  // ---------- Pestañas ----------
  readonly tab = signal<'buscar' | 'frecuentes' | 'unir'>('buscar');

  // ---------- Unir duplicados ----------
  readonly todosClientes = signal<Cliente[]>([]);
  readonly cargandoUnir = signal(false);
  readonly fusionando = signal(false);
  origenId: number | '' = '';
  destinoId: number | '' = '';

  cargarTodosClientes() {
    this.cargandoUnir.set(true);
    this.service.buscar(undefined, undefined, 500).subscribe({
      next: list => { this.todosClientes.set(list); this.cargandoUnir.set(false); },
      error: () => this.cargandoUnir.set(false)
    });
  }

  get puedeFusionar(): boolean {
    return !!this.origenId && !!this.destinoId && this.origenId !== this.destinoId && !this.fusionando();
  }

  fusionarClientes() {
    if (!this.puedeFusionar) return;
    this.fusionando.set(true);
    this.service.fusionar(this.origenId as number, this.destinoId as number).subscribe({
      next: res => {
        this.fusionando.set(false);
        this.toast.exito(res.mensaje);
        this.origenId = '';
        this.destinoId = '';
        this.cargarTodosClientes();
        this.recargar();
      },
      error: (err: HttpErrorResponse) => {
        this.fusionando.set(false);
        this.toast.desdeHttp(err, 'No se pudo fusionar.');
      }
    });
  }

  // ---------- Frecuentes ----------
  readonly frecuentes = signal<ClienteFrecuente[]>([]);
  readonly cargandoFrecuentes = signal(false);
  readonly errorFrecuentes = signal<string | null>(null);
  desdeFrecuentes = this.formatoFecha(new Date(Date.now() - 30 * 24 * 60 * 60 * 1000));
  hastaFrecuentes = this.formatoFecha(new Date());

  private formatoFecha(d: Date): string {
    const pad = (n: number) => String(n).padStart(2, '0');
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
  }

  cambiarTab(t: 'buscar' | 'frecuentes' | 'unir') {
    this.tab.set(t);
    if (t === 'frecuentes' && this.frecuentes().length === 0) this.cargarFrecuentes();
    if (t === 'unir' && this.todosClientes().length === 0) this.cargarTodosClientes();
  }

  cargarFrecuentes() {
    this.cargandoFrecuentes.set(true);
    this.errorFrecuentes.set(null);
    this.service.frecuentes(this.desdeFrecuentes, this.hastaFrecuentes).subscribe({
      next: list => { this.frecuentes.set(list); this.cargandoFrecuentes.set(false); },
      error: (err: HttpErrorResponse) => {
        this.cargandoFrecuentes.set(false);
        this.errorFrecuentes.set(err.status === 0
          ? 'No se pudo conectar con el servidor.'
          : (err.error?.mensaje ?? 'Error al cargar el informe.'));
      }
    });
  }

  maxVisitas(): number {
    return Math.max(1, ...this.frecuentes().map(f => f.visitas));
  }

  exportarFrecuentesCsv() {
    const list = this.frecuentes();
    if (list.length === 0) {
      this.toast.advertencia('No hay datos para exportar.');
      return;
    }
    const filas = [
      'Nombre,Celular,Visitas',
      ...list.map(f => `"${f.nombre.replace(/"/g, '""')}","${f.celular ?? ''}",${f.visitas}`)
    ];
    const blob = new Blob(['﻿' + filas.join('\n')], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `clientes-frecuentes-${this.desdeFrecuentes}.csv`;
    a.click();
    URL.revokeObjectURL(url);
  }

  ngOnInit() {
    this.buscar$
      .pipe(debounceTime(300), distinctUntilChanged())
      .subscribe(() => this.recargar());
    this.recargar();
  }

  onBuscarChange() {
    this.pagina.set(1);
    this.buscar$.next();
  }

  recargar() {
    this.cargando.set(true);
    this.error.set(null);
    const campo = this.campoBusqueda.toLowerCase();
    this.service.buscar(this.textoBusqueda || undefined, campo, 500).subscribe({
      next: list => { this.clientes.set(list); this.cargando.set(false); },
      error: (err: HttpErrorResponse) => {
        this.cargando.set(false);
        this.error.set(err.status === 0
          ? 'No se pudo conectar con el servidor. Verifica que el backend esté corriendo.'
          : (err.error?.mensaje ?? 'Error al cargar clientes.'));
      }
    });
  }

  abrirModal() {
    this.nuevoCliente = { nombre: '', celular: '', dni: '', direccion: '', puntos: 0 };
    this.errorNuevo.set(null);
    this.modalAbierto.set(true);
  }

  cerrarModal() { this.modalAbierto.set(false); }

  guardar() {
    if (!this.nuevoCliente.nombre?.trim()) {
      this.errorNuevo.set('El nombre es obligatorio.');
      return;
    }
    this.guardando.set(true);
    this.errorNuevo.set(null);

    const edit = this.editando();
    const obs$: import('rxjs').Observable<any> = edit
      ? this.service.actualizar(edit.id, this.nuevoCliente)
      : this.service.crear(this.nuevoCliente);

    obs$.subscribe({
      next: (res: any) => {
        this.guardando.set(false);
        this.modalAbierto.set(false);
        this.editando.set(null);
        this.toast.exito(edit
          ? `Cliente "${this.nuevoCliente.nombre}" actualizado`
          : `Cliente "${res?.nombre ?? this.nuevoCliente.nombre}" registrado`);
        this.recargar();
      },
      error: (err: HttpErrorResponse) => {
        this.guardando.set(false);
        const msg = err.error?.mensaje ?? 'No se pudo guardar el cliente.';
        this.errorNuevo.set(msg);
        this.toast.desdeHttp(err, msg);
      }
    });
  }

  editar(c: Cliente) {
    this.editando.set(c);
    this.nuevoCliente = { ...c };
    this.errorNuevo.set(null);
    this.modalAbierto.set(true);
  }

  pedirEliminar(c: Cliente) {
    this.confirmarEliminar.set(c);
  }

  eliminar() {
    const c = this.confirmarEliminar();
    if (!c) return;
    this.guardando.set(true);
    this.service.desactivar(c.id).subscribe({
      next: res => {
        this.guardando.set(false);
        this.confirmarEliminar.set(null);
        this.toast.exito(res.mensaje);
        this.recargar();
      },
      error: (err: HttpErrorResponse) => {
        this.guardando.set(false);
        this.toast.desdeHttp(err, 'No se pudo eliminar.');
      }
    });
  }

  // ---------- Detalle 360° del cliente (Puntos + Órdenes) ----------
  readonly clienteDetalle = signal<Cliente | null>(null);
  readonly subTabDetalle = signal<'puntos' | 'ordenes'>('puntos');

  readonly movimientosPuntos = signal<MovimientoPuntos[]>([]);
  readonly cargandoPuntos = signal(false);
  readonly modalPuntoAbierto = signal(false);
  nuevoPuntoMotivo = '';
  nuevoPuntoCantidad = 0;
  nuevoPuntoTipo: 'SUMA' | 'RESTA' = 'SUMA';
  readonly guardandoPunto = signal(false);

  readonly ordenesCliente = signal<Pedido[]>([]);
  readonly cargandoOrdenes = signal(false);
  readonly ordenesFiltro = signal<'en-proceso' | 'con-deuda' | 'entregados' | 'todos'>('en-proceso');
  readonly ordenesPagina = signal(1);
  readonly ordenesTotal = signal(0);
  readonly ordenesTamanoPagina = 10;

  abrirDetalle(c: Cliente) {
    this.clienteDetalle.set(c);
    this.subTabDetalle.set('puntos');
    this.cargarPuntos();
  }

  cerrarDetalle() {
    this.clienteDetalle.set(null);
  }

  cambiarSubTabDetalle(t: 'puntos' | 'ordenes') {
    this.subTabDetalle.set(t);
    if (t === 'puntos' && this.movimientosPuntos().length === 0) this.cargarPuntos();
    if (t === 'ordenes' && this.ordenesCliente().length === 0) this.cargarOrdenes();
  }

  cargarPuntos() {
    const c = this.clienteDetalle();
    if (!c) return;
    this.cargandoPuntos.set(true);
    this.service.listarPuntos(c.id).subscribe({
      next: list => { this.movimientosPuntos.set(list); this.cargandoPuntos.set(false); },
      error: () => this.cargandoPuntos.set(false)
    });
  }

  abrirModalPunto() {
    this.nuevoPuntoMotivo = '';
    this.nuevoPuntoCantidad = 0;
    this.nuevoPuntoTipo = 'SUMA';
    this.modalPuntoAbierto.set(true);
  }

  confirmarAgregarPunto() {
    const c = this.clienteDetalle();
    if (!c || !this.nuevoPuntoMotivo.trim() || this.nuevoPuntoCantidad <= 0) return;
    this.guardandoPunto.set(true);
    this.service.agregarPuntos(c.id, this.nuevoPuntoMotivo.trim(), this.nuevoPuntoCantidad, this.nuevoPuntoTipo).subscribe({
      next: () => {
        this.guardandoPunto.set(false);
        this.modalPuntoAbierto.set(false);
        this.toast.exito('Registro de puntos agregado');
        this.cargarPuntos();
        this.recargar();
        this.service.obtener(c.id).subscribe(actualizado => this.clienteDetalle.set(actualizado));
      },
      error: (err: HttpErrorResponse) => {
        this.guardandoPunto.set(false);
        this.toast.desdeHttp(err, 'No se pudo agregar el registro.');
      }
    });
  }

  cambiarOrdenesFiltro(f: 'en-proceso' | 'con-deuda' | 'entregados' | 'todos') {
    this.ordenesFiltro.set(f);
    this.ordenesPagina.set(1);
    this.cargarOrdenes();
  }

  cambiarOrdenesPagina(p: number) {
    this.ordenesPagina.set(p);
    this.cargarOrdenes();
  }

  cargarOrdenes() {
    const c = this.clienteDetalle();
    if (!c) return;
    this.cargandoOrdenes.set(true);
    this.pedidosSvc.listarPorCliente(c.id, this.ordenesFiltro(), this.ordenesPagina(), this.ordenesTamanoPagina).subscribe({
      next: res => {
        this.ordenesCliente.set(res.items);
        this.ordenesTotal.set(res.total);
        this.cargandoOrdenes.set(false);
      },
      error: () => this.cargandoOrdenes.set(false)
    });
  }

  saldoPendiente(p: Pedido): number {
    return Math.max(0, p.total - p.montoPagado);
  }

  etiquetaEstadoPago(p: Pedido): string {
    return p.estadoPago === 'PAGADO' ? 'Pagado' : p.estadoPago === 'PARCIAL' ? 'Parcial' : 'Pendiente';
  }
}
