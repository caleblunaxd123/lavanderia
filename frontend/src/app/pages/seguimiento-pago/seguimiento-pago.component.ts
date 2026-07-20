import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { EstadoRuta, PagoPublicoService, SeguimientoPedido } from '../../core/services/pago-publico.service';
import { MapaSeguimientoComponent } from '../../shared/mapa-seguimiento/mapa-seguimiento.component';

declare const CulqiCheckout: any;

const INTERVALO_NORMAL_MS = 10_000;
const INTERVALO_EN_RUTA_MS = 5_000;

@Component({
  selector: 'app-seguimiento-pago',
  imports: [CommonModule, FormsModule, MapaSeguimientoComponent],
  templateUrl: './seguimiento-pago.component.html',
  styleUrl: './seguimiento-pago.component.scss'
})
export class SeguimientoPagoComponent implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly svc = inject(PagoPublicoService);

  private token = '';
  private culqiListo = false;
  private culqiCheckout: any | null = null;
  private timerId?: ReturnType<typeof setInterval>;
  private intervaloActualMs = INTERVALO_NORMAL_MS;
  private estadoRutaPrevio: EstadoRuta | null = null;
  private readonly alRecuperarFoco = () => this.cargar(true);
  private readonly alCambiarVisibilidad = () => {
    if (document.visibilityState === 'visible') this.cargar(true);
  };

  readonly cargando = signal(true);
  readonly error = signal<string | null>(null);
  readonly data = signal<SeguimientoPedido | null>(null);
  readonly procesando = signal(false);
  readonly mensajeCobro = signal<string | null>(null);

  readonly modalReprogramar = signal(false);
  readonly reprogramando = signal(false);
  readonly errorReprogramar = signal<string | null>(null);
  nuevaFecha = '';

  ngOnInit() {
    this.token = this.route.snapshot.paramMap.get('token') ?? '';
    this.cargar();
    this.reprogramarTimer(INTERVALO_NORMAL_MS);
    window.addEventListener('focus', this.alRecuperarFoco);
    document.addEventListener('visibilitychange', this.alCambiarVisibilidad);
  }

  ngOnDestroy() {
    if (this.timerId) clearInterval(this.timerId);
    window.removeEventListener('focus', this.alRecuperarFoco);
    document.removeEventListener('visibilitychange', this.alCambiarVisibilidad);
  }

  private reprogramarTimer(ms: number) {
    if (this.timerId && this.intervaloActualMs === ms) return;
    if (this.timerId) clearInterval(this.timerId);
    this.intervaloActualMs = ms;
    this.timerId = setInterval(() => this.cargar(true), ms);
  }

  cargar(silencioso = false) {
    if (!silencioso) {
      this.cargando.set(true);
      this.error.set(null);
    }
    this.svc.obtener(this.token).subscribe({
      next: d => {
        this.data.set(d);
        this.cargando.set(false);
        this.aplicarTema(d);
        this.procesarEstadoRuta(d);
        if (d.requierePago && d.publicKeyCulqi) this.cargarScriptCulqi(d.publicKeyCulqi);
      },
      error: (err: HttpErrorResponse) => {
        this.cargando.set(false);
        if (!silencioso) {
          this.error.set(err.status === 404
            ? 'Este link no es válido o ya expiró. Pide uno nuevo a la lavandería.'
            : 'No se pudo cargar la información de tu pedido.');
        }
      }
    });
  }

  abrirReprogramar() {
    this.errorReprogramar.set(null);
    this.nuevaFecha = '';
    this.modalReprogramar.set(true);
  }

  cerrarReprogramar() { this.modalReprogramar.set(false); }

  confirmarReprogramar() {
    if (!this.nuevaFecha) {
      this.errorReprogramar.set('Elige una fecha y hora.');
      return;
    }
    this.reprogramando.set(true);
    this.errorReprogramar.set(null);
    this.svc.reprogramar(this.token, new Date(this.nuevaFecha).toISOString()).subscribe({
      next: () => {
        this.reprogramando.set(false);
        this.modalReprogramar.set(false);
        this.cargar();
      },
      error: (err: HttpErrorResponse) => {
        this.reprogramando.set(false);
        this.errorReprogramar.set(err.error?.mensaje ?? 'No se pudo reprogramar el pedido.');
      }
    });
  }

  private aplicarTema(d: SeguimientoPedido) {
    document.documentElement.style.setProperty('--color-primario-cliente', d.colorPrimario || '#0b57d0');
    document.title = `${d.nombreNegocio} · Pedido #${d.numeroPedido}`;
  }

  private cargarScriptCulqi(publicKey: string) {
    if (this.culqiListo && typeof CulqiCheckout !== 'undefined') {
      this.configurarCulqi(publicKey);
      return;
    }

    const existente = document.getElementById('culqi-checkout-script');
    if (existente) {
      existente.addEventListener('load', () => this.configurarCulqi(publicKey), { once: true });
      existente.addEventListener('error', () => this.informarErrorCargaCulqi(), { once: true });
      return;
    }

    const script = document.createElement('script');
    script.id = 'culqi-checkout-script';
    script.src = 'https://js.culqi.com/checkout-js';
    script.onload = () => {
      this.culqiListo = true;
      this.configurarCulqi(publicKey);
    };
    script.onerror = () => this.informarErrorCargaCulqi();
    document.body.appendChild(script);
  }

  private informarErrorCargaCulqi() {
    this.culqiListo = false;
    this.culqiCheckout = null;
    this.mensajeCobro.set('No se pudo cargar la pasarela de pago. Revisa tu conexion e intenta nuevamente.');
  }

  private configurarCulqi(publicKey: string) {
    const d = this.data();
    if (!d || typeof CulqiCheckout === 'undefined') return;

    const admiteYape = d.saldo <= 2000;
    const metodosPago = {
      tarjeta: true,
      yape: admiteYape,
      billetera: false,
      bancaMovil: false,
      agente: false,
      cuotealo: false
    };
    const config = {
      settings: {
        title: d.nombreNegocio,
        currency: 'PEN',
        amount: Math.round(d.saldo * 100),
      },
      client: {},
      options: {
        lang: 'es',
        modal: true,
        installments: true,
        paymentMethods: metodosPago,
        paymentMethodsSort: Object.keys(metodosPago).filter(metodo => metodosPago[metodo as keyof typeof metodosPago])
      },
      appearance: {
        hiddenCulqiLogo: false,
        hiddenBannerContent: false,
        hiddenEmail: false,
        menuType: 'sidebar',
        buttonCardPayText: `Pagar S/ ${d.saldo.toFixed(2)}`
      }
    };

    this.culqiCheckout = new CulqiCheckout(publicKey, config);
    this.culqiCheckout.culqi = () => {
      if (this.culqiCheckout?.token) {
        this.confirmarPago(this.culqiCheckout.token.id, this.culqiCheckout.token.email);
        this.culqiCheckout.close();
      } else if (this.culqiCheckout?.error) {
        this.mensajeCobro.set(this.culqiCheckout.error.user_message ?? 'No se pudo procesar el pago.');
      }
    };
  }

  abrirPago() {
    if (!this.culqiCheckout) {
      this.mensajeCobro.set('La pasarela de pago aún está cargando. Intenta de nuevo en unos segundos.');
      return;
    }
    this.mensajeCobro.set(null);
    this.culqiCheckout.open();
  }

  metodosPagoDisponibles(): string {
    return (this.data()?.saldo ?? 0) <= 2000 ? 'tarjeta o Yape' : 'tarjeta';
  }

  abrirWhatsappNegocio() {
    const telefono = this.telefonoWhatsapp();
    if (!telefono) return;
    const d = this.data();
    const texto = encodeURIComponent(`Hola, necesito ayuda con mi pedido #${d?.numeroPedido ?? ''}.`);
    window.open(`https://wa.me/${telefono}?text=${texto}`, '_blank');
  }

  telefonoWhatsapp() {
    const telefono = this.data()?.telefonoNegocio ?? '';
    const soloDigitos = telefono.replace(/\D/g, '');
    return soloDigitos || null;
  }

  /** True cuando un pedido delivery está en su tramo final: listo y saliendo a ruta. */
  estaEnReparto(d: SeguimientoPedido): boolean {
    return d.modalidad === 'Delivery'
      && !d.anulado
      && d.pasos.some(p => p.codigo === 'LISTO' && p.actual);
  }

  /** El repartidor ya arrancó y podemos mostrar el mapa/estado en vivo. */
  enRutaVivo(d: SeguimientoPedido): boolean {
    return d.modalidad === 'Delivery' && !d.anulado
      && (d.estadoRuta === 'EN_RUTA' || d.estadoRuta === 'CERCA' || d.estadoRuta === 'LLEGO');
  }

  tituloRutaVivo(d: SeguimientoPedido): string {
    switch (d.estadoRuta) {
      case 'CERCA': return '¡Tu repartidor está cerca!';
      case 'LLEGO': return '¡Tu repartidor llegó!';
      default: return '¡Tu pedido va en camino!';
    }
  }

  subtituloRutaVivo(d: SeguimientoPedido): string {
    if (d.estadoRuta === 'LLEGO') return 'Ya está en la puerta. ¡Prepárate para recibirlo!';
    const dist = this.textoDistancia(d);
    if (dist && d.etaMinutos) return `A ${dist} · llega en ~${d.etaMinutos} min`;
    if (dist) return `A ${dist} de tu dirección`;
    return d.motorizadoNombre ? `Lo lleva ${d.motorizadoNombre}` : 'Sigue su recorrido en el mapa';
  }

  textoDistancia(d: SeguimientoPedido): string | null {
    const m = d.distanciaMetros;
    if (m == null) return null;
    return m >= 1000 ? `${(m / 1000).toFixed(1)} km` : `${m} m`;
  }

  /** Notifica al cliente en el navegador cuando el reparto cambia de hito (va en camino / cerca /
   *  llegó / entregado). Solo si dio permiso; nunca reclama permiso de forma intrusiva al abrir. */
  private procesarEstadoRuta(d: SeguimientoPedido) {
    const nuevo = d.estadoRuta;
    this.reprogramarTimer(this.enRutaVivo(d) ? INTERVALO_EN_RUTA_MS : INTERVALO_NORMAL_MS);

    // Al entrar en ruta por primera vez, pedimos permiso de notificación (una sola vez).
    if (this.enRutaVivo(d) && 'Notification' in window && Notification.permission === 'default') {
      Notification.requestPermission().catch(() => undefined);
    }

    const previo = this.estadoRutaPrevio;
    this.estadoRutaPrevio = nuevo;
    if (previo === null || previo === nuevo) return; // primera carga o sin cambio: no notificar

    const negocio = d.nombreNegocio;
    switch (nuevo) {
      case 'EN_RUTA': this.notificar('🛵 Tu pedido va en camino', `${negocio} salió a entregar tu pedido #${d.numeroPedido}.`); break;
      case 'CERCA': this.notificar('📍 Tu repartidor está cerca', 'Ya casi llega a tu dirección.'); break;
      case 'LLEGO': this.notificar('🎯 Tu repartidor llegó', 'Está en la puerta con tu pedido.'); break;
      case 'ENTREGADO': this.notificar('✅ Pedido entregado', '¡Gracias por tu preferencia!'); break;
    }
  }

  private notificar(titulo: string, cuerpo: string) {
    try {
      if ('Notification' in window && Notification.permission === 'granted') {
        new Notification(titulo, { body: cuerpo, icon: this.data()?.logoUrl ?? undefined });
      }
    } catch { /* algunos navegadores móviles limitan Notification fuera de un SW; se ignora */ }
  }

  telMotorizado() {
    const cel = this.data()?.motorizadoCelular ?? '';
    const soloDigitos = cel.replace(/\D/g, '');
    return soloDigitos || null;
  }

  urlMapaEntrega(d: SeguimientoPedido): string | null {
    if (d.latitudEntrega == null || d.longitudEntrega == null) return null;
    return `https://www.openstreetmap.org/?mlat=${d.latitudEntrega}&mlon=${d.longitudEntrega}#map=18/${d.latitudEntrega}/${d.longitudEntrega}`;
  }

  private confirmarPago(culqiTokenId: string, email: string) {
    this.procesando.set(true);
    this.svc.cobrar(this.token, culqiTokenId, email).subscribe({
      next: r => {
        this.procesando.set(false);
        this.mensajeCobro.set(r.mensaje ?? null);
        if (r.exito) this.cargar();
      },
      error: (err: HttpErrorResponse) => {
        this.procesando.set(false);
        this.mensajeCobro.set(err.error?.mensaje ?? 'No se pudo procesar el pago.');
      }
    });
  }
}
