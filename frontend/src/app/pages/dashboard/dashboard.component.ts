import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';
import { Dashboard, PedidosService } from '../../core/services/pedidos.service';
import { ToastService } from '../../core/services/toast.service';
import { SkeletonComponent } from '../../shared/skeleton/skeleton.component';

@Component({
  selector: 'app-dashboard',
  imports: [CommonModule, RouterLink, SkeletonComponent],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss'
})
export class DashboardComponent implements OnInit, OnDestroy {
  private readonly svc = inject(PedidosService);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);

  readonly data = signal<Dashboard | null>(null);
  readonly cargando = signal(false);
  readonly usuario = this.auth.usuario;

  readonly saludo = computed(() => {
    const h = new Date().getHours();
    if (h < 12) return 'Buenos días';
    if (h < 19) return 'Buenas tardes';
    return 'Buenas noches';
  });

  private timerId?: ReturnType<typeof setInterval>;

  ngOnInit() {
    this.cargar();
    // Refresco cada 60s para tener siempre datos frescos
    this.timerId = setInterval(() => this.cargar(true), 60_000);
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

  colorArea(cantidad: number): string {
    if (cantidad === 0) return '#e6f9ee';
    if (cantidad <= 2) return '#fff4e0';
    return '#fde8e8';
  }
}
