import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ConfiguracionPagos, PagosService } from '../../core/services/pagos.service';
import { ToastService } from '../../core/services/toast.service';
import { IconComponent } from '../../shared/icon/icon.component';
import { InfoTooltipComponent } from '../../shared/info-tooltip/info-tooltip.component';

const VACIO: ConfiguracionPagos = {
  proveedor: 'CULQI',
  publicKey: '',
  secretKeyNueva: null,
  activo: false,
  tieneSecretKey: false
};

@Component({
  selector: 'app-ajustes-pagos',
  imports: [CommonModule, FormsModule, IconComponent, InfoTooltipComponent],
  templateUrl: './ajustes-pagos.component.html',
  styleUrl: './ajustes-pagos.component.scss'
})
export class AjustesPagosComponent implements OnInit {
  private readonly svc = inject(PagosService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  readonly form = signal<ConfiguracionPagos>({ ...VACIO });
  readonly cargando = signal(true);
  readonly guardando = signal(false);

  ngOnInit() {
    this.svc.obtenerConfiguracion().subscribe({
      next: c => { this.form.set({ ...c, secretKeyNueva: null }); this.cargando.set(false); },
      error: () => { this.cargando.set(false); this.toast.error('No se pudo cargar la configuración.'); }
    });
  }

  actualizarCampo<K extends keyof ConfiguracionPagos>(campo: K, valor: ConfiguracionPagos[K]) {
    this.form.update(f => ({ ...f, [campo]: valor }));
  }

  guardar() {
    this.guardando.set(true);
    this.svc.guardarConfiguracion(this.form()).subscribe({
      next: () => {
        this.guardando.set(false);
        this.toast.exito('Configuración guardada');
        this.ngOnInit();
      },
      error: (err: HttpErrorResponse) => {
        this.guardando.set(false);
        this.toast.desdeHttp(err, 'No se pudo guardar la configuración.');
      }
    });
  }

  volver() { this.router.navigate(['/ajustes']); }
}
