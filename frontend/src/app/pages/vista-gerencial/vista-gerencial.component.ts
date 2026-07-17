import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { ReportesService, TableroSla, VistaGerencial } from '../../core/services/reportes.service';
import { IconComponent } from '../../shared/icon/icon.component';

@Component({
  selector: 'app-vista-gerencial',
  imports: [CommonModule, IconComponent],
  templateUrl: './vista-gerencial.component.html',
  styleUrl: './vista-gerencial.component.scss'
})
export class VistaGerencialComponent implements OnInit {
  private readonly svc = inject(ReportesService);
  private readonly router = inject(Router);

  readonly cargando = signal(true);
  readonly kpis = signal<VistaGerencial | null>(null);
  readonly sla = signal<TableroSla | null>(null);

  ngOnInit() {
    this.cargando.set(true);
    this.svc.vistaGerencial().subscribe({
      next: k => { this.kpis.set(k); this.cargando.set(false); },
      error: () => this.cargando.set(false)
    });
    this.svc.sla().subscribe({ next: s => this.sla.set(s), error: () => {} });
  }

  volver() { this.router.navigate(['/reportes']); }

  porcentajeDesvio(area: { tiempoEstMinutos: number; minutosPromedioReal: number }): number {
    if (area.tiempoEstMinutos <= 0) return 0;
    return Math.round(((area.minutosPromedioReal - area.tiempoEstMinutos) / area.tiempoEstMinutos) * 100);
  }
}
