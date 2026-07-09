import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { AfterViewInit, Component, ElementRef, OnDestroy, OnInit, ViewChild, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AreaLavado, Pedido, PedidoAbandonado, Servicio } from '../../core/models/models';
import { CatalogosService } from '../../core/services/catalogos.service';
import { ConfiguracionService } from '../../core/services/configuracion.service';
import { PedidoHistorial, PedidosService } from '../../core/services/pedidos.service';
import { FacturacionService } from '../../core/services/facturacion.service';
import { ToastService } from '../../core/services/toast.service';
import { WhatsappService } from '../../core/services/whatsapp.service';
import { EmptyStateComponent } from '../../shared/empty-state/empty-state.component';
import { PaginacionComponent } from '../../shared/paginacion/paginacion.component';
import { IconComponent } from '../../shared/icon/icon.component';
import { SkeletonComponent } from '../../shared/skeleton/skeleton.component';

type Filtro = 'pendientes' | 'otros' | 'ultimos' | 'fecha';
type TipoFecha = 'ingreso' | 'entrega';

@Component({
  selector: 'app-pedidos-list',
  imports: [CommonModule, FormsModule, RouterLink, EmptyStateComponent, PaginacionComponent, IconComponent, SkeletonComponent],
  templateUrl: './pedidos-list.component.html',
  styleUrl: './pedidos-list.component.scss'
})
export class PedidosListComponent implements OnInit, OnDestroy, AfterViewInit {
  private readonly service = inject(PedidosService);
  private readonly catalogos = inject(CatalogosService);
  private readonly toast = inject(ToastService);
  private readonly whatsapp = inject(WhatsappService);
  private readonly config = inject(ConfiguracionService);
  private readonly facturacionSvc = inject(FacturacionService);
  readonly emitiendoComprobante = signal(false);

  readonly pedidos = signal<Pedido[]>([]);
  readonly areas = signal<AreaLavado[]>([]);
  readonly cargando = signal(false);
  readonly error = signal<string | null>(null);
  readonly filtro = signal<Filtro>('pendientes');
  readonly busqueda = signal('');
  private busquedaTimerId?: ReturnType<typeof setTimeout>;

  readonly pagina = signal(1);
  readonly tamanoPagina = signal(15);
  readonly total = signal(0);

  readonly abandonados = signal<PedidoAbandonado[]>([]);
  readonly mostrarAbandonados = signal(true);

  readonly pedidosDelMes = signal(0);
  readonly metaMensual = computed(() => this.config.configuracion().metaMensual);
  readonly progresoMetaPct = computed(() => {
    const meta = this.metaMensual();
    if (!meta || meta <= 0) return 0;
    return Math.min(100, Math.round((this.pedidosDelMes() / meta) * 100));
  });

  // Contadores por tab
  readonly totalPendientesTab = signal(0);
  readonly totalOtrosTab = signal(0);
  readonly totalUltimosTab = signal(0);

  // Leyenda de colores de filas
  readonly leyendaAbierta = signal(false);

  // Filtro por Fecha (modal Tipo de consulta)
  readonly modalTipoConsulta = signal(false);
  readonly tipoFechaConsulta = signal<TipoFecha>('ingreso');
  fechaFiltroDesde = '';
  fechaFiltroHasta = '';

  claseFila(p: Pedido): string {
    if (p.anulado) return 'fila--anulado';
    if (p.estadoProceso === 'ENTREGADO') return 'fila--entregado';
    return '';
  }

  // Filtros por columna (client-side sobre los pedidos ya cargados)
  readonly filtrosColumna = signal({
    numero: '', cliente: '', dni: '', items: '',
    area: '', estado: '', pago: ''
  });

  actualizarFiltroCol(campo: keyof ReturnType<typeof this.filtrosColumna>, valor: string) {
    this.filtrosColumna.update(f => ({ ...f, [campo]: valor }));
  }

  readonly pedidosFiltrados = computed(() => {
    const f = this.filtrosColumna();
    const norm = (s: string) => (s || '').toLowerCase();
    return this.pedidos().filter(p =>
      (!f.numero || String(p.numero).includes(f.numero.trim())) &&
      (!f.cliente || norm(p.clienteNombre ?? '').includes(norm(f.cliente))) &&
      (!f.dni || norm(p.clienteDni ?? '').includes(norm(f.dni))) &&
      (!f.items || p.items.some(i => norm(i.servicioNombre ?? '').includes(norm(f.items)))) &&
      (!f.area || norm(p.areaActualNombre ?? '').includes(norm(f.area))) &&
      (!f.estado || norm(p.estadoProceso).includes(norm(f.estado))) &&
      (!f.pago || norm(p.estadoPago).includes(norm(f.pago)))
    );
  });

  @ViewChild('buscadorInput') buscadorInputRef?: ElementRef<HTMLInputElement>;

  /** Al presionar Enter con un texto puramente numérico, abrir directo el pedido. */
  buscarPorNumeroYAbrir() {
    const texto = this.busqueda().trim();
    if (!texto) return;

    // Si es puramente numérico, intentamos abrir el pedido por N° exacto
    if (/^\d+$/.test(texto)) {
      // Busca en el resultado ya cargado; si no está, refresca y luego abre
      const match = this.pedidos().find(p => String(p.numero) === texto);
      if (match) {
        this.abrirDetalle(match);
        return;
      }
    }
    // fallback: si hay solo un resultado en pantalla, abrirlo
    if (this.pedidos().length === 1) {
      this.abrirDetalle(this.pedidos()[0]);
    }
  }

  readonly pedidoAbierto = signal<Pedido | null>(null);
  readonly historial = signal<PedidoHistorial[]>([]);
  readonly cargandoHistorial = signal(false);
  readonly avanzando = signal(false);

  // Modales secundarios
  readonly modalPago = signal(false);
  readonly modalItem = signal(false);
  readonly modalAnular = signal(false);
  readonly modalEntrega = signal(false);
  readonly modalFecha = signal(false);
  readonly servicios = signal<Servicio[]>([]);

  // Formularios internos
  pagoMonto = 0;
  pagoMetodo: 'EFECTIVO' | 'YAPE' | 'PLIN' | 'TRANSFERENCIA' | 'POS' = 'EFECTIVO';
  itemServicioId: number | '' = '';
  itemCantidad = 1;
  itemDescripcion = '';
  motivoAnulacion = '';
  fechaEntregaNueva = '';
  motivoCambioFecha = '';
  recibidoPor = '';
  procesando = signal(false);

  private timerId?: ReturnType<typeof setInterval>;


  readonly saldoPendiente = computed(() => {
    const p = this.pedidoAbierto();
    return p ? Math.max(0, p.total - p.montoPagado) : 0;
  });

  ngOnInit() {
    this.catalogos.areasLavado().subscribe(a => this.areas.set(a));
    this.catalogos.servicios().subscribe(s => this.servicios.set(s));
    this.whatsapp.cargar();
    this.recargar();
    this.cargarAbandonados();
    this.cargarMetaMensual();
    this.timerId = setInterval(() => { this.recargar(true); this.cargarAbandonados(); }, 30_000);
  }

  ngAfterViewInit() {
    // Autofocus en el buscador para que el operador solo teclee N° de ticket + Enter
    setTimeout(() => this.buscadorInputRef?.nativeElement.focus(), 100);
  }

  cargarMetaMensual() {
    this.service.dashboard().subscribe({
      next: d => {
        this.pedidosDelMes.set(d.pedidosDelMes);
        this.totalPendientesTab.set(d.totalPendientesTab);
        this.totalOtrosTab.set(d.totalOtrosTab);
        this.totalUltimosTab.set(d.totalUltimosTab);
      },
      error: () => {}
    });
  }

  private tiempoRestanteMinutos(p: Pedido): number {
    const lista = this.areas();
    const idx = lista.findIndex(a => a.id === p.areaActualId);
    if (idx === -1) return 0;
    return lista.slice(idx).reduce((acc, a) => acc + a.tiempoEstMinutos, 0);
  }

  cargarAbandonados() {
    this.service.abandonados(3).subscribe({
      next: list => this.abandonados.set(list),
      error: () => {}
    });
  }

  abrirDetallePorId(pedidoId: number) {
    this.service.obtener(pedidoId).subscribe(p => this.abrirDetalle(p));
  }

  avisarAbandonoWhatsapp(a: PedidoAbandonado) {
    if (!a.clienteCelular) {
      this.toast.advertencia('Este cliente no tiene celular registrado.');
      return;
    }
    const mensaje = this.whatsapp.mensaje('LISTO', {
      cliente: a.clienteNombre,
      numero: String(a.numero),
      negocio: this.config.configuracion().nombreNegocio,
    }, `Hola ${a.clienteNombre}, tu pedido #${a.numero} está listo para recoger hace ${a.diasEsperando} día${a.diasEsperando === 1 ? '' : 's'}. ¡Te esperamos!`);
    this.whatsapp.enviar(a.clienteCelular, mensaje);
  }

  ngOnDestroy() {
    if (this.timerId) clearInterval(this.timerId);
    if (this.busquedaTimerId) clearTimeout(this.busquedaTimerId);
  }

  cambiarFiltro(f: Filtro) {
    this.filtro.set(f);
    this.busqueda.set('');
    this.pagina.set(1);
    this.recargar();
  }

  onBuscarInput(texto: string) {
    this.busqueda.set(texto);
    this.pagina.set(1);
    if (this.busquedaTimerId) clearTimeout(this.busquedaTimerId);
    this.busquedaTimerId = setTimeout(() => this.recargar(), 300);
  }

  limpiarBusqueda() {
    this.busqueda.set('');
    this.pagina.set(1);
    this.recargar();
  }

  cambiarPagina(p: number) {
    this.pagina.set(p);
    this.recargar();
  }

  cambiarTamanoPagina(t: number) {
    this.tamanoPagina.set(t);
    this.recargar();
  }

  recargar(silencioso = false) {
    if (!silencioso) this.cargando.set(true);
    this.error.set(null);
    const texto = this.busqueda().trim();
    this.service.listar(texto ? undefined : this.filtro(), this.pagina(), this.tamanoPagina(), texto || undefined).subscribe({
      next: res => { this.pedidos.set(res.items); this.total.set(res.total); this.cargando.set(false); },
      error: (err: HttpErrorResponse) => {
        this.cargando.set(false);
        if (!silencioso) {
          this.error.set(err.status === 0
            ? 'No se pudo conectar con el servidor. Verifica que el backend esté corriendo.'
            : (err.error?.mensaje ?? 'Error al cargar los pedidos.'));
        }
      }
    });
  }

  abrirDetalle(p: Pedido) {
    this.pedidoAbierto.set(p);
    this.historial.set([]);
    this.cargandoHistorial.set(true);
    this.service.obtener(p.id).subscribe(completo => this.pedidoAbierto.set(completo));
    this.service.historial(p.id).subscribe({
      next: h => { this.historial.set(h); this.cargandoHistorial.set(false); },
      error: () => this.cargandoHistorial.set(false)
    });
  }

  cerrarDetalle() { this.pedidoAbierto.set(null); }

  avanzarEtapa(p: Pedido) {
    // Si el próximo paso es "Marcar entregado", SIEMPRE abrir el modal para
    // registrar quién recoge (aunque no haya saldo pendiente).
    if (p.estadoProceso === 'LISTO') {
      const saldo = p.total - p.montoPagado;
      this.pedidoAbierto.set(p);
      this.pagoMonto = saldo > 0.01 ? Math.round(saldo * 100) / 100 : 0;
      this.pagoMetodo = 'EFECTIVO';
      this.recibidoPor = ''; // vacío por default → asume titular
      this.modalEntrega.set(true);
      return;
    }
    this.ejecutarAvance(p);
  }

  private ejecutarAvance(p: Pedido) {
    this.avanzando.set(true);
    const eraLista = p.estadoProceso === 'LISTO';
    this.service.siguienteArea(p.id).subscribe({
      next: () => {
        this.avanzando.set(false);
        this.toast.exito(eraLista ? `Pedido #${p.numero} entregado` : 'Etapa actualizada');
        this.recargar(true);
        if (this.pedidoAbierto()?.id === p.id) {
          this.service.obtener(p.id).subscribe(actualizado => {
            this.pedidoAbierto.set(actualizado);
            this.service.historial(actualizado.id).subscribe(h => this.historial.set(h));
          });
        }
      },
      error: (err: HttpErrorResponse) => {
        this.avanzando.set(false);
        this.toast.error(err.error?.mensaje ?? 'No se pudo avanzar la etapa.');
      }
    });
  }

  // ---------- Facturación electrónica ----------
  emitirComprobante(tipo: 'BOLETA' | 'FACTURA') {
    const p = this.pedidoAbierto();
    if (!p || this.emitiendoComprobante()) return;
    this.emitiendoComprobante.set(true);
    this.facturacionSvc.emitirComprobante(p.id, tipo).subscribe({
      next: c => {
        this.emitiendoComprobante.set(false);
        if (c.estado === 'ACEPTADO') this.toast.exito(`${c.numeroCompleto} emitido y aceptado por SUNAT.`);
        else this.toast.error(`${c.numeroCompleto}: ${c.descripcionRespuestaSunat ?? c.estado}`);
      },
      error: (err: HttpErrorResponse) => {
        this.emitiendoComprobante.set(false);
        this.toast.error(err.error?.mensaje ?? 'No se pudo emitir el comprobante.');
      }
    });
  }

  // ---------- Pagos ----------
  abrirModalPago() {
    const p = this.pedidoAbierto();
    if (!p) return;
    this.pagoMonto = Math.max(0, p.total - p.montoPagado);
    this.pagoMetodo = 'EFECTIVO';
    this.modalPago.set(true);
  }

  confirmarPago() {
    const p = this.pedidoAbierto();
    if (!p || this.pagoMonto <= 0) return;
    this.procesando.set(true);
    this.service.registrarPago(p.id, this.pagoMonto, this.pagoMetodo).subscribe({
      next: () => {
        this.procesando.set(false);
        this.modalPago.set(false);
        this.toast.exito(`Pago de S/ ${this.pagoMonto.toFixed(2)} registrado`);
        this.refrescarPedidoAbierto();
        this.recargar(true);
      },
      error: (err: HttpErrorResponse) => {
        this.procesando.set(false);
        this.toast.error(err.error?.mensaje ?? 'No se pudo registrar el pago.');
      }
    });
  }

  // ---------- Entrega con cobro ----------
  confirmarEntrega() {
    const p = this.pedidoAbierto();
    if (!p) return;
    const saldo = p.total - p.montoPagado;
    this.procesando.set(true);

    const cobrar = this.pagoMonto > 0
      ? this.service.registrarPago(p.id, this.pagoMonto, this.pagoMetodo)
      : null;

    const flujo = () => {
      const nombreTercero = this.recibidoPor.trim();
      const titular = (p.clienteNombre ?? '').trim();
      // Solo pasamos "recibidoPor" si es distinto del titular
      const pasarRecibidoPor = nombreTercero && nombreTercero.toLowerCase() !== titular.toLowerCase()
        ? nombreTercero
        : undefined;
      this.service.siguienteArea(p.id, pasarRecibidoPor).subscribe({
        next: () => {
          this.procesando.set(false);
          this.modalEntrega.set(false);
          this.toast.exito(pasarRecibidoPor
            ? `Pedido #${p.numero} entregado a ${pasarRecibidoPor}`
            : `Pedido #${p.numero} entregado`);
          this.recargar(true);
          this.cerrarDetalle();
        },
        error: (err: HttpErrorResponse) => {
          this.procesando.set(false);
          this.toast.error(err.error?.mensaje ?? 'No se pudo completar la entrega.');
        }
      });
    };

    if (cobrar) {
      cobrar.subscribe({
        next: () => flujo(),
        error: (err: HttpErrorResponse) => {
          this.procesando.set(false);
          this.toast.error(err.error?.mensaje ?? 'No se pudo cobrar el saldo.');
        }
      });
    } else if (saldo > 0.01) {
      this.procesando.set(false);
      this.toast.advertencia('Hay saldo pendiente. Registra el cobro antes de entregar.');
    } else {
      flujo();
    }
  }

  // ---------- Agregar ítem ----------
  abrirModalItem() {
    this.itemServicioId = '';
    this.itemCantidad = 1;
    this.itemDescripcion = '';
    this.modalItem.set(true);
  }

  confirmarAgregarItem() {
    const p = this.pedidoAbierto();
    if (!p || !this.itemServicioId || this.itemCantidad <= 0) return;
    this.procesando.set(true);
    this.service.agregarItem(p.id, this.itemServicioId as number, this.itemCantidad, this.itemDescripcion.trim() || undefined).subscribe({
      next: () => {
        this.procesando.set(false);
        this.modalItem.set(false);
        this.toast.exito('Ítem agregado');
        this.refrescarPedidoAbierto();
        this.recargar(true);
      },
      error: (err: HttpErrorResponse) => {
        this.procesando.set(false);
        this.toast.error(err.error?.mensaje ?? 'No se pudo agregar el ítem.');
      }
    });
  }

  // ---------- Anular ----------
  abrirModalAnular() {
    this.motivoAnulacion = '';
    this.modalAnular.set(true);
  }

  confirmarAnular() {
    const p = this.pedidoAbierto();
    if (!p || this.motivoAnulacion.trim().length < 3) return;
    this.procesando.set(true);
    this.service.anular(p.id, this.motivoAnulacion.trim()).subscribe({
      next: () => {
        this.procesando.set(false);
        this.modalAnular.set(false);
        this.toast.advertencia(`Pedido #${p.numero} anulado`);
        this.recargar(true);
        this.cerrarDetalle();
      },
      error: (err: HttpErrorResponse) => {
        this.procesando.set(false);
        this.toast.error(err.error?.mensaje ?? 'No se pudo anular el pedido.');
      }
    });
  }

  // ---------- Cambiar fecha de entrega ----------
  abrirModalFecha() {
    const p = this.pedidoAbierto();
    if (!p) return;
    const base = p.fechaEntregaEst ? new Date(p.fechaEntregaEst) : new Date(Date.now() + 2 * 60 * 60 * 1000);
    this.fechaEntregaNueva = this.formatoLocal(base);
    this.motivoCambioFecha = '';
    this.modalFecha.set(true);
  }

  presetFechaEntrega(horas: number) {
    this.fechaEntregaNueva = this.formatoLocal(new Date(Date.now() + horas * 60 * 60 * 1000));
  }

  presetFechaMananaHora(hora: number) {
    const d = new Date();
    d.setDate(d.getDate() + 1);
    d.setHours(hora, 0, 0, 0);
    this.fechaEntregaNueva = this.formatoLocal(d);
  }

  private formatoLocal(d: Date): string {
    const pad = (n: number) => String(n).padStart(2, '0');
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
  }

  confirmarCambioFecha() {
    const p = this.pedidoAbierto();
    if (!p || !this.fechaEntregaNueva) return;
    this.procesando.set(true);
    const iso = new Date(this.fechaEntregaNueva).toISOString();
    this.service.cambiarFechaEntrega(p.id, iso, this.motivoCambioFecha.trim() || undefined).subscribe({
      next: () => {
        this.procesando.set(false);
        this.modalFecha.set(false);
        this.toast.exito('Fecha de entrega actualizada');
        this.refrescarPedidoAbierto();
        this.recargar(true);
      },
      error: (err: HttpErrorResponse) => {
        this.procesando.set(false);
        this.toast.error(err.error?.mensaje ?? 'No se pudo actualizar la fecha.');
      }
    });
  }

  avisarCambioFechaWhatsapp(p: Pedido) {
    if (!p.clienteCelular) {
      this.toast.advertencia('Este cliente no tiene celular registrado.');
      return;
    }
    const fecha = p.fechaEntregaEst
      ? new Date(p.fechaEntregaEst).toLocaleString('es-PE', { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' })
      : 'próximamente';
    const mensaje = this.whatsapp.mensaje('DEMORA', {
      cliente: p.clienteNombre ?? '',
      numero: String(p.numero),
      entrega: fecha,
    }, `Hola ${p.clienteNombre}, hemos actualizado la hora ${p.modalidad === 'Delivery' ? 'de entrega' : 'de recojo'} de tu pedido #${p.numero}. Nueva hora: ${fecha}. Disculpa las molestias.`);
    this.whatsapp.enviar(p.clienteCelular, mensaje);
  }

  private refrescarPedidoAbierto() {
    const p = this.pedidoAbierto();
    if (!p) return;
    this.service.obtener(p.id).subscribe(actualizado => {
      this.pedidoAbierto.set(actualizado);
      this.service.historial(actualizado.id).subscribe(h => this.historial.set(h));
    });
  }

  progresoAreas(p: Pedido): { nombre: string; alcanzada: boolean; actual: boolean; esFinal?: boolean; esEntregado?: boolean; id: string }[] {
    const areasList = this.areas();
    if (areasList.length === 0) return [];
    const idxActual = areasList.findIndex(a => a.id === p.areaActualId);

    const estaEntregado = p.estadoProceso === 'ENTREGADO';
    const estaListo = p.estadoProceso === 'LISTO';

    // Areas del proceso de lavado
    const pasos = areasList.map((a, i) => ({
      id: `a-${a.id}`,
      nombre: a.nombre,
      alcanzada: idxActual >= 0 && i <= idxActual,
      // Ya no es "actual" si el pedido pasó a LISTO o ENTREGADO
      actual: !estaListo && !estaEntregado && a.id === p.areaActualId
    }));

    // Paso final "Entregado" — solo aparece cuando ya está listo o entregado
    if (estaListo || estaEntregado) {
      pasos.push({
        id: 'entregado',
        nombre: 'Entregado',
        alcanzada: estaEntregado,
        actual: estaListo,  // parpadea si está LISTO esperando entrega
      });
    }

    return pasos;
  }

  tituloHistorial(h: PedidoHistorial): string {
    // El titulo depende del EVENTO, no del area
    switch (h.estadoProceso) {
      case 'LISTO': return '✓ Listo para recojo';
      case 'ENTREGADO': return '📦 Entregado al cliente';
      case 'ANULADO': return '🚫 Pedido anulado';
      case 'PENDIENTE':
        // Ingreso inicial del pedido
        return h.areaNombre ? `Ingreso · ${h.areaNombre}` : 'Ingreso de pedido';
      case 'EN_PROCESO':
      default:
        // Cambio de area normal
        return h.areaNombre ?? 'Actualización';
    }
  }

  claseEstado(estado: string): string {
    return ({
      'PENDIENTE': 'badge badge--gris',
      'EN_PROCESO': 'badge badge--azul',
      'LISTO': 'badge badge--verde',
      'ENTREGADO': 'badge badge--verde-oscuro',
      'ANULADO': 'badge badge--rojo'
    } as Record<string, string>)[estado] ?? 'badge badge--gris';
  }

  etiquetaEstado(estado: string): string {
    return ({
      'PENDIENTE': 'Pendiente',
      'EN_PROCESO': 'En proceso',
      'LISTO': 'Listo para recojo',
      'ENTREGADO': 'Entregado',
      'ANULADO': 'Anulado'
    } as Record<string, string>)[estado] ?? estado;
  }

  diasEnProceso(p: Pedido): number {
    const ms = Date.now() - new Date(p.fechaIngreso).getTime();
    return Math.max(0, Math.floor(ms / (24 * 60 * 60 * 1000)));
  }

  puedeAvanzar(p: Pedido): boolean {
    return p.estadoProceso !== 'ENTREGADO' && p.estadoProceso !== 'ANULADO';
  }

  botonAvanzarLabel(p: Pedido): string {
    if (p.estadoProceso === 'LISTO') return 'Marcar entregado';
    if (p.areaActualId == null) return 'Iniciar proceso';
    const areasList = this.areas();
    const idx = areasList.findIndex(a => a.id === p.areaActualId);
    if (idx === -1 || idx === areasList.length - 1) return 'Marcar listo';
    return `Pasar a "${areasList[idx + 1].nombre}"`;
  }

  imprimirTicket(p: Pedido) {
    window.open(`/ticket/${p.id}`, '_blank');
  }

  abrirWhatsapp(p: Pedido) {
    if (!p.clienteCelular) {
      this.toast.advertencia('Este cliente no tiene celular registrado.');
      return;
    }
    const cliente = p.clienteNombre ?? '';
    const numero = String(p.numero);
    const fallback = `Hola ${cliente}, tu pedido #${p.numero} ${
      p.estadoProceso === 'LISTO' ? 'está listo para recoger' : `está en la etapa: ${p.areaActualNombre ?? 'Pendiente'}`
    }. ¡Gracias por tu preferencia!`;

    let mensaje: string;
    if (p.estadoProceso === 'ENTREGADO') {
      mensaje = this.whatsapp.mensaje('ENTREGADO', { cliente, numero, total: p.total.toFixed(2) }, fallback);
    } else if (p.estadoProceso === 'LISTO') {
      mensaje = this.whatsapp.mensaje('LISTO', { cliente, numero, negocio: this.config.configuracion().nombreNegocio }, fallback);
    } else {
      mensaje = this.whatsapp.mensaje('CAMBIO_AREA', {
        cliente, numero,
        area: p.areaActualNombre ?? 'Pendiente',
        tiempoRestante: `${this.tiempoRestanteMinutos(p)} min`,
      }, fallback);
    }
    this.whatsapp.enviar(p.clienteCelular, mensaje);
  }
}
