import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Comprobante, FacturacionService } from '../../core/services/facturacion.service';
import { ToastService } from '../../core/services/toast.service';
import { EmptyStateComponent } from '../../shared/empty-state/empty-state.component';
import { PaginacionComponent } from '../../shared/paginacion/paginacion.component';
import { IconComponent } from '../../shared/icon/icon.component';

@Component({
  selector: 'app-comprobantes-list',
  imports: [CommonModule, EmptyStateComponent, PaginacionComponent, IconComponent],
  templateUrl: './comprobantes-list.component.html',
  styleUrl: './comprobantes-list.component.scss'
})
export class ComprobantesListComponent implements OnInit {
  private readonly svc = inject(FacturacionService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  readonly comprobantes = signal<Comprobante[]>([]);
  readonly total = signal(0);
  readonly cargando = signal(false);
  readonly error = signal<string | null>(null);
  readonly pagina = signal(1);
  readonly tamanoPagina = signal(15);
  readonly descargandoId = signal<number | null>(null);

  ngOnInit() { this.cargar(); }

  cargar() {
    this.cargando.set(true);
    this.error.set(null);
    this.svc.listarComprobantes(this.pagina(), this.tamanoPagina()).subscribe({
      next: r => { this.comprobantes.set(r.items); this.total.set(r.total); this.cargando.set(false); },
      error: () => { this.cargando.set(false); this.error.set('No se pudo cargar el listado de comprobantes.'); }
    });
  }

  cambiarPagina(p: number) { this.pagina.set(p); this.cargar(); }
  cambiarTamanoPagina(t: number) { this.tamanoPagina.set(t); this.pagina.set(1); this.cargar(); }

  verPdf(c: Comprobante) {
    this.descargandoId.set(c.id);
    this.svc.descargarPdf(c.id).subscribe({
      next: blob => {
        this.descargandoId.set(null);
        const url = URL.createObjectURL(blob);
        window.open(url, '_blank');
      },
      error: () => { this.descargandoId.set(null); this.toast.error('No se pudo generar el PDF.'); }
    });
  }

  anular(c: Comprobante) {
    if (!confirm(`¿Anular el comprobante ${c.numeroCompleto}? Esta acción no se puede deshacer.`)) return;
    this.svc.anular(c.id).subscribe({
      next: res => { this.toast.info(res.mensaje); this.cargar(); },
      error: () => this.toast.error('No se pudo anular el comprobante.')
    });
  }

  claseEstado(estado: string): string {
    switch (estado) {
      case 'ACEPTADO': return 'badge--verde';
      case 'RECHAZADO': case 'ERROR': return 'badge--rojo';
      case 'ANULADO': return 'badge--gris';
      default: return 'badge--amarillo';
    }
  }

  volver() { this.router.navigate(['/ajustes']); }
}
