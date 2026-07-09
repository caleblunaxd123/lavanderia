import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { Pedido } from '../../core/models/models';
import { ConfiguracionService } from '../../core/services/configuracion.service';
import { PedidosService } from '../../core/services/pedidos.service';
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
