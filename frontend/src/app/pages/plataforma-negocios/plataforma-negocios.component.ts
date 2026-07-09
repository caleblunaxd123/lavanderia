import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { NegocioResumen } from '../../core/models/models';
import { NegociosPlataformaService } from '../../core/services/negocios-plataforma.service';
import { ToastService } from '../../core/services/toast.service';
import { EmptyStateComponent } from '../../shared/empty-state/empty-state.component';
import { IconComponent } from '../../shared/icon/icon.component';

@Component({
  selector: 'app-plataforma-negocios',
  imports: [CommonModule, EmptyStateComponent, IconComponent],
  templateUrl: './plataforma-negocios.component.html',
  styleUrl: './plataforma-negocios.component.scss'
})
export class PlataformaNegociosComponent implements OnInit {
  private readonly svc = inject(NegociosPlataformaService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  readonly negocios = signal<NegocioResumen[]>([]);
  readonly cargando = signal(false);
  readonly error = signal<string | null>(null);

  readonly confirmarDesactivar = signal<NegocioResumen | null>(null);

  ngOnInit() { this.cargar(); }

  cargar() {
    this.cargando.set(true);
    this.error.set(null);
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

  toggleActivo(n: NegocioResumen) {
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
