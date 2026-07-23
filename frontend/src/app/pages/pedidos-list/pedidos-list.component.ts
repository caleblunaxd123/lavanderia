import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { AfterViewInit, Component, DestroyRef, ElementRef, OnDestroy, OnInit, ViewChild, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { DISTRITOS_LIMA_CALLAO } from '../../core/constants/distritos-lima-callao';
import { AreaLavado, Pedido, PedidoAbandonado, Servicio } from '../../core/models/models';
import { CatalogosService } from '../../core/services/catalogos.service';
import { ConfiguracionService } from '../../core/services/configuracion.service';
import { PedidoHistorial, PedidosService } from '../../core/services/pedidos.service';
import { FacturacionService } from '../../core/services/facturacion.service';
import { Motorizado, MotorizadosService } from '../../core/services/motorizados.service';
import { ToastService } from '../../core/services/toast.service';
import { WhatsappService } from '../../core/services/whatsapp.service';
import { EmptyStateComponent } from '../../shared/empty-state/empty-state.component';
import { PaginacionComponent } from '../../shared/paginacion/paginacion.component';
import { IconComponent } from '../../shared/icon/icon.component';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';
import { SkeletonComponent } from '../../shared/skeleton/skeleton.component';
import { MapaUbicacionComponent, UbicacionMapa } from '../../shared/mapa-ubicacion/mapa-ubicacion.component';
import { debounceTime } from 'rxjs';
import { ActualizacionDatosService } from '../../core/services/actualizacion-datos.service';
import { FotoPedido, FotosPedidoService, MomentoFoto } from '../../core/services/fotos-pedido.service';

type Filtro = 'pendientes' | 'otros' | 'ultimos' | 'fecha';
type TipoFecha = 'ingreso' | 'entrega';
type VistaPedidos = 'lista' | 'tablero';
type KanbanTone = 'pendiente' | 'area' | 'listo' | 'entregado' | 'anulado' | 'alerta';

interface KanbanColumn {
  key: string;
  titulo: string;
  subtitulo: string;
  tone: KanbanTone;
  pedidos: Pedido[];
}

@Component({
  selector: 'app-pedidos-list',
  imports: [CommonModule, FormsModule, RouterLink, EmptyStateComponent, PaginacionComponent, IconComponent, SkeletonComponent, PageHeaderComponent, MapaUbicacionComponent],
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
  private readonly motorizadosSvc = inject(MotorizadosService);
  private readonly actualizaciones = inject(ActualizacionDatosService);
  private readonly fotosSvc = inject(FotosPedidoService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  // ---- Fotos de evidencia del pedido abierto ----
  readonly fotosPedido = signal<Array<FotoPedido & { url: string | null }>>([]);
  readonly cargandoFotos = signal(false);
  readonly subiendoFoto = signal(false);
  readonly momentoFotoNueva = signal<MomentoFoto>('RECEPCION');
  readonly fotoAmpliada = signal<string | null>(null);

  // Modo "fuera de tiempo": llega desde la alerta del inicio (?ver=fuera-de-tiempo).
  // Muestra SOLO los pedidos cuya fecha de entrega/recojo ya venció.
  readonly soloFueraDeTiempo = signal(false);
  readonly emitiendoComprobante = signal(false);

  readonly pedidos = signal<Pedido[]>([]);
  readonly areas = signal<AreaLavado[]>([]);
  readonly cargando = signal(false);
  readonly error = signal<string | null>(null);
  readonly filtro = signal<Filtro>('pendientes');
  readonly vista = signal<VistaPedidos>(this.vistaInicial());
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

  readonly resumenFiltroFecha = computed(() => {
    if (this.filtro() !== 'fecha') return null;
    const etiquetaCampo = this.tipoFechaConsulta() === 'entrega' ? 'entrega' : 'ingreso';
    const desde = this.fechaFiltroDesde || 'inicio';
    const hasta = this.fechaFiltroHasta || 'hoy';
    return `Filtrando por fecha de ${etiquetaCampo}: ${desde} a ${hasta}`;
  });

  // Explicación corta del filtro activo, para que se entienda a qué se refiere cada uno.
  readonly descripcionFiltro = computed<string | null>(() => {
    if (this.busqueda().trim()) return null;
    switch (this.filtro()) {
      case 'ultimos': return 'Los pedidos más recientes, de todos los estados.';
      case 'pendientes': return 'Pedidos aún en proceso: recibidos, en producción o listos sin entregar.';
      case 'otros': return 'Pedidos ya entregados o anulados (fuera del flujo activo).';
      case 'fecha': return null; // el resumen del rango ya se muestra en su propia tarjeta
      default: return null;
    }
  });

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
      (!this.soloFueraDeTiempo() || this.entregaVencida(p)) &&
      (!f.numero || String(p.numero).includes(f.numero.trim())) &&
      (!f.cliente || norm(p.clienteNombre ?? '').includes(norm(f.cliente))) &&
      (!f.dni || norm(p.clienteDni ?? '').includes(norm(f.dni))) &&
      (!f.items || p.items.some(i => norm(i.servicioNombre ?? '').includes(norm(f.items)))) &&
      (!f.area || norm(p.areaActualNombre ?? '').includes(norm(f.area))) &&
      (!f.estado || norm(p.estadoProceso).includes(norm(f.estado))) &&
      (!f.pago || norm(p.estadoPago).includes(norm(f.pago)))
    );
  });

  // Total y saldo por cobrar del conjunto "fuera de tiempo" (para el resumen del banner).
  readonly resumenFueraDeTiempo = computed(() => {
    const lista = this.pedidosFiltrados();
    return {
      cantidad: lista.length,
      totalDinero: lista.reduce((acc, p) => acc + (p.total ?? 0), 0),
      saldoPorCobrar: lista.reduce((acc, p) => acc + this.saldoPedido(p), 0),
    };
  });

  readonly kanbanColumns = computed<KanbanColumn[]>(() => {
    const pedidos = this.pedidosFiltrados();
    const areas = [...this.areas()].sort((a, b) => a.orden - b.orden);
    const idsArea = new Set(areas.map(a => a.id));
    const flujoCompleto = this.filtro() === 'pendientes' && !this.busqueda().trim();

    const columnas: KanbanColumn[] = [
      {
        key: 'pendiente',
        titulo: 'Sin iniciar',
        subtitulo: 'En recepción',
        tone: 'pendiente',
        pedidos: pedidos.filter(p => !p.anulado && p.estadoProceso === 'PENDIENTE' && p.areaActualId == null)
      },
      ...areas.map(area => ({
        key: `area-${area.id}`,
        titulo: area.nombre,
        subtitulo: `Meta ${area.tiempoEstMinutos} min`,
        tone: 'area' as const,
        pedidos: pedidos.filter(p =>
          !p.anulado &&
          (p.estadoProceso === 'PENDIENTE' || p.estadoProceso === 'EN_PROCESO') &&
          p.areaActualId === area.id
        )
      })),
      {
        key: 'listo',
        titulo: 'Listos',
        subtitulo: 'Para entregar',
        tone: 'listo',
        pedidos: pedidos.filter(p => !p.anulado && p.estadoProceso === 'LISTO')
      }
    ];

    const inconsistentes = pedidos.filter(p => {
      if (p.anulado || !['PENDIENTE', 'EN_PROCESO'].includes(p.estadoProceso)) return false;
      if (p.estadoProceso === 'EN_PROCESO' && p.areaActualId == null) return true;
      return p.areaActualId != null && !idsArea.has(p.areaActualId);
    });
    if (inconsistentes.length > 0) {
      columnas.push({
        key: 'revisar',
        titulo: 'Revisar',
        subtitulo: 'Flujo inconsistente',
        tone: 'alerta',
        pedidos: inconsistentes
      });
    }

    const entregados = pedidos.filter(p => !p.anulado && p.estadoProceso === 'ENTREGADO');
    if (entregados.length > 0) {
      columnas.push({
        key: 'entregado',
        titulo: 'Entregados',
        subtitulo: 'Finalizados',
        tone: 'entregado',
        pedidos: entregados
      });
    }

    const anulados = pedidos.filter(p => p.anulado || p.estadoProceso === 'ANULADO');
    if (anulados.length > 0) {
      columnas.push({
        key: 'anulado',
        titulo: 'Anulados',
        subtitulo: 'Sin actividad',
        tone: 'anulado',
        pedidos: anulados
      });
    }

    return flujoCompleto ? columnas : columnas.filter(c => c.pedidos.length > 0);
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
  readonly avanzandoId = signal<number | null>(null);
  readonly pedidoAnimadoId = signal<number | null>(null);
  private pedidoAnimadoTimer?: ReturnType<typeof setTimeout>;

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
  private versionRecarga = 0;

  constructor() {
    this.actualizaciones.cambios('pedidos', 'foco').pipe(
      debounceTime(180),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe(() => this.refrescarDinamicamente());
  }


  readonly saldoPendiente = computed(() => {
    const p = this.pedidoAbierto();
    return p ? Math.max(0, p.total - p.montoPagado) : 0;
  });

  ngOnInit() {
    this.catalogos.areasLavado().subscribe(a => this.areas.set(a));
    this.catalogos.servicios().subscribe(s => this.servicios.set(s));
    this.whatsapp.cargar();
    this.cargarAbandonados();
    this.cargarMetaMensual();
    this.motorizadosSvc.listarActivos().subscribe(m => this.motorizadosActivos.set(m));
    this.timerId = setInterval(() => this.refrescarDinamicamente(), 10_000);

    // La carga inicial (y los cambios de ?ver=…) dependen del query param: si viene
    // "fuera-de-tiempo" (desde la alerta del inicio) mostramos solo los pedidos vencidos.
    let primera = true;
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(params => {
      const fuera = params.get('ver') === 'fuera-de-tiempo';
      if (fuera) {
        this.soloFueraDeTiempo.set(true);
        this.filtro.set('pendientes');
        this.busqueda.set('');
        this.vista.set('lista');
        this.tamanoPagina.set(100);
        this.pagina.set(1);
        this.recargar();
      } else {
        const estaba = this.soloFueraDeTiempo();
        this.soloFueraDeTiempo.set(false);
        if (estaba) this.tamanoPagina.set(15);
        if (primera || estaba) this.recargar();
      }
      primera = false;
    });
  }

  /** Sale del modo "fuera de tiempo" volviendo al listado normal. */
  salirFueraDeTiempo() {
    this.router.navigate([], { relativeTo: this.route, queryParams: {} });
  }

  ngAfterViewInit() {
    // Autofocus en el buscador para que el operador solo teclee N° de ticket + Enter
    setTimeout(() => this.buscadorInputRef?.nativeElement.focus(), 100);
  }

  cargarMetaMensual() {
    this.service.contadores().subscribe({
      next: d => {
        this.pedidosDelMes.set(d.pedidosDelMes);
        this.totalPendientesTab.set(d.totalPendientes);
        this.totalOtrosTab.set(d.totalOtros);
        this.totalUltimosTab.set(d.totalUltimos);
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
    if (this.pedidoAnimadoTimer) clearTimeout(this.pedidoAnimadoTimer);
  }

  cambiarVista(vista: VistaPedidos) {
    if (this.vista() === vista) return;
    this.vista.set(vista);
    this.pagina.set(1);
    try { localStorage.setItem('lavanderia.pedidos.vista', vista); } catch {}
    this.recargar();
  }

  cambiarFiltro(f: Filtro) {
    // Elegir un filtro sale del modo "fuera de tiempo" y limpia el query param.
    if (this.soloFueraDeTiempo()) {
      this.soloFueraDeTiempo.set(false);
      this.tamanoPagina.set(15);
      this.router.navigate([], { relativeTo: this.route, queryParams: {}, replaceUrl: true });
    }
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
    const version = ++this.versionRecarga;
    if (!silencioso) this.cargando.set(true);
    // Toda mutacion (avanzar, entregar, anular, pagar) y el refresco periodico entran con
    // silencioso=true: es el momento de refrescar tambien los contadores de las pestañas,
    // que salen del endpoint de dashboard y quedarian desactualizados.
    if (silencioso) this.cargarMetaMensual();
    this.error.set(null);
    const texto = this.busqueda().trim();
    const usandoFiltroFecha = !texto && this.filtro() === 'fecha';
    this.service.listar(
      texto ? undefined : this.filtro(),
      this.vista() === 'tablero' ? 1 : this.pagina(),
      this.vista() === 'tablero' ? 100 : this.tamanoPagina(),
      texto || undefined,
      usandoFiltroFecha && this.fechaFiltroDesde ? this.fechaFiltroDesde : undefined,
      usandoFiltroFecha && this.fechaFiltroHasta ? this.fechaFiltroHasta : undefined,
      usandoFiltroFecha ? this.tipoFechaConsulta() : undefined
    ).subscribe({
      next: res => {
        if (version !== this.versionRecarga) return;
        this.pedidos.set(res.items);
        this.total.set(res.total);
        this.cargando.set(false);
      },
      error: (err: HttpErrorResponse) => {
        if (version !== this.versionRecarga) return;
        this.cargando.set(false);
        if (!silencioso) {
          this.error.set(err.status === 0
            ? 'No se pudo conectar con el servidor. Verifica que el backend esté corriendo.'
            : (err.error?.mensaje ?? 'Error al cargar los pedidos.'));
        }
      }
    });
  }

  limpiarFiltroFecha() {
    this.fechaFiltroDesde = '';
    this.fechaFiltroHasta = '';
    this.tipoFechaConsulta.set('ingreso');
    this.cambiarFiltro('pendientes');
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
    this.cargarFotos(p.id);
  }

  cerrarDetalle() {
    this.liberarUrlsFotos();
    this.fotosPedido.set([]);
    this.fotoAmpliada.set(null);
    this.pedidoAbierto.set(null);
  }

  // ---------- Fotos de evidencia ----------
  private liberarUrlsFotos() {
    for (const f of this.fotosPedido()) { if (f.url) URL.revokeObjectURL(f.url); }
  }

  private cargarFotos(pedidoId: number) {
    this.liberarUrlsFotos();
    this.fotosPedido.set([]);
    this.cargandoFotos.set(true);
    this.fotosSvc.listar(pedidoId).subscribe({
      next: fotos => {
        this.fotosPedido.set(fotos.map(f => ({ ...f, url: null })));
        this.cargandoFotos.set(false);
        for (const f of fotos) {
          this.fotosSvc.urlArchivo(pedidoId, f.id).subscribe({
            next: url => this.fotosPedido.update(lista => lista.map(x => x.id === f.id ? { ...x, url } : x)),
            error: () => {}
          });
        }
      },
      error: () => this.cargandoFotos.set(false)
    });
  }

  async onFotoSeleccionada(evento: Event) {
    const input = evento.target as HTMLInputElement;
    const archivo = input.files?.[0];
    const p = this.pedidoAbierto();
    if (!archivo || !p) return;
    if (!archivo.type.startsWith('image/')) {
      this.toast.advertencia('Selecciona una imagen.');
      input.value = '';
      return;
    }
    this.subiendoFoto.set(true);
    try {
      const comprimida = await this.fotosSvc.comprimir(archivo);
      this.fotosSvc.subir(p.id, comprimida, this.momentoFotoNueva()).subscribe({
        next: () => { this.subiendoFoto.set(false); this.toast.exito('Foto agregada'); this.cargarFotos(p.id); },
        error: (err: HttpErrorResponse) => { this.subiendoFoto.set(false); this.toast.desdeHttp(err, 'No se pudo subir la foto.'); }
      });
    } catch {
      this.subiendoFoto.set(false);
      this.toast.error('No se pudo procesar la imagen.');
    }
    input.value = '';
  }

  eliminarFoto(fotoId: number) {
    const p = this.pedidoAbierto();
    if (!p) return;
    this.fotosSvc.eliminar(p.id, fotoId).subscribe({
      next: () => {
        const f = this.fotosPedido().find(x => x.id === fotoId);
        if (f?.url) URL.revokeObjectURL(f.url);
        this.fotosPedido.update(lista => lista.filter(x => x.id !== fotoId));
        this.toast.info('Foto eliminada');
      },
      error: () => this.toast.error('No se pudo eliminar la foto.')
    });
  }

  etiquetaMomento(m: MomentoFoto): string {
    return m === 'RECEPCION' ? 'Recepción' : m === 'ENTREGA' ? 'Entrega' : 'Otro';
  }

  avanzarEtapa(p: Pedido) {
    if (this.flujoInconsistente(p)) {
      this.toast.advertencia(
        'El pedido está EN PROCESO pero no tiene un área actual. No se reinició el flujo; revisa su historial.'
      );
      return;
    }
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
    this.avanzandoId.set(p.id);
    const eraLista = p.estadoProceso === 'LISTO';
    this.service.siguienteArea(p.id).subscribe({
      next: () => {
        this.avanzando.set(false);
        this.avanzandoId.set(null);
        this.destacarPedido(p.id);
        this.toast.exito(eraLista ? `Pedido #${p.numero} entregado` : 'Etapa actualizada');
        this.recargar(true);
        // Refresca el pedido para conocer su nuevo estado. Si acaba de quedar LISTO, avisa
        // automáticamente al cliente por WhatsApp que puede recogerlo (el operario ya no
        // tiene que acordarse de tocar el botón).
        this.service.obtener(p.id).subscribe(actualizado => {
          if (!actualizado) return;
          if (this.pedidoAbierto()?.id === p.id) {
            this.pedidoAbierto.set(actualizado);
            this.service.historial(actualizado.id).subscribe(h => this.historial.set(h));
          }
          if (!eraLista && actualizado.estadoProceso === 'LISTO' && actualizado.clienteCelular) {
            this.avisarListoAuto(actualizado);
          }
        });
      },
      error: (err: HttpErrorResponse) => {
        this.avanzando.set(false);
        this.avanzandoId.set(null);
        this.toast.desdeHttp(err, 'No se pudo avanzar la etapa.');
      }
    });
  }

  private refrescarDinamicamente() {
    if (typeof document !== 'undefined' && document.visibilityState !== 'visible') return;
    if (this.cargando() || this.procesando() || this.avanzando()) return;
    this.recargar(true);
    this.cargarAbandonados();
    this.refrescarPedidoAbierto();
  }

  private destacarPedido(id: number) {
    clearTimeout(this.pedidoAnimadoTimer);
    this.pedidoAnimadoId.set(id);
    this.pedidoAnimadoTimer = setTimeout(() => {
      if (this.pedidoAnimadoId() === id) this.pedidoAnimadoId.set(null);
    }, 1200);
  }

  /** Abre WhatsApp con el mensaje de "pedido listo" cuando el pedido acaba de terminar producción. */
  private avisarListoAuto(p: Pedido) {
    const cliente = (p.clienteNombre ?? '').trim().split(' ')[0] || 'cliente';
    const esDomicilio = p.modalidad === 'Recojo' || p.modalidad === 'Delivery';
    const fallback = esDomicilio
      ? `Hola ${cliente}! Tu pedido #${p.numero} ya está listo y saldrá a ruta.`
      : `Hola ${cliente}! Tu pedido #${p.numero} ya está listo para recoger en ${this.config.configuracion().nombreNegocio}. Te esperamos!`;
    const mensaje = this.whatsapp.mensaje('LISTO', {
      cliente, numero: String(p.numero), negocio: this.config.configuracion().nombreNegocio
    }, fallback);
    this.whatsapp.enviar(p.clienteCelular!, mensaje);
    this.toast.info(`Pedido #${p.numero} listo — se abrió WhatsApp para avisar a ${cliente}.`);
  }

  // ---------- Delivery / link de pago online ----------
  readonly convirtiendoDelivery = signal(false);
  readonly enviandoLinkPago = signal(false);
  readonly motorizadosActivos = signal<Motorizado[]>([]);
  readonly asignandoMotorizado = signal(false);
  readonly modalDestinoDelivery = signal(false);
  readonly pedidoAConvertir = signal<Pedido | null>(null);
  readonly distritos = DISTRITOS_LIMA_CALLAO;
  direccionEntregaConversion = '';
  distritoEntregaConversion = '';
  referenciaEntregaConversion = '';
  latitudEntregaConversion: number | null = null;
  longitudEntregaConversion: number | null = null;
  ubicacionConversionConfirmada = false;

  asignarMotorizado(p: Pedido, motorizadoIdTexto: string) {
    if (this.asignandoMotorizado()) return;
    const motorizadoId = motorizadoIdTexto ? Number(motorizadoIdTexto) : null;
    this.asignandoMotorizado.set(true);
    this.service.asignarMotorizado(p.id, motorizadoId).subscribe({
      next: () => {
        this.asignandoMotorizado.set(false);
        this.toast.exito(motorizadoId ? 'Repartidor asignado' : 'Repartidor quitado del pedido');
        this.refrescarPedidoAbierto();
        this.recargar(true);
      },
      error: (err: HttpErrorResponse) => {
        this.asignandoMotorizado.set(false);
        this.toast.desdeHttp(err, 'No se pudo asignar el repartidor.');
      }
    });
  }

  abrirDestinoDelivery(p: Pedido) {
    this.pedidoAConvertir.set(p);
    this.direccionEntregaConversion = p.direccionEntrega ?? '';
    this.distritoEntregaConversion = p.distritoEntrega ?? '';
    this.referenciaEntregaConversion = p.referenciaEntrega ?? '';
    this.latitudEntregaConversion = p.latitudEntrega ?? null;
    this.longitudEntregaConversion = p.longitudEntrega ?? null;
    this.ubicacionConversionConfirmada = p.latitudEntrega != null && p.longitudEntrega != null;
    this.modalDestinoDelivery.set(true);
  }

  cerrarDestinoDelivery() {
    if (this.convirtiendoDelivery()) return;
    this.modalDestinoDelivery.set(false);
    this.pedidoAConvertir.set(null);
  }

  actualizarUbicacionConversion(ubicacion: UbicacionMapa | null) {
    this.latitudEntregaConversion = ubicacion?.latitud ?? null;
    this.longitudEntregaConversion = ubicacion?.longitud ?? null;
    this.ubicacionConversionConfirmada = !!ubicacion?.etiqueta;
    if (ubicacion?.direccion) this.direccionEntregaConversion = ubicacion.direccion;
    if (ubicacion?.distrito) this.distritoEntregaConversion = ubicacion.distrito;
  }

  actualizarDireccionConversion(valor: string) {
    if (valor !== this.direccionEntregaConversion) this.ubicacionConversionConfirmada = false;
    this.direccionEntregaConversion = valor;
  }

  actualizarDistritoConversion(valor: string) {
    if (valor !== this.distritoEntregaConversion) this.ubicacionConversionConfirmada = false;
    this.distritoEntregaConversion = valor;
  }

  confirmarConversionDelivery() {
    const p = this.pedidoAConvertir();
    if (!p || this.convirtiendoDelivery()) return;
    if (!this.direccionEntregaConversion.trim() || !this.distritoEntregaConversion) {
      this.toast.advertencia('Completa la dirección exacta y el distrito de entrega.');
      return;
    }
    if (this.latitudEntregaConversion === null || this.longitudEntregaConversion === null || !this.ubicacionConversionConfirmada) {
      this.toast.advertencia('Confirma la dirección y el punto exacto en el mapa.');
      return;
    }
    if (this.convirtiendoDelivery()) return;
    this.convirtiendoDelivery.set(true);
    this.service.convertirDelivery(p.id, {
      direccionEntrega: this.direccionEntregaConversion.trim(),
      distritoEntrega: this.distritoEntregaConversion,
      referenciaEntrega: this.referenciaEntregaConversion.trim() || null,
      latitudEntrega: this.latitudEntregaConversion,
      longitudEntrega: this.longitudEntregaConversion
    }).subscribe({
      next: () => {
        this.convirtiendoDelivery.set(false);
        this.modalDestinoDelivery.set(false);
        this.pedidoAConvertir.set(null);
        this.toast.exito(p.modalidad === 'Delivery'
          ? `Destino del pedido #${p.numero} actualizado`
          : `Pedido #${p.numero} convertido a Delivery`);
        this.refrescarPedidoAbierto();
        this.recargar(true);
      },
      error: (err: HttpErrorResponse) => {
        this.convirtiendoDelivery.set(false);
        this.toast.desdeHttp(err, 'No se pudo convertir el pedido a Delivery.');
      }
    });
  }

  urlMapaPedido(p: Pedido): string | null {
    if (p.latitudEntrega == null || p.longitudEntrega == null) return null;
    return `https://www.openstreetmap.org/?mlat=${p.latitudEntrega}&mlon=${p.longitudEntrega}#map=18/${p.latitudEntrega}/${p.longitudEntrega}`;
  }

  // ---------- Seguimiento en vivo del repartidor ----------
  readonly generandoLinkRepartidor = signal(false);

  private conLinkRepartidor(p: Pedido, accion: (url: string) => void) {
    if (this.generandoLinkRepartidor()) return;
    this.generandoLinkRepartidor.set(true);
    this.service.linkRepartidor(p.id).subscribe({
      next: ({ token }) => {
        this.generandoLinkRepartidor.set(false);
        accion(`${window.location.origin}/repartidor/${token}`);
      },
      error: (err: HttpErrorResponse) => {
        this.generandoLinkRepartidor.set(false);
        this.toast.desdeHttp(err, 'No se pudo generar el enlace del repartidor.');
      }
    });
  }

  /** Manda al repartidor (por WhatsApp) el link donde comparte su ubicación en vivo. */
  enviarLinkRepartidor(p: Pedido) {
    if (!p.motorizadoId) { this.toast.advertencia('Primero asigna un repartidor al pedido.'); return; }
    this.conLinkRepartidor(p, url => {
      const cliente = (p.clienteNombre || 'el cliente').trim();
      const destino = [p.direccionEntrega, p.distritoEntrega].filter(Boolean).join(', ');
      const mensaje = `🛵 *Reparto — Pedido #${p.numero}*\n\nCliente: ${cliente}\nDirección: ${destino || 'ver en el mapa'}\n\nAbre este enlace en tu celular para compartir tu ubicación en vivo y marcar la entrega:\n${url}`;
      if (p.motorizadoCelular) {
        this.whatsapp.enviar(p.motorizadoCelular, mensaje);
      } else {
        this.copiarTexto(url, 'El repartidor no tiene celular. Copié el enlace para que se lo pases.');
      }
    });
  }

  copiarLinkRepartidor(p: Pedido) {
    this.conLinkRepartidor(p, url => this.copiarTexto(url, 'Enlace del repartidor copiado.'));
  }

  /** Avisa al cliente por WhatsApp que su pedido va en camino, con el link de seguimiento en vivo. */
  avisarEnCaminoCliente(p: Pedido) {
    if (!p.clienteCelular) { this.toast.advertencia('Este cliente no tiene celular registrado.'); return; }
    if (this.enviandoLinkPago()) return;
    this.enviandoLinkPago.set(true);
    this.service.linkSeguimiento(p.id).subscribe({
      next: ({ token }) => {
        this.enviandoLinkPago.set(false);
        const url = `${window.location.origin}/seguimiento/${token}`;
        const cliente = (p.clienteNombre || 'cliente').trim();
        const negocio = this.config.configuracion().nombreNegocio || 'la lavandería';
        const fallback = `Hola ${cliente}! Tu pedido #${p.numero} de ${negocio} ya va en camino a tu dirección. Sigue al repartidor en tiempo real aquí:\n${url}`;
        const mensaje = this.whatsapp.mensaje('EN_RUTA', {
          cliente, numero: String(p.numero), negocio, seguimiento: url
        }, fallback);
        const texto = mensaje.includes(url) ? mensaje : `${mensaje}\n${url}`;
        this.whatsapp.enviar(p.clienteCelular!, texto);
      },
      error: (err: HttpErrorResponse) => {
        this.enviandoLinkPago.set(false);
        this.toast.desdeHttp(err, 'No se pudo generar el enlace de seguimiento.');
      }
    });
  }

  private copiarTexto(texto: string, ok: string) {
    navigator.clipboard?.writeText(texto)
      .then(() => this.toast.exito(ok))
      .catch(() => this.toast.advertencia('No se pudo copiar. Enlace: ' + texto));
  }

  enviarLinkPago(p: Pedido) {
    if (this.enviandoLinkPago() || !p.clienteCelular) {
      if (!p.clienteCelular) this.toast.advertencia('Este cliente no tiene celular registrado.');
      return;
    }
    this.enviandoLinkPago.set(true);
    this.service.linkSeguimiento(p.id).subscribe({
      next: ({ token }) => {
        this.enviandoLinkPago.set(false);
        const url = `${window.location.origin}/seguimiento/${token}`;
        const mensaje = this.whatsapp.mensajeIngreso(p, this.config.configuracion(), url);
        this.whatsapp.enviar(p.clienteCelular!, mensaje);
      },
      error: (err: HttpErrorResponse) => {
        this.enviandoLinkPago.set(false);
        this.toast.desdeHttp(err, 'No se pudo generar el enlace de seguimiento.');
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
        this.toast.desdeHttp(err, 'No se pudo emitir el comprobante.');
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

  cerrarModalPago() { if (!this.procesando()) this.modalPago.set(false); }
  cerrarModalEntrega() { if (!this.procesando()) this.modalEntrega.set(false); }
  cerrarModalItem() { if (!this.procesando()) this.modalItem.set(false); }
  cerrarModalFecha() { if (!this.procesando()) this.modalFecha.set(false); }
  cerrarModalAnular() { if (!this.procesando()) this.modalAnular.set(false); }

  confirmarPago() {
    const p = this.pedidoAbierto();
    const saldo = this.saldoPendiente();
    if (!p || this.procesando()) return;
    if (!Number.isFinite(this.pagoMonto) || this.pagoMonto <= 0 || this.pagoMonto > saldo + 0.01) {
      this.toast.advertencia(`Ingresa un monto válido de hasta S/ ${saldo.toFixed(2)}.`);
      return;
    }
    this.pagoMonto = Math.round(this.pagoMonto * 100) / 100;
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
        this.toast.desdeHttp(err, 'No se pudo registrar el pago.');
      }
    });
  }

  // ---------- Entrega con cobro ----------
  confirmarEntrega() {
    const p = this.pedidoAbierto();
    if (!p || this.procesando()) return;
    const saldo = p.total - p.montoPagado;
    if (saldo > 0.01 && (!Number.isFinite(this.pagoMonto) || Math.abs(this.pagoMonto - saldo) > 0.01)) {
      this.toast.advertencia(`Para entregar debes cobrar el saldo completo de S/ ${saldo.toFixed(2)}.`);
      return;
    }
    if (this.recibidoPor.trim().length > 120) {
      this.toast.advertencia('El nombre de quien recibe no puede superar 120 caracteres.');
      return;
    }
    this.pagoMonto = Math.round(Math.max(0, this.pagoMonto) * 100) / 100;
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
          this.toast.desdeHttp(err, 'No se pudo completar la entrega.');
        }
      });
    };

    if (cobrar) {
      cobrar.subscribe({
        next: () => flujo(),
        error: (err: HttpErrorResponse) => {
          this.procesando.set(false);
          this.toast.desdeHttp(err, 'No se pudo cobrar el saldo.');
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
    if (!p || this.procesando() || !this.itemServicioId) return;
    if (!Number.isFinite(this.itemCantidad) || this.itemCantidad <= 0 || this.itemCantidad > 10_000) {
      this.toast.advertencia('La cantidad debe ser mayor a 0 y no superar 10,000.');
      return;
    }
    if (this.itemDescripcion.trim().length > 200) {
      this.toast.advertencia('La observación no puede superar 200 caracteres.');
      return;
    }
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
        this.toast.desdeHttp(err, 'No se pudo agregar el ítem.');
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
        this.toast.desdeHttp(err, 'No se pudo anular el pedido.');
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

  get fechaEntregaMinima(): string {
    return this.formatoLocal(new Date(Date.now() + 5 * 60 * 1000));
  }

  confirmarCambioFecha() {
    const p = this.pedidoAbierto();
    if (!p || this.procesando() || !this.fechaEntregaNueva) return;
    const nuevaFecha = new Date(this.fechaEntregaNueva);
    if (Number.isNaN(nuevaFecha.getTime()) || nuevaFecha.getTime() < Date.now() + 4 * 60 * 1000) {
      this.toast.advertencia('La nueva fecha de entrega debe ser posterior al momento actual.');
      return;
    }
    if (this.motivoCambioFecha.trim().length > 200) {
      this.toast.advertencia('El motivo no puede superar 200 caracteres.');
      return;
    }
    this.procesando.set(true);
    const iso = nuevaFecha.toISOString();
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
        this.toast.desdeHttp(err, 'No se pudo actualizar la fecha.');
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

  saldoPedido(p: Pedido): number {
    return Math.max(0, p.total - p.montoPagado);
  }

  puedeAvanzar(p: Pedido): boolean {
    return !['ENTREGADO', 'ANULADO', 'DONADO'].includes(p.estadoProceso) && !this.flujoInconsistente(p);
  }

  flujoInconsistente(p: Pedido): boolean {
    if (!['PENDIENTE', 'EN_PROCESO'].includes(p.estadoProceso)) return false;
    if (p.estadoProceso === 'EN_PROCESO' && p.areaActualId == null) return true;
    return p.areaActualId != null && !this.areas().some(a => a.id === p.areaActualId);
  }

  botonAvanzarLabel(p: Pedido): string {
    if (p.estadoProceso === 'LISTO') return 'Marcar entregado';
    if (p.estadoProceso === 'PENDIENTE' && p.areaActualId == null) return 'Iniciar proceso';
    const areasList = this.areas();
    const idx = areasList.findIndex(a => a.id === p.areaActualId);
    if (idx === -1 || idx === areasList.length - 1) return 'Marcar listo';
    return `Pasar a "${areasList[idx + 1].nombre}"`;
  }

  kanbanActionLabel(p: Pedido): string {
    if (p.estadoProceso === 'LISTO') return 'Entregar';
    if (p.estadoProceso === 'PENDIENTE' && p.areaActualId == null) return 'Iniciar';
    const areasList = this.areas();
    const idx = areasList.findIndex(a => a.id === p.areaActualId);
    if (idx === -1 || idx === areasList.length - 1) return 'Marcar listo';
    return `Mover a ${areasList[idx + 1].nombre}`;
  }

  entregaVencida(p: Pedido): boolean {
    if (!p.fechaEntregaEst || ['ENTREGADO', 'ANULADO'].includes(p.estadoProceso)) return false;
    return new Date(p.fechaEntregaEst).getTime() < Date.now();
  }

  private vistaInicial(): VistaPedidos {
    try {
      const guardada = localStorage.getItem('lavanderia.pedidos.vista');
      if (guardada === 'lista' || guardada === 'tablero') return guardada;
    } catch {}
    return typeof window !== 'undefined' && window.matchMedia('(max-width: 720px)').matches ? 'lista' : 'tablero';
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
