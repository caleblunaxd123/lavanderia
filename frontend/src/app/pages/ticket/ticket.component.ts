import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { Pedido } from '../../core/models/models';
import { ConfiguracionService } from '../../core/services/configuracion.service';
import { PedidosService } from '../../core/services/pedidos.service';
import { WhatsappService } from '../../core/services/whatsapp.service';
import { IconComponent } from '../../shared/icon/icon.component';

@Component({
  selector: 'app-ticket',
  imports: [CommonModule, IconComponent],
  templateUrl: './ticket.component.html',
  styleUrl: './ticket.component.scss'
})
export class TicketComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly service = inject(PedidosService);
  private readonly config = inject(ConfiguracionService);
  private readonly whatsapp = inject(WhatsappService);

  readonly pedido = signal<Pedido | null>(null);
  readonly error = signal<string | null>(null);
  readonly cargando = signal(true);

  readonly tipoTicket = signal<'CLIENTE' | 'PRODUCCION'>('CLIENTE');
  readonly mostrarDescripcion = signal(false);

  readonly negocio = computed(() => this.config.configuracion());

  readonly condicionesLista = computed(() =>
    (this.negocio().condicionesServicio ?? '')
      .split('\n')
      .map(l => l.trim())
      .filter(l => l.length > 0)
  );

  readonly saldo = computed(() => {
    const p = this.pedido();
    return p ? Math.max(0, p.total - p.montoPagado) : 0;
  });

  ngOnInit() {
    // Ancho de pagina segun configuracion (58 o 80mm)
    const ancho = this.negocio().anchoTicketMm || 80;
    this.aplicarAncho(ancho);
    this.whatsapp.cargar();

    const id = Number(this.route.snapshot.paramMap.get('id'));
    if (!id) {
      this.error.set('ID de pedido inválido.');
      this.cargando.set(false);
      return;
    }

    this.service.obtener(id).subscribe({
      next: p => {
        this.pedido.set(p);
        this.cargando.set(false);
        // Auto lanzar el dialogo de imprimir despues de que se pinte todo
        setTimeout(() => this.imprimir(), 400);
      },
      error: () => {
        this.error.set('No se pudo cargar el pedido.');
        this.cargando.set(false);
      }
    });
  }

  imprimir() { window.print(); }

  enviarWhatsapp() {
    const p = this.pedido();
    if (!p) return;
    if (!p.clienteCelular) {
      this.error.set('Este cliente no tiene celular registrado.');
      return;
    }

    const cliente = p.clienteNombre ?? 'cliente';
    const numero = String(p.numero);
    const entrega = p.fechaEntregaEst
      ? new Date(p.fechaEntregaEst).toLocaleString('es-PE')
      : 'por confirmar';
    const saldoTexto = this.saldo() > 0
      ? `Saldo pendiente: S/ ${this.saldo().toFixed(2)}`
      : 'Pago completo';
    const fallback = `Hola ${cliente}, te compartimos el resumen de tu pedido #${numero} en ${this.negocio().nombreNegocio}. Total: S/ ${p.total.toFixed(2)}. ${saldoTexto}. Entrega: ${entrega}.`;

    const mensaje = this.whatsapp.mensaje('INGRESO', {
      cliente,
      numero,
      negocio: this.negocio().nombreNegocio,
      total: p.total.toFixed(2),
      saldo: this.saldo().toFixed(2),
      entrega
    }, fallback);

    this.whatsapp.enviar(p.clienteCelular, mensaje);
  }

  cerrar() { window.close(); }

  private aplicarAncho(mm: number) {
    // Inyectar la regla @page dinamica
    const styleId = 'ticket-page-size';
    document.getElementById(styleId)?.remove();
    const style = document.createElement('style');
    style.id = styleId;
    style.textContent = `@page { size: ${mm}mm auto; margin: 0; }`;
    document.head.appendChild(style);
  }
}
