import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, effect, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import JsBarcode from 'jsbarcode';
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
  readonly celularEnvio = signal('');
  readonly generandoImagen = signal(false);
  readonly generandoPdf = signal(false);

  readonly negocio = computed(() => this.config.configuracion());

  readonly marcaCorta = computed(() =>
    (this.negocio().nombreNegocio || 'Lavixa').replace(/^lavander[ií]a\s+/i, '').trim().toUpperCase()
  );

  readonly condicionesLista = computed(() =>
    (this.negocio().condicionesServicio ?? '')
      .split('\n')
      .map(l => l.trim().replace(/^\d+\.\s*/, ''))
      .filter(l => l.length > 0)
  );

  readonly saldo = computed(() => {
    const p = this.pedido();
    return p ? Math.max(0, p.total - p.montoPagado) : 0;
  });

  constructor() {
    effect(() => {
      const p = this.pedido();
      if (p) this.renderizarCodigoBarrasCuandoExista(p.numero);
    });
  }

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
        this.celularEnvio.set(p.clienteCelular ?? '');
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

  // Permite escanear el N° de pedido con un lector de codigo de barras USB en vez de
  // teclearlo en la busqueda rapida del listado de pedidos (esos lectores solo "tipean"
  // el numero + Enter, como un teclado — no requieren cambios adicionales en el sistema).
  //
  // El <svg> vive dentro de un @if(pedido()) del template, asi que no hay garantia de que
  // ya este en el DOM en el mismo tick en que el signal se actualiza. Reintenta en varios
  // frames en vez de asumir un timing exacto de Angular.
  private renderizarCodigoBarrasCuandoExista(numero: number, intentos = 20) {
    const el = document.getElementById('ticket-barcode');
    if (!el) {
      if (intentos > 0) requestAnimationFrame(() => this.renderizarCodigoBarrasCuandoExista(numero, intentos - 1));
      return;
    }
    try {
      JsBarcode(el, String(numero), {
        format: 'CODE128',
        displayValue: true,
        fontSize: 14,
        height: 40,
        margin: 4
      });
    } catch {
      // Si el numero no es codificable (no debería pasar), simplemente no se muestra.
    }
  }

  enviarWhatsapp() {
    const p = this.pedido();
    if (!p) return;
    const celular = this.celularEnvio().trim();
    if (!celular) {
      this.error.set('Ingresa un celular para enviar el mensaje.');
      return;
    }

    const enviar = (link?: string) =>
      this.whatsapp.enviar(celular, this.whatsapp.mensajeIngreso(p, this.negocio(), link));

    if (p.modalidad === 'Recojo' || p.modalidad === 'Delivery') {
      this.service.linkSeguimiento(p.id).subscribe({ next: ({ token }) => enviar(`${window.location.origin}/seguimiento/${token}`), error: () => enviar() });
    } else {
      enviar();
    }
  }

  async compartirPdfWhatsapp() {
    const p = this.pedido();
    if (!p) return;
    const celular = this.celularEnvio().trim();
    if (!celular) {
      this.error.set('Ingresa un celular para compartir el PDF.');
      return;
    }

    this.generandoPdf.set(true);
    try {
      const blob = await this.generarPdfTicket();
      const file = new File([blob], `ticket-${p.numero}.pdf`, { type: 'application/pdf' });
      const nav = navigator as Navigator & {
        canShare?: (data: ShareData) => boolean;
        share?: (data: ShareData) => Promise<void>;
      };
      const shareData: ShareData = {
        files: [file],
        title: `Ticket ${p.numero}`,
        text: `Ticket #${p.numero} - ${this.negocio().nombreNegocio}`
      };

      if (nav.share && (!nav.canShare || nav.canShare(shareData))) {
        await nav.share(shareData);
        return;
      }

      this.descargarBlob(blob, `ticket-${p.numero}.pdf`);
      this.enviarWhatsapp();
    } catch {
      this.error.set('No se pudo generar el PDF del ticket.');
    } finally {
      this.generandoPdf.set(false);
    }
  }

  // No existe forma de adjuntar un archivo directo al mensaje de WhatsApp desde la web
  // (los enlaces wa.me solo prellenan texto). Como alternativa se descarga el ticket como
  // imagen para que el usuario la adjunte manualmente en WhatsApp.
  async descargarImagen() {
    const p = this.pedido();
    if (!p) return;

    this.generandoImagen.set(true);
    try {
      const canvas = await this.generarCanvasTicket();
      const url = canvas.toDataURL('image/png');
      const a = document.createElement('a');
      a.href = url;
      a.download = `ticket-${p.numero}.png`;
      a.click();
    } catch {
      this.error.set('No se pudo generar la imagen del ticket.');
    } finally {
      this.generandoImagen.set(false);
    }
  }

  cerrar() { window.close(); }

  private async generarCanvasTicket() {
    const el = document.querySelector('.ticket') as HTMLElement | null;
    if (!el) throw new Error('Ticket no encontrado.');
    const html2canvas = (await import('html2canvas')).default;
    return await html2canvas(el, { backgroundColor: '#ffffff', scale: 2 });
  }

  private async generarPdfTicket() {
    const canvas = await this.generarCanvasTicket();
    const { jsPDF } = await import('jspdf');
    const anchoMm = this.negocio().anchoTicketMm || 80;
    const altoMm = Math.max(1, (canvas.height * anchoMm) / canvas.width);
    const pdf = new jsPDF({
      orientation: 'portrait',
      unit: 'mm',
      format: [anchoMm, altoMm]
    });

    pdf.addImage(canvas.toDataURL('image/png'), 'PNG', 0, 0, anchoMm, altoMm);
    return pdf.output('blob');
  }

  private descargarBlob(blob: Blob, nombre: string) {
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = nombre;
    a.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }

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
