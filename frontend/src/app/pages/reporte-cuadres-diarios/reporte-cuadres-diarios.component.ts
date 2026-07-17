import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { CuadreDiarioDia, CuadresDiariosReporte, ReportesService } from '../../core/services/reportes.service';
import { ToastService } from '../../core/services/toast.service';
import { IconComponent } from '../../shared/icon/icon.component';

@Component({
  selector: 'app-reporte-cuadres-diarios',
  imports: [CommonModule, FormsModule, IconComponent],
  templateUrl: './reporte-cuadres-diarios.component.html',
  styleUrl: './reporte-cuadres-diarios.component.scss'
})
export class ReporteCuadresDiariosComponent implements OnInit {
  private readonly svc = inject(ReportesService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  // Filtro por mes: input type="month" → 'YYYY-MM'
  mesFiltro = new Date().toISOString().slice(0, 7);

  readonly data = signal<CuadresDiariosReporte | null>(null);
  readonly cargando = signal(false);
  readonly expandido = signal(false);

  // Días con dinero no cuadrado (sin cuadre guardado pero con movimientos).
  readonly noCuadrados = computed(() =>
    (this.data()?.dias ?? []).filter(d => d.sinInformacion && (d.noCuadradoIngresos > 0 || d.noCuadradoEgresos > 0))
  );

  ngOnInit() { this.cargar(); }

  cargar() {
    const [a, m] = this.mesFiltro.split('-').map(Number);
    if (!a || !m) return;
    this.cargando.set(true);
    this.svc.cuadresDiarios(a, m).subscribe({
      next: d => { this.data.set(d); this.cargando.set(false); },
      error: () => { this.cargando.set(false); this.toast.error('No se pudo cargar el reporte.'); }
    });
  }

  toggleExpandido() { this.expandido.set(!this.expandido()); }

  claseEstado(estado: string): string {
    return ({ CUADRA: 'badge badge--verde', SOBRA: 'badge badge--naranja', FALTA: 'badge badge--rojo' } as Record<string, string>)[estado]
      ?? 'badge badge--gris';
  }

  // Al hacer click en un día no cuadrado, ir al cuadre de esa fecha para guardarlo.
  verDia(dia: CuadreDiarioDia) {
    this.router.navigate(['/cuadre-caja'], { queryParams: { fecha: dia.fecha.slice(0, 10) } });
  }
}
