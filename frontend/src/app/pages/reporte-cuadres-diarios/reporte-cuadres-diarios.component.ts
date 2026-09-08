import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { CuadreDiarioDia, CuadresDiariosReporte, ReportesService } from '../../core/services/reportes.service';
import { mesLocalIso } from '../../core/util/fecha-local';
import { ToastService } from '../../core/services/toast.service';
import { IconComponent } from '../../shared/icon/icon.component';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';

@Component({
  selector: 'app-reporte-cuadres-diarios',
  imports: [CommonModule, FormsModule, IconComponent, PageHeaderComponent],
  templateUrl: './reporte-cuadres-diarios.component.html',
  styleUrl: './reporte-cuadres-diarios.component.scss'
})
export class ReporteCuadresDiariosComponent implements OnInit {
  private readonly svc = inject(ReportesService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  // Filtro por mes: input type="month" → 'YYYY-MM'
  mesFiltro = mesLocalIso();

  readonly data = signal<CuadresDiariosReporte | null>(null);
  readonly cargando = signal(false);
  readonly expandido = signal(false);

  // Días con dinero no cuadrado (sin cuadre guardado pero con movimientos).
  readonly noCuadrados = computed(() =>
    (this.data()?.dias ?? []).filter(d => d.sinInformacion && (d.noCuadradoIngresos > 0 || d.noCuadradoEgresos > 0))
  );

  // ===== Resumen del mes (tarjetas KPI para el dueño) =====
  readonly resumen = computed(() => {
    let efectivo = 0, digital = 0, tarjeta = 0, faltante = 0, sobrante = 0, cerrados = 0, cuadrados = 0;
    for (const d of this.data()?.dias ?? []) {
      for (const c of d.cuadres) {
        efectivo += c.ingresosEfectivo; digital += c.ingresosDigital; tarjeta += c.ingresosTarjeta;
        cerrados++;
        if (c.estado === 'FALTA') faltante += c.margenError;
        else if (c.estado === 'SOBRA') sobrante += c.margenError;
        else cuadrados++;
      }
    }
    return {
      efectivo, digital, tarjeta, faltante, sobrante,
      neto: sobrante - faltante, cerrados, cuadrados,
      conDiferencia: cerrados - cuadrados, sinCuadrar: this.noCuadrados().length,
    };
  });

  // ===== Serie de DIFERENCIAS por día (dónde está el error) =====
  readonly serieDif = computed(() => {
    const filas: { fecha: string; usuario: string; estado: string; valor: number }[] = [];
    for (const d of this.data()?.dias ?? []) {
      for (const c of d.cuadres) {
        const valor = c.estado === 'FALTA' ? -c.margenError : (c.estado === 'SOBRA' ? c.margenError : 0);
        filas.push({ fecha: d.fecha, usuario: c.usuarioNombre, estado: c.estado, valor });
      }
    }
    return filas;
  });
  readonly maxDifAbs = computed(() => Math.max(1, ...this.serieDif().map(f => Math.abs(f.valor))));

  // ===== Serie de INGRESOS por día y método =====
  readonly serieIng = computed(() => {
    const map = new Map<string, { fecha: string; efectivo: number; digital: number; tarjeta: number }>();
    for (const d of this.data()?.dias ?? []) {
      for (const c of d.cuadres) {
        const cur = map.get(d.fecha) ?? { fecha: d.fecha, efectivo: 0, digital: 0, tarjeta: 0 };
        cur.efectivo += c.ingresosEfectivo; cur.digital += c.ingresosDigital; cur.tarjeta += c.ingresosTarjeta;
        map.set(d.fecha, cur);
      }
    }
    return [...map.values()].filter(s => s.efectivo + s.digital + s.tarjeta > 0);
  });
  readonly maxIng = computed(() => Math.max(1, ...this.serieIng().map(s => s.efectivo + s.digital + s.tarjeta)));

  /** Porcentaje (0-100) de un valor respecto al máximo, para el ancho de las barras. */
  pct(valor: number, max: number): number { return Math.round((Math.abs(valor) / max) * 100); }

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

  volver() { this.router.navigate(['/reportes']); }

  claseEstado(estado: string): string {
    return ({ CUADRA: 'badge badge--verde', SOBRA: 'badge badge--naranja', FALTA: 'badge badge--rojo' } as Record<string, string>)[estado]
      ?? 'badge badge--gris';
  }

  // Al hacer click en un día no cuadrado, ir al cuadre de esa fecha para guardarlo.
  verDia(dia: CuadreDiarioDia) {
    this.verFecha(dia.fecha);
  }

  /** Abre el cuadre de una fecha concreta (para revisar el detalle del día). */
  verFecha(fechaIso: string) {
    this.router.navigate(['/cuadre-caja'], { queryParams: { fecha: fechaIso.slice(0, 10) } });
  }
}
