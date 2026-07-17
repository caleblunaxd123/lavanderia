import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ConfiguracionNegocio } from '../../core/models/models';
import { ConfiguracionService } from '../../core/services/configuracion.service';
import { ToastService } from '../../core/services/toast.service';
import { IconComponent } from '../../shared/icon/icon.component';

@Component({
  selector: 'app-ajustes-negocio',
  imports: [CommonModule, FormsModule, IconComponent],
  templateUrl: './ajustes-negocio.component.html',
  styleUrl: './ajustes-negocio.component.scss'
})
export class AjustesNegocioComponent implements OnInit {
  private readonly svc = inject(ConfiguracionService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  readonly form = signal<ConfiguracionNegocio>({ ...this.svc.configuracion() });
  readonly guardando = signal(false);

  readonly previewStyle = computed(() => ({
    'background': `linear-gradient(90deg, ${this.form().colorPrimario} 0%, ${this.form().colorSecundario} 100%)`,
  }));

  ngOnInit() {
    // Aseguramos que traigamos la ultima config del backend
    this.svc.cargar().subscribe({
      next: c => this.form.set({ ...c }),
      error: () => this.toast.error('No se pudo cargar la configuración.')
    });
  }

  actualizarCampo<K extends keyof ConfiguracionNegocio>(campo: K, valor: ConfiguracionNegocio[K]) {
    this.form.update(f => ({ ...f, [campo]: valor }));
  }

  guardar() {
    this.guardando.set(true);
    this.svc.actualizar(this.form()).subscribe({
      next: () => {
        this.guardando.set(false);
        this.toast.exito('Configuración guardada');
      },
      error: (err: HttpErrorResponse) => {
        this.guardando.set(false);
        this.toast.desdeHttp(err, 'No se pudo guardar la configuración.');
      }
    });
  }

  volver() { this.router.navigate(['/ajustes']); }
}
