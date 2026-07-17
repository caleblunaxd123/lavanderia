import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';
import { CatalogosService } from '../../core/services/catalogos.service';
import { ConfiguracionService } from '../../core/services/configuracion.service';
import { Dashboard, PedidosService } from '../../core/services/pedidos.service';
import { PersonalService } from '../../core/services/personal.service';
import { SuscripcionService } from '../../core/services/suscripcion.service';
import { ToastService } from '../../core/services/toast.service';
import { MiSuscripcion } from '../../core/models/models';
import { IconComponent } from '../../shared/icon/icon.component';
import { SkeletonComponent } from '../../shared/skeleton/skeleton.component';

interface PasoOnboarding {
  clave: string;
  titulo: string;
  hecho: boolean;
  ruta: string;
}

@Component({
  selector: 'app-dashboard',
  imports: [CommonModule, RouterLink, SkeletonComponent, IconComponent],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss'
})
export class DashboardComponent implements OnInit, OnDestroy {
  private readonly svc = inject(PedidosService);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  private readonly config = inject(ConfiguracionService);
  private readonly catalogos = inject(CatalogosService);
  private readonly personalSvc = inject(PersonalService);
  private readonly suscripcionSvc = inject(SuscripcionService);

  readonly data = signal<Dashboard | null>(null);
  readonly cargando = signal(false);
  readonly usuario = this.auth.usuario;

  // Aviso de vencimiento de la suscripción (lo ve la propia empresa).
  readonly suscripcion = signal<MiSuscripcion | null>(null);
  readonly avisoSuscripcion = computed(() => {
    const s = this.suscripcion();
    return s && s.mostrar ? s : null;
  });

  // Checklist de primeros pasos para un negocio recien creado (solo ADMIN, y solo
  // mientras falte algo por hacer o el usuario no lo haya cerrado desde este navegador).
  readonly pasosOnboarding = signal<PasoOnboarding[]>([]);
  readonly onboardingCerrado = signal(false);
  readonly mostrarOnboarding = computed(() =>
    this.usuario()?.rol === 'ADMIN' &&
    !this.onboardingCerrado() &&
    this.pasosOnboarding().some(p => !p.hecho)
  );

  private claveCierre(): string {
    return `lav.onboarding.cerrado.${this.usuario()?.negocioId ?? 0}`;
  }

  private cargarOnboarding() {
    if (this.usuario()?.rol !== 'ADMIN') return;
    this.onboardingCerrado.set(localStorage.getItem(this.claveCierre()) === '1');

    this.catalogos.servicios().subscribe(servicios => {
      this.personalSvc.listar().subscribe(personal => {
        this.pasosOnboarding.set([
          { clave: 'logo', titulo: 'Pon el logo de tu lavandería', hecho: !!this.config.configuracion().logoUrl, ruta: '/ajustes/negocio' },
          { clave: 'servicios', titulo: 'Crea tus servicios y precios', hecho: servicios.length > 0, ruta: '/ajustes/servicios' },
          { clave: 'personal', titulo: 'Registra a tus trabajadores', hecho: personal.length > 0, ruta: '/ajustes/personal' },
        ]);
      });
    });
  }

  cerrarOnboarding() {
    localStorage.setItem(this.claveCierre(), '1');
    this.onboardingCerrado.set(true);
  }

  readonly saludo = computed(() => {
    const h = new Date().getHours();
    if (h < 12) return 'Buenos días';
    if (h < 19) return 'Buenas tardes';
    return 'Buenas noches';
  });

  private timerId?: ReturnType<typeof setInterval>;

  ngOnInit() {
    this.cargar();
    this.cargarOnboarding();
    this.suscripcionSvc.mia().subscribe({
      next: s => this.suscripcion.set(s),
      error: () => { /* aviso no crítico: silencioso */ }
    });
    // Refresco cada 30s para tener siempre datos frescos (la presentacion promete "cada 30 segundos")
    this.timerId = setInterval(() => this.cargar(true), 30_000);
  }

  ngOnDestroy() { if (this.timerId) clearInterval(this.timerId); }

  cargar(silencioso = false) {
    if (!silencioso) this.cargando.set(true);
    this.svc.dashboard().subscribe({
      next: d => { this.data.set(d); this.cargando.set(false); },
      error: () => {
        this.cargando.set(false);
        if (!silencioso) this.toast.error('No se pudo cargar el resumen.');
      }
    });
  }

  tieneModulo(modulo: string): boolean {
    return this.usuario()?.modulosPermitidos?.includes(modulo) ?? false;
  }

  colorArea(cantidad: number): string {
    if (cantidad === 0) return '#e6f9ee';
    if (cantidad <= 2) return '#fff4e0';
    return '#fde8e8';
  }
}
