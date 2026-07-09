import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { CajaService, CuadreCajaGuardado } from '../../core/services/caja.service';
import { ConfiguracionService } from '../../core/services/configuracion.service';
import { IconComponent } from '../../shared/icon/icon.component';

@Component({
  selector: 'app-cuadre-imprimir',
  imports: [CommonModule, IconComponent],
  templateUrl: './cuadre-imprimir.component.html',
  styleUrl: './cuadre-imprimir.component.scss'
})
export class CuadreImprimirComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly cajaSvc = inject(CajaService);
  private readonly config = inject(ConfiguracionService);

  readonly cuadre = signal<CuadreCajaGuardado | null>(null);
  readonly error = signal<string | null>(null);
  readonly cargando = signal(true);
  readonly negocio = computed(() => this.config.configuracion());

  readonly estado = computed<'SOBRA' | 'CUADRA' | 'FALTA'>(() => {
    const c = this.cuadre();
    if (!c) return 'CUADRA';
    if (Math.abs(c.diferencia) < 0.01) return 'CUADRA';
    return c.diferencia > 0 ? 'SOBRA' : 'FALTA';
  });

  ngOnInit() {
    const id = Number(this.route.snapshot.paramMap.get('id'));
    if (!id) {
      this.error.set('ID de cuadre inválido.');
      this.cargando.set(false);
      return;
    }
    this.cajaSvc.obtenerCuadre(id).subscribe({
      next: c => {
        this.cuadre.set(c);
        this.cargando.set(false);
        setTimeout(() => this.imprimir(), 500);
      },
      error: () => {
        this.error.set('No se pudo cargar el cuadre.');
        this.cargando.set(false);
      }
    });
  }

  imprimir() { window.print(); }
  cerrar() { window.close(); }
}
