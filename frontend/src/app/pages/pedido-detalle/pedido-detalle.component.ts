import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { debounceTime } from 'rxjs';
import { DISTRITOS_LIMA_CALLAO } from '../../core/constants/distritos-lima-callao';
import { AreaLavado, Pedido, Servicio } from '../../core/models/models';
import { ActualizacionDatosService } from '../../core/services/actualizacion-datos.service';
import { CatalogosService } from '../../core/services/catalogos.service';
import { ConfiguracionService } from '../../core/services/configuracion.service';
import { FacturacionService } from '../../core/services/facturacion.service';
import { FotoPedido, FotosPedidoService, MomentoFoto } from '../../core/services/fotos-pedido.service';
import { Motorizado, MotorizadosService } from '../../core/services/motorizados.service';
import { PedidoHistorial, PedidosService } from '../../core/services/pedidos.service';
import { ToastService } from '../../core/services/toast.service';
import { WhatsappService } from '../../core/services/whatsapp.service';
import { IconComponent } from '../../shared/icon/icon.component';
import { MapaUbicacionComponent, UbicacionMapa } from '../../shared/mapa-ubicacion/mapa-ubicacion.component';
import { SkeletonComponent } from '../../shared/skeleton/skeleton.component';
import { TourService } from '../../core/services/tour.service';
import { TOURS } from '../../core/constants/tours';

/**
 * Página dedicada del pedido (/pedidos/:id).
 *
 * Diseñada para el trabajador de mostrador: UNA acción principal según el estado
 * (avanzar / cobrar y entregar), el cobro siempre visible, y todo lo secundario
 * agrupado en "Más acciones" o colapsado (fotos, historial). Reemplaza al antiguo
 * modal sobrecargado de la lista de pedidos.
 */
@Component({
  selector: 'app-pedido-detalle',
  imports: [CommonModule, FormsModule, IconComponent, MapaUbicacionComponent, SkeletonComponent],
  templateUrl: './pedido-detalle.component.html',
  styleUrl: './pedido-detalle.component.scss'
})
export class PedidoDetalleComponent implements OnInit, OnDestroy {
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

  readonly pedido = signal<Pedido | null>(null);
  readonly cargando = signal(true);
  readonly error = signal<string | null>(null);
  readonly areas = signal<AreaLavado[]>([]);
  readonly servicios = signal<Servicio[]>([]);
  readonly historial = signal<PedidoHistorial[]>([]);
  readonly cargandoHistorial = signal(false);
  readonly motorizadosActivos = signal<Motorizado[]>([]);

  readonly menuAcciones = signal(false);

  // Modales de acción
  readonly modalPago = signal(false);
  readonly modalEntrega = signal(false);
  readonly modalItem = signal(false);
  readonly modalFecha = signal(false);
  readonly modalAnular = signal(false);
  readonly modalDestinoDelivery = signal(false);

  // Formularios
  pagoMonto = 0;
  pagoMetodo: 'EFECTIVO' | 'YAPE' | 'PLIN' | 'TRANSFERENCIA' | 'POS' = 'EFECTIVO';
  recibidoPor = '';
  itemServicioId: number | '' = '';
  itemCantidad = 1;
  itemDescripcion = '';
  motivoAnulacion = '';
  fechaEntregaNueva = '';
  motivoCambioFecha = '';
  readonly procesando = signal(false);
  readonly avanzando = signal(false);
  readonly emitiendoComprobante = signal(false);

  // Delivery
  readonly convirtiendoDelivery = signal(false);
  readonly enviandoLinkPago = signal(false);
  readonly asignandoMotorizado = signal(false);
  readonly generandoLinkRepartidor = signal(false);
  readonly distritos = DISTRITOS_LIMA_CALLAO;
  direccionEntregaConversion = '';
  distritoEntregaConversion = '';
  referenciaEntregaConversion = '';
  latitudEntregaConversion: number | null = null;
  longitudEntregaConversion: number | null = null;
  ubicacionConversionConfirmada = false;

  // Fotos de evidencia
  readonly fotosPedido = signal<Array<FotoPedido & { url: string | null }>>([]);
  readonly cargandoFotos = signal(false);
  readonly subiendoFoto = signal(false);
  readonly momentoFotoNueva = signal<MomentoFoto>('RECEPCION');
  readonly fotoAmpliada = signal<string | null>(null);

  private pedidoId = 0;

  constructor() {
    // Cambios hechos por otros usuarios/pestañas: refresca este pedido en silencio.
    this.actualizaciones.cambios('pedidos', 'foco').pipe(
      debounceTime(200),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe(() => {
      if (!this.procesando() && !this.avanzando()) this.refrescar(true);
    });
  }

  ngOnInit() {
    this.catalogos.areasLavado().subscribe(a => this.areas.set(a));
    this.catalogos.servicios().subscribe(s => this.servicios.set(s));
    this.whatsapp.cargar();
    this.motorizadosSvc.listarActivos().subscribe(m => this.motorizadosActivos.set(m));

    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(params => {
      const id = Number(params.get('id'));
      if (!Number.isInteger(id) || id <= 0) { this.volver(); return; }
      this.pedidoId = id;
      this.cargar();
    });
  }

  ngOnDestroy() { this.liberarUrlsFotos(); }

  volver() { this.router.navigate(['/pedidos']); }

  private readonly tour = inject(TourService);
  iniciarTour() { this.tour.iniciar(TOURS['pedido-detalle']); }

  private cargar() {
    this.cargando.set(true);
    this.error.set(null);
    this.service.obtener(this.pedidoId).subscribe({
      next: p => { this.pedido.set(p); this.cargando.set(false); },
      error: (err: HttpErrorResponse) => {
        this.cargando.set(false);
        this.error.set(err.status === 404 ? 'Este pedido no existe o pertenece a otra sede.' : 'No se pudo cargar el pedido.');
      }
    });
    this.cargandoHistorial.set(true);
    this.service.historial(this.pedidoId).subscribe({
      next: h => { this.historial.set(h); this.cargandoHistorial.set(false); },
      error: () => this.cargandoHistorial.set(false)
    });
    this.cargarFotos();
  }

  private refrescar(silencioso = false) {
    if (!this.pedidoId) return;
    this.service.obtener(this.pedidoId).subscribe({
      next: p => this.pedido.set(p),
      error: () => { if (!silencioso) this.toast.error('No se pudo actualizar el pedido.'); }
    });
    this.service.historial(this.pedidoId).subscribe(h => this.historial.set(h));
  }

  // ---------- Derivados ----------
  readonly saldoPendiente = computed(() => {
    const p = this.pedido();
    return p ? Math.max(0, p.total - p.montoPagado) : 0;
  });

  readonly esDomicilio = computed(() => {
    const m = this.pedido()?.modalidad;
    return m === 'Recojo' || m === 'Delivery';
  });

  progresoAreas(p: Pedido): { nombre: string; alcanzada: boolean; actual: boolean; id: string }[] {
    const areasList = this.areas();
    if (areasList.length === 0) return [];
    const idxActual = areasList.findIndex(a => a.id === p.areaActualId);
    const estaEntregado = p.estadoProceso === 'ENTREGADO';
    const estaListo = p.estadoProceso === 'LISTO';

    const pasos = areasList.map((a, i) => ({
      id: `a-${a.id}`,
      nombre: a.nombre,
      alcanzada: estaListo || estaEntregado || (idxActual >= 0 && i <= idxActual),
      actual: !estaListo && !estaEntregado && a.id === p.areaActualId
    }));
    pasos.push({
      id: 'entregado',
      nombre: estaEntregado ? 'Entregado' : 'Entrega',
      alcanzada: estaEntregado,
      actual: estaListo
    });
    return pasos;
  }

  flujoInconsistente(p: Pedido): boolean {
    if (!['PENDIENTE', 'EN_PROCESO'].includes(p.estadoProceso)) return false;
    if (p.estadoProceso === 'EN_PROCESO' && p.areaActualId == null) return true;
    return p.areaActualId != null && !this.areas().some(a => a.id === p.areaActualId);
  }

  puedeAvanzar(p: Pedido): boolean {
    return !p.anulado && !['ENTREGADO', 'ANULADO', 'DONADO'].includes(p.estadoProceso) && !this.flujoInconsistente(p);
  }

  /** Texto de LA acción principal: el sistema le dice al trabajador qué toca hacer ahora. */
  accionPrincipalLabel(p: Pedido): string {
    if (p.estadoProceso === 'LISTO') {
      return this.saldoPendiente() > 0.01
        ? `Cobrar S/ ${this.saldoPendiente().toFixed(2)} y entregar`
        : 'Entregar pedido';
    }
    if (p.estadoProceso === 'PENDIENTE' && p.areaActualId == null) return 'Iniciar proceso';
    const areasList = this.areas();
    const idx = areasList.findIndex(a => a.id === p.areaActualId);
    if (idx === -1 || idx === areasList.length - 1) return 'Marcar listo';
    return `Avanzar a ${areasList[idx + 1].nombre}`;
  }

  accionPrincipal(p: Pedido) {
    if (this.flujoInconsistente(p)) {
      this.toast.advertencia('El pedido está EN PROCESO pero no tiene un área actual. Revisa su historial.');
      return;
    }
    if (p.estadoProceso === 'LISTO') {
      const saldo = this.saldoPendiente();
      this.pagoMonto = saldo > 0.01 ? Math.round(saldo * 100) / 100 : 0;
      this.pagoMetodo = 'EFECTIVO';
      this.recibidoPor = '';
      this.modalEntrega.set(true);
      return;
    }
    this.ejecutarAvance(p);
  }

  private ejecutarAvance(p: Pedido) {
    this.avanzando.set(true);
    this.service.siguienteArea(p.id).subscribe({
      next: () => {
        this.avanzando.set(false);
        this.toast.exito('Etapa actualizada');
        this.service.obtener(p.id).subscribe(actualizado => {
          this.pedido.set(actualizado);
          this.service.historial(p.id).subscribe(h => this.historial.set(h));
          if (actualizado.estadoProceso === 'LISTO' && actualizado.clienteCelular) {
            this.avisarListoAuto(actualizado);
          }
        });
      },
      error: (err: HttpErrorResponse) => {
        this.avanzando.set(false);
        this.toast.desdeHttp(err, 'No se pudo avanzar la etapa.');
      }
    });
  }

  private avisarListoAuto(p: Pedido) {
    const cliente = (p.clienteNombre ?? '').trim().split(' ')[0] || 'cliente';
    const fallback = this.esDomicilio()
      ? `Hola ${cliente}! Tu pedido #${p.numero} ya está listo y saldrá a ruta.`
      : `Hola ${cliente}! Tu pedido #${p.numero} ya está listo para recoger en ${this.config.configuracion().nombreNegocio}. Te esperamos!`;
    const mensaje = this.whatsapp.mensaje('LISTO', {
      cliente, numero: String(p.numero), negocio: this.config.configuracion().nombreNegocio
    }, fallback);
    this.whatsapp.enviar(p.clienteCelular!, mensaje);
    this.toast.info(`Pedido #${p.numero} listo — se abrió WhatsApp para avisar a ${cliente}.`);
  }

  // ---------- WhatsApp contextual ----------
  abrirWhatsapp(p: Pedido) {
    if (!p.clienteCelular) { this.toast.advertencia('Este cliente no tiene celular registrado.'); return; }
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
        cliente, numero, area: p.areaActualNombre ?? 'Pendiente', tiempoRestante: `${this.tiempoRestanteMinutos(p)} min`,
      }, fallback);
    }
    this.whatsapp.enviar(p.clienteCelular, mensaje);
  }

  private tiempoRestanteMinutos(p: Pedido): number {
    const lista = this.areas();
    const idx = lista.findIndex(a => a.id === p.areaActualId);
    if (idx === -1) return 0;
    return lista.slice(idx).reduce((acc, a) => acc + a.tiempoEstMinutos, 0);
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
        const mensaje = this.whatsapp.mensaje('EN_RUTA', { cliente, numero: String(p.numero), negocio, seguimiento: url }, fallback);
        this.whatsapp.enviar(p.clienteCelular!, mensaje.includes(url) ? mensaje : `${mensaje}\n${url}`);
      },
      error: (err: HttpErrorResponse) => {
        this.enviandoLinkPago.set(false);
        this.toast.desdeHttp(err, 'No se pudo generar el enlace de seguimiento.');
      }
    });
  }

  // ---------- Repartidor ----------
  asignarMotorizado(p: Pedido, motorizadoIdTexto: string) {
    if (this.asignandoMotorizado()) return;
    const motorizadoId = motorizadoIdTexto ? Number(motorizadoIdTexto) : null;
    this.asignandoMotorizado.set(true);
    this.service.asignarMotorizado(p.id, motorizadoId).subscribe({
      next: () => {
        this.asignandoMotorizado.set(false);
        this.toast.exito(motorizadoId ? 'Repartidor asignado' : 'Repartidor quitado del pedido');
        this.refrescar();
      },
      error: (err: HttpErrorResponse) => {
        this.asignandoMotorizado.set(false);
        this.toast.desdeHttp(err, 'No se pudo asignar el repartidor.');
      }
    });
  }

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

  enviarLinkRepartidor(p: Pedido) {
    if (!p.motorizadoId) { this.toast.advertencia('Primero asigna un repartidor al pedido.'); return; }
    this.conLinkRepartidor(p, url => {
      const cliente = (p.clienteNombre || 'el cliente').trim();
      const destino = [p.direccionEntrega, p.distritoEntrega].filter(Boolean).join(', ');
      const mensaje = `🛵 *Reparto — Pedido #${p.numero}*\n\nCliente: ${cliente}\nDirección: ${destino || 'ver en el mapa'}\n\nAbre este enlace en tu celular para compartir tu ubicación en vivo y marcar la entrega:\n${url}`;
      if (p.motorizadoCelular) this.whatsapp.enviar(p.motorizadoCelular, mensaje);
      else this.copiarTexto(url, 'El repartidor no tiene celular. Copié el enlace para que se lo pases.');
    });
  }

  copiarLinkRepartidor(p: Pedido) {
    this.conLinkRepartidor(p, url => this.copiarTexto(url, 'Enlace del repartidor copiado.'));
  }

  private copiarTexto(texto: string, ok: string) {
    navigator.clipboard?.writeText(texto)
      .then(() => this.toast.exito(ok))
      .catch(() => this.toast.advertencia('No se pudo copiar. Enlace: ' + texto));
  }

  urlMapaPedido(p: Pedido): string | null {
    if (p.latitudEntrega == null || p.longitudEntrega == null) return null;
    return `https://www.openstreetmap.org/?mlat=${p.latitudEntrega}&mlon=${p.longitudEntrega}#map=18/${p.latitudEntrega}/${p.longitudEntrega}`;
  }

  // ---------- Pago ----------
  abrirModalPago() {
    const p = this.pedido();
    if (!p) return;
    this.pagoMonto = Math.max(0, p.total - p.montoPagado);
    this.pagoMetodo = 'EFECTIVO';
    this.modalPago.set(true);
  }

  confirmarPago() {
    const p = this.pedido();
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
        this.refrescar();
      },
      error: (err: HttpErrorResponse) => {
        this.procesando.set(false);
        this.toast.desdeHttp(err, 'No se pudo registrar el pago.');
      }
    });
  }

  // ---------- Entrega (cobra el saldo si hay) ----------
  confirmarEntrega() {
    const p = this.pedido();
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

    const finalizar = () => {
      const nombreTercero = this.recibidoPor.trim();
      const titular = (p.clienteNombre ?? '').trim();
      const pasarRecibidoPor = nombreTercero && nombreTercero.toLowerCase() !== titular.toLowerCase()
        ? nombreTercero : undefined;
      this.service.siguienteArea(p.id, pasarRecibidoPor).subscribe({
        next: () => {
          this.procesando.set(false);
          this.modalEntrega.set(false);
          this.toast.exito(pasarRecibidoPor
            ? `Pedido #${p.numero} entregado a ${pasarRecibidoPor}`
            : `Pedido #${p.numero} entregado`);
          this.refrescar();
        },
        error: (err: HttpErrorResponse) => {
          this.procesando.set(false);
          this.toast.desdeHttp(err, 'No se pudo completar la entrega.');
        }
      });
    };

    if (this.pagoMonto > 0) {
      this.service.registrarPago(p.id, this.pagoMonto, this.pagoMetodo).subscribe({
        next: () => finalizar(),
        error: (err: HttpErrorResponse) => {
          this.procesando.set(false);
          this.toast.desdeHttp(err, 'No se pudo cobrar el saldo.');
        }
      });
    } else if (saldo > 0.01) {
      this.procesando.set(false);
      this.toast.advertencia('Hay saldo pendiente. Registra el cobro antes de entregar.');
    } else {
      finalizar();
    }
  }

  // ---------- Ítems ----------
  abrirModalItem() {
    this.itemServicioId = '';
    this.itemCantidad = 1;
    this.itemDescripcion = '';
    this.modalItem.set(true);
  }

  confirmarAgregarItem() {
    const p = this.pedido();
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
        this.refrescar();
      },
      error: (err: HttpErrorResponse) => {
        this.procesando.set(false);
        this.toast.desdeHttp(err, 'No se pudo agregar el ítem.');
      }
    });
  }

  // ---------- Cambiar fecha ----------
  abrirModalFecha() {
    const p = this.pedido();
    if (!p) return;
    const base = p.fechaEntregaEst ? new Date(p.fechaEntregaEst) : new Date(Date.now() + 2 * 60 * 60 * 1000);
    this.fechaEntregaNueva = this.formatoLocal(base);
    this.motivoCambioFecha = '';
    this.menuAcciones.set(false);
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
    const p = this.pedido();
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
    this.service.cambiarFechaEntrega(p.id, nuevaFecha.toISOString(), this.motivoCambioFecha.trim() || undefined).subscribe({
      next: () => {
        this.procesando.set(false);
        this.modalFecha.set(false);
        this.toast.exito('Fecha de entrega actualizada');
        this.refrescar();
      },
      error: (err: HttpErrorResponse) => {
        this.procesando.set(false);
        this.toast.desdeHttp(err, 'No se pudo actualizar la fecha.');
      }
    });
  }

  avisarCambioFechaWhatsapp(p: Pedido) {
    if (!p.clienteCelular) { this.toast.advertencia('Este cliente no tiene celular registrado.'); return; }
    const fecha = p.fechaEntregaEst
      ? new Date(p.fechaEntregaEst).toLocaleString('es-PE', { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' })
      : 'próximamente';
    const mensaje = this.whatsapp.mensaje('DEMORA', {
      cliente: p.clienteNombre ?? '', numero: String(p.numero), entrega: fecha,
    }, `Hola ${p.clienteNombre}, hemos actualizado la hora ${p.modalidad === 'Delivery' ? 'de entrega' : 'de recojo'} de tu pedido #${p.numero}. Nueva hora: ${fecha}. Disculpa las molestias.`);
    this.whatsapp.enviar(p.clienteCelular, mensaje);
  }

  // ---------- Delivery (convertir / editar destino) ----------
  abrirDestinoDelivery(p: Pedido) {
    this.direccionEntregaConversion = p.direccionEntrega ?? '';
    this.distritoEntregaConversion = p.distritoEntrega ?? '';
    this.referenciaEntregaConversion = p.referenciaEntrega ?? '';
    this.latitudEntregaConversion = p.latitudEntrega ?? null;
    this.longitudEntregaConversion = p.longitudEntrega ?? null;
    this.ubicacionConversionConfirmada = p.latitudEntrega != null && p.longitudEntrega != null;
    this.menuAcciones.set(false);
    this.modalDestinoDelivery.set(true);
  }

  cerrarDestinoDelivery() {
    if (!this.convirtiendoDelivery()) this.modalDestinoDelivery.set(false);
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
    const p = this.pedido();
    if (!p || this.convirtiendoDelivery()) return;
    if (!this.direccionEntregaConversion.trim() || !this.distritoEntregaConversion) {
      this.toast.advertencia('Completa la dirección exacta y el distrito de entrega.');
      return;
    }
    if (this.latitudEntregaConversion === null || this.longitudEntregaConversion === null || !this.ubicacionConversionConfirmada) {
      this.toast.advertencia('Confirma la dirección y el punto exacto en el mapa.');
      return;
    }
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
        this.toast.exito(p.modalidad === 'Delivery'
          ? `Destino del pedido #${p.numero} actualizado`
          : `Pedido #${p.numero} convertido a Delivery`);
        this.refrescar();
      },
      error: (err: HttpErrorResponse) => {
        this.convirtiendoDelivery.set(false);
        this.toast.desdeHttp(err, 'No se pudo convertir el pedido a Delivery.');
      }
    });
  }

  // ---------- Anular ----------
  abrirModalAnular() {
    this.motivoAnulacion = '';
    this.menuAcciones.set(false);
    this.modalAnular.set(true);
  }

  confirmarAnular() {
    const p = this.pedido();
    if (!p || this.motivoAnulacion.trim().length < 3) return;
    this.procesando.set(true);
    this.service.anular(p.id, this.motivoAnulacion.trim()).subscribe({
      next: () => {
        this.procesando.set(false);
        this.modalAnular.set(false);
        this.toast.advertencia(`Pedido #${p.numero} anulado`);
        this.refrescar();
      },
      error: (err: HttpErrorResponse) => {
        this.procesando.set(false);
        this.toast.desdeHttp(err, 'No se pudo anular el pedido.');
      }
    });
  }

  // ---------- Comprobantes / ticket ----------
  emitirComprobante(tipo: 'BOLETA' | 'FACTURA') {
    const p = this.pedido();
    if (!p || this.emitiendoComprobante()) return;
    this.menuAcciones.set(false);
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

  imprimirTicket(p: Pedido) {
    this.menuAcciones.set(false);
    window.open(`/ticket/${p.id}`, '_blank');
  }

  // ---------- Fotos ----------
  private liberarUrlsFotos() {
    for (const f of this.fotosPedido()) { if (f.url) URL.revokeObjectURL(f.url); }
  }

  private cargarFotos() {
    this.liberarUrlsFotos();
    this.fotosPedido.set([]);
    this.cargandoFotos.set(true);
    this.fotosSvc.listar(this.pedidoId).subscribe({
      next: fotos => {
        this.fotosPedido.set(fotos.map(f => ({ ...f, url: null })));
        this.cargandoFotos.set(false);
        for (const f of fotos) {
          this.fotosSvc.urlArchivo(this.pedidoId, f.id).subscribe({
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
    const p = this.pedido();
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
        next: () => { this.subiendoFoto.set(false); this.toast.exito('Foto agregada'); this.cargarFotos(); },
        error: (err: HttpErrorResponse) => { this.subiendoFoto.set(false); this.toast.desdeHttp(err, 'No se pudo subir la foto.'); }
      });
    } catch {
      this.subiendoFoto.set(false);
      this.toast.error('No se pudo procesar la imagen.');
    }
    input.value = '';
  }

  eliminarFoto(fotoId: number) {
    const p = this.pedido();
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

  // ---------- Presentación ----------
  tituloHistorial(h: PedidoHistorial): string {
    switch (h.estadoProceso) {
      case 'LISTO': return '✓ Listo para recojo';
      case 'ENTREGADO': return '📦 Entregado al cliente';
      case 'ANULADO': return '🚫 Pedido anulado';
      case 'PENDIENTE': return h.areaNombre ? `Ingreso · ${h.areaNombre}` : 'Ingreso de pedido';
      case 'EN_PROCESO':
      default: return h.areaNombre ?? 'Actualización';
    }
  }

  etiquetaEstado(estado: string): string {
    return ({
      'PENDIENTE': 'Pendiente',
      'EN_PROCESO': 'En proceso',
      'LISTO': 'Listo para entregar',
      'ENTREGADO': 'Entregado',
      'ANULADO': 'Anulado'
    } as Record<string, string>)[estado] ?? estado;
  }

  cerrarModales() {
    if (this.procesando()) return;
    this.modalPago.set(false);
    this.modalEntrega.set(false);
    this.modalItem.set(false);
    this.modalFecha.set(false);
    this.modalAnular.set(false);
    this.cerrarDestinoDelivery();
  }
}
