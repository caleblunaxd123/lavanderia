import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { Sede } from '../../core/models/models';
import { AuthService } from '../../core/services/auth.service';
import { ConfiguracionService } from '../../core/services/configuracion.service';
import { SedesService } from '../../core/services/sedes.service';

interface NavLink {
  label: string;
  path: string;
  modulo: string;
}

@Component({
  selector: 'app-header',
  imports: [CommonModule, RouterLink, RouterLinkActive],
  templateUrl: './header.component.html',
  styleUrl: './header.component.scss'
})
export class HeaderComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly config = inject(ConfiguracionService);
  private readonly sedesSvc = inject(SedesService);

  readonly usuario = this.auth.usuario;
  readonly negocio = computed(() => this.config.configuracion());

  readonly menuAbierto = signal(false);

  // Badge de sede: siempre visible para saber en qué sucursal estás trabajando.
  // Solo ADMIN con más de una sede puede además cambiarse entre sucursales desde acá.
  readonly sedes = signal<Sede[]>([]);
  readonly sedeMenuAbierto = signal(false);
  readonly cambiandoSede = signal(false);
  readonly mostrarBadgeSede = computed(() => !!this.usuario()?.sedeNombre);
  readonly puedeCambiarSede = computed(() => this.usuario()?.rol === 'ADMIN' && this.sedes().length > 1);

  ngOnInit() {
    if (this.usuario()?.rol === 'ADMIN') {
      this.sedesSvc.listar().subscribe(list => this.sedes.set(list.filter(s => s.activo)));
    }
  }

  toggleSedeMenu() { this.sedeMenuAbierto.update(v => !v); }

  elegirSede(sedeId: number) {
    if (sedeId === this.usuario()?.sedeId) { this.sedeMenuAbierto.set(false); return; }
    this.cambiandoSede.set(true);
    this.auth.cambiarSede(sedeId).subscribe({
      next: () => {
        this.cambiandoSede.set(false);
        this.sedeMenuAbierto.set(false);
        window.location.reload();
      },
      error: () => { this.cambiandoSede.set(false); }
    });
  }

  private readonly primaryLinksBase: NavLink[] = [
    { label: 'Inicio', path: '/inicio', modulo: 'INICIO' },
    { label: 'Pedidos', path: '/pedidos', modulo: 'PEDIDOS' },
    { label: 'Registrar', path: '/registrar', modulo: 'REGISTRAR' },
    { label: 'Cuadre de Caja', path: '/cuadre-caja', modulo: 'CAJA' },
  ];

  private readonly secondaryLinksBase: NavLink[] = [
    { label: 'Clientes', path: '/clientes', modulo: 'CLIENTES' },
    { label: 'Promociones', path: '/promociones', modulo: 'PROMOCIONES' },
    { label: 'Reportes', path: '/reportes', modulo: 'REPORTES' },
    { label: 'Inventario', path: '/inventario', modulo: 'INVENTARIO' },
    { label: 'Ajustes', path: '/ajustes', modulo: 'AJUSTES' },
  ];

  readonly primaryLinks = computed(() => this.filtrar(this.primaryLinksBase));
  readonly secondaryLinks = computed(() => this.filtrar(this.secondaryLinksBase));

  private filtrar(list: NavLink[]): NavLink[] {
    const modulos = this.usuario()?.modulosPermitidos ?? [];
    return list.filter(l => modulos.includes(l.modulo));
  }

  toggleMenu() { this.menuAbierto.update(v => !v); }
  cerrarMenu() { this.menuAbierto.set(false); }
  logout() { this.cerrarMenu(); this.auth.logout(); }
}
