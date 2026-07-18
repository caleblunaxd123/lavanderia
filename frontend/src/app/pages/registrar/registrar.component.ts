import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { DISTRITOS_LIMA_CALLAO } from '../../core/constants/distritos-lima-callao';
import { AreaLavado, Cliente, ModalidadPedido, Pedido, Servicio } from '../../core/models/models';
import { CatalogosService } from '../../core/services/catalogos.service';
import { ClientesService } from '../../core/services/clientes.service';
import { ConfiguracionService } from '../../core/services/configuracion.service';
import { PedidosService } from '../../core/services/pedidos.service';
import { PromocionValida } from '../../core/services/promociones.service';
import { ToastService } from '../../core/services/toast.service';
import { WhatsappService } from '../../core/services/whatsapp.service';
import { IconComponent } from '../../shared/icon/icon.component';
import { MapaUbicacionComponent, UbicacionMapa } from '../../shared/mapa-ubicacion/mapa-ubicacion.component';

interface ItemAgregado {
  servicioId: number;
  nombre: string;
  precio: number;
  unidad: string;
  cantidad: number;
  descripcion: string;
}

@Component({
  selector: 'app-registrar',
  imports: [CommonModule, FormsModule, IconComponent, MapaUbicacionComponent],
  templateUrl: './registrar.component.html',
  styleUrl: './registrar.component.scss'
})
export class RegistrarComponent implements OnInit {
  private readonly catalogosSvc = inject(CatalogosService);
  private readonly clientesSvc = inject(ClientesService);
  private readonly pedidosSvc = inject(PedidosService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  private readonly whatsapp = inject(WhatsappService);
  private readonly config = inject(ConfiguracionService);

  readonly catalogo = signal<Servicio[]>([]);
  readonly areas = signal<AreaLavado[]>([]);
  readonly cargando = signal(false);
  readonly siguienteNumero = signal<number | null>(null);

  clienteExistente: Cliente | null = null;
  nombre = '';
  direccion = '';
  celular = '';
  documentoIdentidad = '';
  documentoFiscal = '';
  modalidad: ModalidadPedido = 'Tienda';
  readonly distritos = DISTRITOS_LIMA_CALLAO;
  direccionEntrega = '';
  distritoEntrega = '';
  referenciaEntrega = '';
  latitudEntrega: number | null = null;
  longitudEntrega: number | null = null;
  costoDeliveryPedido = 0;
  observacionesPedido = '';
  buscandoCliente = signal(false);

  campoBusquedaCliente: 'Nombre' | 'Celular' | 'DNI' = 'Nombre';
  textoBusquedaCliente = '';
  readonly resultadosBusquedaCliente = signal<Cliente[]>([]);
  private busquedaClienteTimerId?: ReturnType<typeof setTimeout>;

  servicioSeleccionadoId: number | '' = '';
  items = signal<ItemAgregado[]>([]);

  areaInicialId: number | null = null;
  fechaEntregaValor = signal<string>(this.formatoLocal(new Date(Date.now() + 2.5 * 60 * 60 * 1000)));
  fechaEntregaEstimada = computed(() => new Date(this.fechaEntregaValor()));

  aplicaDescuento = signal(false);
  descuentoPorcentaje = signal(10);
  esUrgente = signal(false);
  recargoUrgentePorcentaje = signal(20);

  // Canje de puntos (fidelización)
  puntosACanjear = signal(0);
  get valorPuntoCanje(): number { return this.config.configuracion().valorPuntoCanje ?? 0; }
  get maxDescuentoPct(): number { return this.config.configuracion().maxDescuentoPct ?? 0; }
  get puntosDisponibles(): number { return this.clienteExistente?.puntos ?? 0; }
  get canjeHabilitado(): boolean { return this.valorPuntoCanje > 0 && this.puntosDisponibles > 0; }
  get negocioConfig() { return this.config.configuracion(); }

  codigoPromo = '';
  readonly validandoPromo = signal(false);
  readonly promoAplicada = signal<PromocionValida | null>(null);
  readonly errorPromo = signal<string | null>(null);

  montoPagado = 0;
  metodoPagoInicial: 'EFECTIVO' | 'YAPE' | 'PLIN' | 'TRANSFERENCIA' | 'POS' = 'EFECTIVO';
  notificarWhatsapp = true;

  subtotal = computed(() =>
    this.items().reduce((acc, it) => acc + it.precio * it.cantidad, 0)
  );

  descuentoMonto = computed(() =>
    this.aplicaDescuento() ? this.subtotal() * (this.descuentoPorcentaje() / 100) : 0
  );

  recargoUrgenteMonto = computed(() =>
    this.esUrgente() ? this.subtotal() * (this.recargoUrgentePorcentaje() / 100) : 0
  );

  descuentoPuntosMonto = computed(() => {
    const bruto = this.puntosACanjear() * this.valorPuntoCanje;
    const disponible = Math.max(0, this.subtotal() - this.descuentoMonto());
    return Math.min(bruto, disponible);
  });

  totalNeto = computed(() => this.subtotal() - this.descuentoMonto() - this.descuentoPuntosMonto() + this.recargoUrgenteMonto());

  setPuntosACanjear(v: number) {
    const n = Math.floor(Number(v) || 0);
    const maxPorMonto = this.valorPuntoCanje > 0
      ? Math.floor(Math.max(0, this.subtotal() - this.descuentoMonto()) / this.valorPuntoCanje)
      : 0;
    const max = Math.min(this.puntosDisponibles, maxPorMonto);
    this.puntosACanjear.set(Math.max(0, Math.min(n, max)));
  }

  setDescuentoPct(v: number) {
    let n = Number(v) || 0;
    if (this.maxDescuentoPct > 0) n = Math.min(n, this.maxDescuentoPct);
    this.descuentoPorcentaje.set(Math.max(0, Math.min(100, n)));
  }
  totalFinal = computed(() => Math.round(this.totalNeto() * 10) / 10);
  redondeo = computed(() => Math.round((this.totalFinal() - this.totalNeto()) * 100) / 100);

  registrado = signal(false);
  pedidoCreado = signal<Pedido | null>(null);
  registrando = signal(false);

  ngOnInit() {
    this.whatsapp.cargar();
    this.config.cargar().subscribe({ error: () => {} }); // asegura valorPuntoCanje / maxDescuentoPct frescos
    this.pedidosSvc.siguienteNumero().subscribe({
      next: n => this.siguienteNumero.set(n),
      error: () => {}
    });
    this.cargando.set(true);
    this.catalogosSvc.servicios().subscribe({
      next: s => this.catalogo.set(s),
      error: () => this.toast.error('No se pudo cargar el catálogo de servicios.')
    });
    this.catalogosSvc.areasLavado().subscribe({
      next: a => {
        this.areas.set(a);
        this.areaInicialId = a[0]?.id ?? null;
        this.cargando.set(false);
      },
      error: () => this.cargando.set(false)
    });
  }

  private formatoLocal(d: Date): string {
    const pad = (n: number) => String(n).padStart(2, '0');
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
  }

  presetEntrega(horas: number) {
    this.fechaEntregaValor.set(this.formatoLocal(new Date(Date.now() + horas * 60 * 60 * 1000)));
  }

  presetMananaHora(hora: number) {
    const d = new Date();
    d.setDate(d.getDate() + 1);
    d.setHours(hora, 0, 0, 0);
    this.fechaEntregaValor.set(this.formatoLocal(d));
  }

  onBuscarClienteInput(texto: string) {
    this.textoBusquedaCliente = texto;
    if (this.busquedaClienteTimerId) clearTimeout(this.busquedaClienteTimerId);
    if (texto.trim().length < 2) {
      this.resultadosBusquedaCliente.set([]);
      return;
    }
    this.busquedaClienteTimerId = setTimeout(() => this.ejecutarBusquedaCliente(), 300);
  }

  private ejecutarBusquedaCliente() {
    const texto = this.textoBusquedaCliente.trim();
    if (texto.length < 2) return;
    this.buscandoCliente.set(true);
    this.clientesSvc.buscar(texto, this.campoBusquedaCliente.toLowerCase(), 8).subscribe({
      next: list => { this.resultadosBusquedaCliente.set(list); this.buscandoCliente.set(false); },
      error: () => this.buscandoCliente.set(false)
    });
  }

  cambiarModalidad(m: ModalidadPedido) {
    this.modalidad = m;
    if (m === 'Delivery' && !this.direccionEntrega.trim() && this.direccion.trim()) {
      this.direccionEntrega = this.direccion.trim();
    }
    const idDelivery = this.config.configuracion().servicioDeliveryId;
    if (!idDelivery) return;

    if (this.esDomicilio()) {
      if (this.costoDeliveryPedido <= 0) {
        this.costoDeliveryPedido = Number(this.config.configuracion().costoDelivery) || 0;
      }
      const yaEsta = this.items().some(i => i.servicioId === idDelivery);
      if (!yaEsta) {
        this.items.update(list => [...list, {
          servicioId: idDelivery,
          nombre: 'Servicio a domicilio',
          precio: this.costoDeliveryPedido,
          unidad: 'Unidad',
          cantidad: 1,
          descripcion: ''
        }]);
      } else {
        this.actualizarCostoDelivery(this.costoDeliveryPedido);
      }
    } else {
      this.items.update(list => list.filter(i => i.servicioId !== idDelivery));
    }
  }

  actualizarCostoDelivery(valor: number) {
    const costo = Math.max(0, Math.min(10000, Number(valor) || 0));
    this.costoDeliveryPedido = costo;
    const idDelivery = this.config.configuracion().servicioDeliveryId;
    if (idDelivery) {
      this.items.update(list => list.map(i => i.servicioId === idDelivery ? { ...i, precio: costo } : i));
    }
  }

  esDomicilio() {
    return this.modalidad === 'Recojo' || this.modalidad === 'Delivery';
  }

  tieneServicioLavanderia() {
    const idDelivery = this.config.configuracion().servicioDeliveryId;
    return this.items().some(item => !idDelivery || item.servicioId !== idDelivery);
  }

  actualizarUbicacion(ubicacion: UbicacionMapa | null) {
    this.latitudEntrega = ubicacion?.latitud ?? null;
    this.longitudEntrega = ubicacion?.longitud ?? null;
    // El mapa manda: al elegir/mover el pin se actualizan dirección y distrito para que
    // no queden inconsistentes con el punto. Solo sobrescribe cuando el mapa resolvió un valor.
    if (ubicacion?.direccion) this.direccionEntrega = ubicacion.direccion;
    if (ubicacion?.distrito) this.distritoEntrega = ubicacion.distrito;
  }

  descripcionPaso3() {
    return this.modalidad === 'Delivery' ? '¿Cuándo entregar?'
      : this.modalidad === 'Recojo' ? '¿Cuándo estará listo?'
      : '¿Cuándo estará listo para recojo?';
  }

  etiquetaFechaProgramada() {
    return this.modalidad === 'Delivery' ? 'Fecha y hora de entrega'
      : this.esDomicilio() ? 'Fecha y hora estimada'
      : 'Fecha y hora de recojo';
  }

  seleccionarClienteFrecuente(c: Cliente) {
    this.clienteExistente = c;
    this.nombre = c.nombre;
    this.celular = c.celular ?? '';
    this.direccion = c.direccion ?? '';
    if (this.modalidad === 'Delivery') {
      this.direccionEntrega = c.direccion ?? '';
      this.distritoEntrega = '';
      this.referenciaEntrega = '';
      this.latitudEntrega = null;
      this.longitudEntrega = null;
    }
    this.documentoIdentidad = c.dni ?? '';
    this.documentoFiscal = c.documentoFiscal ?? '';
    this.resultadosBusquedaCliente.set([]);
    this.textoBusquedaCliente = '';
    this.toast.info(`Cliente seleccionado: ${c.nombre}`);
  }

  quitarClienteSeleccionado() {
    this.clienteExistente = null;
    this.nombre = '';
    this.celular = '';
    this.direccion = '';
    this.documentoIdentidad = '';
    this.documentoFiscal = '';
    this.direccionEntrega = '';
    this.distritoEntrega = '';
    this.referenciaEntrega = '';
    this.latitudEntrega = null;
    this.longitudEntrega = null;
    this.puntosACanjear.set(0); // los puntos pertenecen al cliente seleccionado
  }

  agregarItem() {
    if (!this.servicioSeleccionadoId) return;
    const servicio = this.catalogo().find(s => s.id === this.servicioSeleccionadoId);
    if (!servicio) return;

    const existente = this.items().find(i => i.servicioId === servicio.id);
    if (existente) {
      this.items.update(list =>
        list.map(i => i.servicioId === servicio.id ? { ...i, cantidad: i.cantidad + 1 } : i)
      );
    } else {
      this.items.update(list => [...list, {
        servicioId: servicio.id,
        nombre: servicio.nombre,
        precio: servicio.precio,
        unidad: servicio.unidad,
        cantidad: 1,
        descripcion: ''
      }]);
    }
    this.servicioSeleccionadoId = '';
  }

  validarPromo() {
    const codigo = this.codigoPromo.trim();
    if (!codigo) return;
    this.validandoPromo.set(true);
    this.errorPromo.set(null);
    this.pedidosSvc.validarCodigoPromocion(codigo).subscribe({
      next: promo => {
        this.validandoPromo.set(false);

        const cantidadAplicable = promo.servicioId
          ? this.items().filter(i => i.servicioId === promo.servicioId).reduce((acc, i) => acc + i.cantidad, 0)
          : this.items().reduce((acc, i) => acc + i.cantidad, 0);

        if (cantidadAplicable < promo.cantidadMinima) {
          this.errorPromo.set(`Esta promoción requiere una cantidad mínima de ${promo.cantidadMinima}.`);
          return;
        }

        this.promoAplicada.set(promo);
        this.aplicaDescuento.set(true);
        if (promo.descuentoPct) {
          this.descuentoPorcentaje.set(promo.descuentoPct);
        } else if (promo.descuentoMonto && this.subtotal() > 0) {
          this.descuentoPorcentaje.set(Math.min(100, Math.round((promo.descuentoMonto / this.subtotal()) * 10000) / 100));
        }
        this.toast.exito(`Promoción aplicada: ${promo.descripcion}`);
      },
      error: (err: HttpErrorResponse) => {
        this.validandoPromo.set(false);
        this.errorPromo.set(err.error?.mensaje ?? 'Código no válido.');
      }
    });
  }

  quitarPromo() {
    this.promoAplicada.set(null);
    this.codigoPromo = '';
    this.errorPromo.set(null);
    this.aplicaDescuento.set(false);
    this.descuentoPorcentaje.set(10);
  }

  quitarItem(id: number) {
    this.items.update(list => list.filter(i => i.servicioId !== id));
  }

  cambiarCantidad(id: number, cantidad: number) {
    if (cantidad < 0.1) return;
    this.items.update(list =>
      list.map(i => i.servicioId === id ? { ...i, cantidad } : i)
    );
  }

  cambiarDescripcion(id: number, descripcion: string) {
    this.items.update(list =>
      list.map(i => i.servicioId === id ? { ...i, descripcion } : i)
    );
  }

  get puedeRegistrar(): boolean {
    return this.nombre.trim().length > 0
      && this.celular.trim().length > 0
      && (this.modalidad !== 'Recojo' || this.direccion.trim().length > 0)
      && (this.modalidad !== 'Delivery' || (
        this.direccionEntrega.trim().length > 0 && this.distritoEntrega.trim().length > 0
      ))
      && this.tieneServicioLavanderia()
      && this.totalFinal() > 0
      && !this.registrando();
  }

  get validacionPedido(): string | null {
    if (!this.nombre.trim()) return 'Indica el nombre del cliente.';
    if (!this.celular.trim()) return 'Indica un celular de contacto.';
    if (this.modalidad === 'Recojo' && !this.direccion.trim()) return 'Indica la dirección donde se recogerá el pedido.';
    if (this.modalidad === 'Delivery' && !this.direccionEntrega.trim()) return 'Indica la dirección exacta de entrega.';
    if (this.modalidad === 'Delivery' && !this.distritoEntrega.trim()) return 'Selecciona el distrito de entrega.';
    if (!this.tieneServicioLavanderia()) return 'Agrega al menos un servicio de lavandería; la tarifa de delivery no cuenta como servicio.';
    if (this.totalFinal() <= 0) return 'El total del pedido debe ser mayor a cero.';
    return null;
  }

  registrarOrden() {
    if (!this.puedeRegistrar) return;

    this.registrando.set(true);

    const payload = {
      clienteId: this.clienteExistente?.id,
      clienteNuevo: {
        nombre: this.nombre.trim(),
        celular: this.celular || null,
        dni: this.documentoIdentidad || null,
        documentoFiscal: this.documentoFiscal || null,
        direccion: this.direccion || null,
      },
      modalidad: this.modalidad,
      direccionEntrega: this.modalidad === 'Delivery' ? this.direccionEntrega.trim() : null,
      distritoEntrega: this.modalidad === 'Delivery' ? this.distritoEntrega : null,
      referenciaEntrega: this.modalidad === 'Delivery' ? (this.referenciaEntrega.trim() || null) : null,
      latitudEntrega: this.modalidad === 'Delivery' ? this.latitudEntrega : null,
      longitudEntrega: this.modalidad === 'Delivery' ? this.longitudEntrega : null,
      items: this.items().map(i => ({
        servicioId: i.servicioId,
        cantidad: i.cantidad,
        precioUnit: i.precio,
        total: i.precio * i.cantidad,
        descripcion: i.descripcion.trim() || null
      })),
      descuentoPct: this.aplicaDescuento() ? this.descuentoPorcentaje() : 0,
      puntosACanjear: this.puntosACanjear() > 0 ? this.puntosACanjear() : null,
      esUrgente: this.esUrgente(),
      recargoUrgentePct: this.recargoUrgentePorcentaje(),
      costoDelivery: this.esDomicilio() ? this.costoDeliveryPedido : null,
      montoPagado: this.montoPagado,
      metodoPagoInicial: this.metodoPagoInicial,
      fechaEntregaEst: new Date(this.fechaEntregaValor()).toISOString(),
      observaciones: this.observacionesPedido.trim() || null,
      areaInicialId: this.areaInicialId,
    };

    this.pedidosSvc.crear(payload).subscribe({
      next: p => {
        this.registrando.set(false);
        this.registrado.set(true);
        this.pedidoCreado.set(p);
        this.toast.exito(`Pedido #${p.numero} registrado`);
      },
      error: (err: HttpErrorResponse) => {
        this.registrando.set(false);
        this.toast.desdeHttp(err, 'No se pudo registrar el pedido.');
      }
    });
  }

  irAPedidos() {
    this.router.navigate(['/pedidos']);
  }

  imprimirTicket() {
    const p = this.pedidoCreado();
    if (!p) return;
    window.open(`/ticket/${p.id}`, '_blank');
  }

  enviarWhatsapp() {
    const p = this.pedidoCreado();
    if (!p || !p.clienteCelular) return;

    const enviar = (link?: string) => {
      const mensaje = this.whatsapp.mensajeIngreso(p, this.config.configuracion(), link);
      this.whatsapp.enviar(p.clienteCelular!, mensaje);
    };

    if (this.esPedidoDomicilio(p)) {
      this.pedidosSvc.linkSeguimiento(p.id).subscribe({
        next: ({ token }) => enviar(`${window.location.origin}/seguimiento/${token}`),
        error: () => enviar()
      });
      return;
    }

    enviar();
  }

  nuevaOrden() {
    this.registrado.set(false);
    this.pedidoCreado.set(null);
    this.clienteExistente = null;
    this.nombre = '';
    this.direccion = '';
    this.celular = '';
    this.documentoIdentidad = '';
    this.documentoFiscal = '';
    this.observacionesPedido = '';
    this.textoBusquedaCliente = '';
    this.resultadosBusquedaCliente.set([]);
    this.items.set([]);
    this.aplicaDescuento.set(false);
    this.esUrgente.set(false);
    this.puntosACanjear.set(0);
    this.montoPagado = 0;
    this.metodoPagoInicial = 'EFECTIVO';
    this.modalidad = 'Tienda';
    this.direccionEntrega = '';
    this.distritoEntrega = '';
    this.referenciaEntrega = '';
    this.latitudEntrega = null;
    this.longitudEntrega = null;
    this.costoDeliveryPedido = 0;
  }

  private esPedidoDomicilio(p: Pedido) {
    return p.modalidad === 'Recojo' || p.modalidad === 'Delivery';
  }
}
