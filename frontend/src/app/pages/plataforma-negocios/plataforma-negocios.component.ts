import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { NegocioResumen, PlataformaResumen } from '../../core/models/models';
import { NegociosPlataformaService } from '../../core/services/negocios-plataforma.service';
import { ToastService } from '../../core/services/toast.service';
import { EmptyStateComponent } from '../../shared/empty-state/empty-state.component';
import { IconComponent } from '../../shared/icon/icon.component';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';

type FiltroEstado = 'todas' | 'activas' | 'suspendidas' | 'por-cobrar';

@Component({
  selector: 'app-plataforma-negocios',
  imports: [PageHeaderComponent, CommonModule, FormsModule, EmptyStateComponent, IconComponent],
  templateUrl: './plataforma-negocios.component.html',
  styleUrl: './plataforma-negocios.component.scss'
})
export class PlataformaNegociosComponent implements OnInit {
  private readonly svc = inject(NegociosPlataformaService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  readonly negocios = signal<NegocioResumen[]>([]);
  readonly resumen = signal<PlataformaResumen | null>(null);
  readonly cargando = signal(false);
  readonly error = signal<string | null>(null);

  readonly busqueda = signal('');
  readonly filtro = signal<FiltroEstado>('todas');

  readonly confirmarDesactivar = signal<NegocioResumen | null>(null);

  // ---- Alertas tipo panel (vencimiento de suscripción de las empresas alquiladas) ----
  readonly alertaVencidas = computed(() =>
    this.negocios().filter(n => n.activo && (n.estadoSuscripcion === 'VENCIDA' || (this.diasParaVencer(n) ?? 99) < 0)));
  readonly alertaPorVencer = computed(() =>
    this.negocios().filter(n => {
      const d = this.diasParaVencer(n);
      return n.activo && n.estadoSuscripcion !== 'VENCIDA' && d !== null && d >= 0 && d <= 5;
    }));
  readonly alertaSuspendidas = computed(() => this.negocios().filter(n => !n.activo));

  readonly expandida = signal<'vencidas' | 'porvencer' | 'suspendidas' | null>(null);
  readonly cerradas = signal<Set<string>>(new Set());
  toggleExpandir(k: 'vencidas' | 'porvencer' | 'suspendidas') { this.expandida.set(this.expandida() === k ? null : k); }
  cerrarAlerta(k: string) { const s = new Set(this.cerradas()); s.add(k); this.cerradas.set(s); }
  estaCerrada(k: string) { return this.cerradas().has(k); }

  readonly negociosFiltrados = computed(() => {
    const texto = this.busqueda().trim().toLowerCase();
    const f = this.filtro();
    return this.negocios().filter(n => {
      if (texto && !n.nombre.toLowerCase().includes(texto) && !n.slug.toLowerCase().includes(texto)) return false;
      if (f === 'activas') return n.activo;
      if (f === 'suspendidas') return !n.activo;
      if (f === 'por-cobrar') return n.activo && this.diasParaVencer(n) !== null && this.diasParaVencer(n)! <= 7;
      return true;
    });
  });

  ngOnInit() { this.cargar(); }

  cargar() {
    this.cargando.set(true);
    this.error.set(null);
    this.svc.resumen().subscribe({ next: r => this.resumen.set(r), error: () => {} });
    this.svc.listar().subscribe({
      next: list => { this.negocios.set(list); this.cargando.set(false); },
      error: (err: HttpErrorResponse) => {
        this.cargando.set(false);
        this.error.set(err.status === 0
          ? 'No se pudo conectar con el servidor.'
          : (err.error?.mensaje ?? 'Error al cargar las empresas.'));
      }
    });
  }

  nueva() { this.router.navigate(['/plataforma/nueva']); }
  abrir(n: NegocioResumen) { this.router.navigate(['/plataforma/empresa', n.id]); }

  // ---- Suscripción / cobro ----
  diasParaVencer(n: NegocioResumen): number | null {
    if (!n.proximoPago) return null;
    const hoy = new Date(); hoy.setHours(0, 0, 0, 0);
    const pago = new Date(n.proximoPago);
    return Math.round((pago.getTime() - hoy.getTime()) / 86_400_000);
  }

  estadoSuscripcion(n: NegocioResumen): { texto: string; clase: string } {
    if (!n.activo) return { texto: 'Suspendida', clase: 'badge--gris' };
    const dias = this.diasParaVencer(n);
    if (n.estadoSuscripcion === 'VENCIDA' || (dias !== null && dias < 0)) return { texto: 'Vencida', clase: 'badge--rojo' };
    if (n.estadoSuscripcion === 'PRUEBA') return { texto: 'Prueba', clase: 'badge--azul' };
    if (dias !== null && dias <= 7) return { texto: `Vence en ${dias}d`, clase: 'badge--naranja' };
    return { texto: 'Al día', clase: 'badge--verde' };
  }

  // ---- Suspender / reactivar ----
  toggleActivo(n: NegocioResumen, ev?: Event) {
    ev?.stopPropagation();
    if (n.activo) { this.confirmarDesactivar.set(n); return; }
    this.aplicarCambioEstado(n, true);
  }

  confirmarDesactivarOk() {
    const n = this.confirmarDesactivar();
    if (!n) return;
    this.aplicarCambioEstado(n, false);
    this.confirmarDesactivar.set(null);
  }

  private aplicarCambioEstado(n: NegocioResumen, activo: boolean) {
    this.svc.cambiarEstado(n.id, activo).subscribe({
      next: () => {
        this.toast.info(activo ? 'Empresa reactivada' : 'Empresa suspendida');
        this.cargar();
      },
      error: () => this.toast.error('No se pudo cambiar el estado.')
    });
  }
}
