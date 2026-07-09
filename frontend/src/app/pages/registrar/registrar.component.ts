import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AreaLavado, Cliente, Pedido, Servicio } from '../../core/models/models';
import { CatalogosService } from '../../core/services/catalogos.service';
import { ClientesService } from '../../core/services/clientes.service';
import { ConfiguracionService } from '../../core/services/configuracion.service';
import { PedidosService } from '../../core/services/pedidos.service';
import { PromocionValida } from '../../core/services/promociones.service';
import { ToastService } from '../../core/services/toast.service';
import { WhatsappService } from '../../core/services/whatsapp.service';
import { IconComponent } from '../../shared/icon/icon.component';

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
  imports: [CommonModule, FormsModule, IconComponent],
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

  // Paso 1 - Cliente
  clienteExistente: Cliente | null = null;
  nombre = '';
  direccion = '';
  celular = '';
  documentoIdentidad = '';
  documentoFiscal = '';
  modalidad: 'Tienda' | 'Delivery' = 'Tienda';
  buscandoCliente = signal(false);

  // Buscar cliente frecuente (por nombre, celular o DNI)
  campoBusquedaCliente: 'Nombre' | 'Celular' | 'DNI' = 'Nombre';
  textoBusquedaCliente = '';
  readonly resultadosBusquedaCliente = signal<Cliente[]>([]);
  private busquedaClienteTimerId?: ReturnType<typeof setTimeout>;

  // Paso 2 - Items
  servicioSeleccionadoId: number | '' = '';
  items = signal<ItemAgregado[]>([]);

  // Paso 3 - Entrega
  areaInicialId: number | null = null;
  fechaEntregaValor = signal<string>(this.formatoLocal(new Date(Date.now() + 2.5 * 60 * 60 * 1000)));
  fechaEntregaEstimada = computed(() => new Date(this.fechaEntregaValor()));

  private formatoLocal(d: Date): string {
    // yyyy-MM-ddTHH:mm para <input type="datetime-local">
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

  // Paso 4 - Descuento / Urgente
  aplicaDescuento = signal(false);
  descuentoPorcentaje = signal(10);
  esUrgente = signal(false);
  recargoUrgentePorcentaje = signal(20);

  // Código de promoción
  codigoPromo = '';
  readonly validandoPromo = signal(false);
  readonly promoAplicada = signal<PromocionValida | null>(null);
  readonly errorPromo = signal<string | null>(null);

  // Paso 5 - Pago
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

  totalNeto = computed(() => this.subtotal() - this.descuentoMonto() + this.recargoUrgenteMonto());

  // Redondeo a los 10 centimos mas cercanos (no circulan monedas de 1, 2 y 5 centimos en Peru)
  totalFinal = computed(() => Math.round(this.totalNeto() * 10) / 10);
  redondeo = computed(() => Math.round((this.totalFinal() - this.totalNeto()) * 100) / 100);

  registrado = signal(false);
  pedidoCreado = signal<Pedido | null>(null);
  registrando = signal(false);

  ngOnInit() {
    this.whatsapp.cargar();
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

  seleccionarClienteFrecuente(c: Cliente) {
    this.clienteExistente = c;
    this.nombre = c.nombre;
    this.celular = c.celular ?? '';
    this.direccion = c.direccion ?? '';
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
      && this.items().length > 0
      && this.totalFinal() > 0
      && !this.registrando();
  }

  registrarOrden() {
    if (!this.puedeRegistrar) return;

    this.registrando.set(true);

    const payload = {
      clienteId: this.clienteExistente?.id,
      clienteNuevo: this.clienteExistente ? undefined : {
        nombre: this.nombre.trim(),
        celular: this.celular || null,
        dni: this.documentoIdentidad || null,
        documentoFiscal: this.documentoFiscal || null,
        direccion: this.direccion || null,
      },
      modalidad: this.modalidad,
      items: this.items().map(i => ({
        servicioId: i.servicioId,
        cantidad: i.cantidad,
        precioUnit: i.precio,
        total: i.precio * i.cantidad,
        descripcion: i.descripcion.trim() || null
      })),
      descuentoPct: this.aplicaDescuento() ? this.descuentoPorcentaje() : 0,
      esUrgente: this.esUrgente(),
      recargoUrgentePct: this.recargoUrgentePorcentaje(),
      montoPagado: this.montoPagado,
      metodoPagoInicial: this.metodoPagoInicial,
      fechaEntregaEst: new Date(this.fechaEntregaValor()).toISOString(),
      observaciones: null,
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
        this.toast.error(err.error?.mensaje ?? 'No se pudo registrar el pedido.');
      }
    });
  }

  irAPedidos() {
    this.router.navigate(['/pedidos']);
  }

  imprimirTicket() {
    const p = this.pedidoCreado();
    if (!p) return;
    // Abre en nueva pestaña para no perder el estado del wizard
    window.open(`/ticket/${p.id}`, '_blank');
  }

  enviarWhatsapp() {
    const p = this.pedidoCreado();
    if (!p || !p.clienteCelular) return;
    const cliente = p.clienteNombre ?? '';
    const entrega = p.fechaEntregaEst
      ? new Date(p.fechaEntregaEst).toLocaleString('es-PE', { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' })
      : 'por confirmar';
    const mensaje = this.whatsapp.mensaje('INGRESO', {
      cliente,
      numero: String(p.numero),
      negocio: this.config.configuracion().nombreNegocio,
      total: p.total.toFixed(2),
      entrega,
    }, `Hola ${cliente}, registramos tu pedido #${p.numero} por un total de S/ ${p.total.toFixed(2)}. ¡Gracias por tu preferencia!`);
    this.whatsapp.enviar(p.clienteCelular, mensaje);
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
    this.textoBusquedaCliente = '';
    this.resultadosBusquedaCliente.set([]);
    this.items.set([]);
    this.aplicaDescuento.set(false);
    this.esUrgente.set(false);
    this.montoPagado = 0;
    this.metodoPagoInicial = 'EFECTIVO';
  }
}
