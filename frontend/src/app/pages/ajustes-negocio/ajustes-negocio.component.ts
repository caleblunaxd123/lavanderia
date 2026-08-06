import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ConfiguracionNegocio } from '../../core/models/models';
import { ConfiguracionService } from '../../core/services/configuracion.service';
import { ToastService } from '../../core/services/toast.service';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';
import { IconComponent } from '../../shared/icon/icon.component';

@Component({
  selector: 'app-ajustes-negocio',
  imports: [PageHeaderComponent, CommonModule, FormsModule, IconComponent],
  templateUrl: './ajustes-negocio.component.html',
  styleUrl: './ajustes-negocio.component.scss'
})
export class AjustesNegocioComponent implements OnInit {
  private readonly svc = inject(ConfiguracionService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  readonly form = signal<ConfiguracionNegocio>({ ...this.svc.configuracion() });
  readonly guardando = signal(false);

  // --- Logo: se sube desde el equipo del usuario ---
  readonly subiendoLogo = signal(false);
  readonly errorLogo = signal<string | null>(null);
  readonly arrastrando = signal(false);

  onDragOver(e: DragEvent) {
    e.preventDefault();
    this.arrastrando.set(true);
  }

  onDrop(e: DragEvent) {
    e.preventDefault();
    this.arrastrando.set(false);
    const archivo = e.dataTransfer?.files?.[0];
    if (archivo) this.subirLogo(archivo);
  }

  onArchivoLogo(e: Event) {
    const input = e.target as HTMLInputElement;
    const archivo = input.files?.[0];
    if (archivo) this.subirLogo(archivo);
    input.value = '';   // permite volver a elegir el mismo archivo
  }

  quitarLogo() {
    this.actualizarCampo('logoUrl', null as never);
    this.errorLogo.set(null);
  }

  private subirLogo(archivo: File) {
    this.errorLogo.set(null);
    if (!['image/png', 'image/jpeg', 'image/webp'].includes(archivo.type)) {
      this.errorLogo.set('Formato no permitido. Elige una imagen JPG, PNG o WEBP.');
      return;
    }
    if (archivo.size > 2 * 1024 * 1024) {
      this.errorLogo.set('La imagen pesa más de 2 MB. Usa una más liviana.');
      return;
    }
    this.subiendoLogo.set(true);
    this.svc.subirLogo(archivo).subscribe({
      next: r => {
        this.subiendoLogo.set(false);
        this.actualizarCampo('logoUrl', r.logoUrl as never);
        this.toast.exito('Logo cargado. Guarda los cambios para aplicarlo.');
      },
      error: (err: HttpErrorResponse) => {
        this.subiendoLogo.set(false);
        const msg = err.error?.mensaje ?? 'No se pudo subir el logo.';
        this.errorLogo.set(msg);
        this.toast.desdeHttp(err, msg);
      }
    });
  }

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
