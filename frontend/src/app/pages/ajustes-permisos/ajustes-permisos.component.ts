import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { PermisoItem, PermisosService } from '../../core/services/permisos.service';
import { Rol, UsuariosService } from '../../core/services/usuarios.service';
import { ToastService } from '../../core/services/toast.service';
import { IconComponent } from '../../shared/icon/icon.component';

@Component({
  selector: 'app-ajustes-permisos',
  imports: [CommonModule, IconComponent],
  templateUrl: './ajustes-permisos.component.html',
  styleUrl: './ajustes-permisos.component.scss'
})
export class AjustesPermisosComponent implements OnInit {
  private readonly svc = inject(PermisosService);
  private readonly usuariosSvc = inject(UsuariosService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  readonly cargando = signal(false);
  readonly guardando = signal(false);
  readonly roles = signal<Rol[]>([]);
  readonly modulos = signal<string[]>([]);
  readonly matriz = signal<Map<string, boolean>>(new Map());

  readonly rolesEditables = computed(() => this.roles().filter(r => r.codigo !== 'ADMIN'));
  readonly etiquetas = this.svc.modulosEtiquetas;

  ngOnInit() { this.cargar(); }

  private clave(rolId: number, modulo: string) { return `${rolId}::${modulo}`; }

  cargar() {
    this.cargando.set(true);
    this.usuariosSvc.roles().subscribe(roles => {
      this.roles.set(roles);
      this.svc.modulos().subscribe(modulos => {
        this.modulos.set(modulos);
        this.svc.obtenerMatriz().subscribe({
          next: (items: PermisoItem[]) => {
            const map = new Map<string, boolean>();
            for (const it of items) map.set(this.clave(it.rolId, it.modulo), it.puedeAcceder);
            this.matriz.set(map);
            this.cargando.set(false);
          },
          error: (err: HttpErrorResponse) => {
            this.cargando.set(false);
            this.toast.error(err.error?.mensaje ?? 'No se pudo cargar la matriz de permisos.');
          }
        });
      });
    });
  }

  tienePermiso(rolId: number, modulo: string): boolean {
    return this.matriz().get(this.clave(rolId, modulo)) ?? false;
  }

  toggle(rolId: number, modulo: string) {
    const key = this.clave(rolId, modulo);
    const actual = this.matriz().get(key) ?? false;
    const nuevo = new Map(this.matriz());
    nuevo.set(key, !actual);
    this.matriz.set(nuevo);
  }

  guardar() {
    this.guardando.set(true);
    const permisos: PermisoItem[] = [];
    for (const r of this.rolesEditables()) {
      for (const m of this.modulos()) {
        permisos.push({ rolId: r.id, modulo: m, puedeAcceder: this.tienePermiso(r.id, m) });
      }
    }
    this.svc.guardar(permisos).subscribe({
      next: () => {
        this.guardando.set(false);
        this.toast.exito('Permisos actualizados. Los usuarios verán los cambios en su próximo inicio de sesión.');
      },
      error: (err: HttpErrorResponse) => {
        this.guardando.set(false);
        this.toast.error(err.error?.mensaje ?? 'No se pudo guardar la matriz de permisos.');
      }
    });
  }

  volver() { this.router.navigate(['/ajustes']); }
}
