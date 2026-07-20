import { CommonModule } from '@angular/common';
import { Component, HostListener, OnInit, computed, inject, signal } from '@angular/core';
import { NavigationEnd, Router, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import { HeaderComponent } from './layout/header/header.component';
import { PlataformaHeaderComponent } from './layout/plataforma-header/plataforma-header.component';
import { AuthService } from './core/services/auth.service';
import { ConfiguracionService } from './core/services/configuracion.service';
import { TenantContextService } from './core/services/tenant-context.service';
import { ToasterComponent } from './shared/toaster/toaster.component';
import { AlertasGlobalesComponent } from './shared/alertas-globales/alertas-globales.component';

@Component({
  selector: 'app-root',
  imports: [CommonModule, RouterOutlet, HeaderComponent, PlataformaHeaderComponent, AlertasGlobalesComponent, ToasterComponent],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss'
})
export class AppComponent implements OnInit {
  private readonly router = inject(Router);
  private readonly auth = inject(AuthService);
  private readonly config = inject(ConfiguracionService);
  private readonly tenant = inject(TenantContextService);

  private readonly rutaActual = signal<string>('/');
  readonly esPlataforma = computed(() => this.rutaActual().startsWith('/plataforma'));
  readonly mostrarHeader = computed(() => {
    const r = this.rutaActual();
    if (!this.auth.autenticado()) return false;
    if (r.startsWith('/login')) return false;
    if (r.startsWith('/ticket/')) return false;  // ticket es fullscreen para imprimir
    if (r.startsWith('/cuadre-caja/imprimir/')) return false;  // cuadre imprimible tambien
    if (r.startsWith('/seguimiento/')) return false;  // portal publico del cliente (incluye pago): jamas mostrar el nav interno
    if (r.startsWith('/repartidor/')) return false;  // portal publico del repartidor
    if (this.esPlataforma()) return false;  // usa su propio header minimo
    return true;
  });
  readonly mostrarAlertas = computed(() => {
    const r = this.rutaActual();
    if (!this.auth.autenticado() || r.startsWith('/login')) return false;
    return !r.startsWith('/ticket/') &&
      !r.startsWith('/cuadre-caja/imprimir/') &&
      !r.startsWith('/seguimiento/') &&
      !r.startsWith('/repartidor/') &&
      !r.startsWith('/seleccionar-sede');
  });

  ngOnInit() {
    // La primera navegacion es la que hace que TenantUrlSerializer.parse() fije el slug;
    // si se lee tenant.slug() antes de eso (fuera de este subscribe) puede llegar en null
    // y cargar la marca generica por error. Por eso la carga de marca va DESPUES del primer
    // NavigationEnd, nunca de forma sincronica en el cuerpo de ngOnInit.
    let primeraNavegacion = true;
    this.router.events
      .pipe(filter(e => e instanceof NavigationEnd))
      .subscribe(e => {
        this.rutaActual.set(this.quitarSlug((e as NavigationEnd).urlAfterRedirects));
        if (primeraNavegacion) {
          primeraNavegacion = false;
          const slug = this.tenant.slug();
          const obs$ = slug ? this.config.cargarPorSlug(slug) : this.config.cargar();
          obs$.subscribe({ error: () => {} });
        }
      });
  }

  /**
   * `urlAfterRedirects` ya viene con el slug de empresa antepuesto (pasa por
   * TenantUrlSerializer.serialize). Se le quita antes de guardarla para que los chequeos
   * de mostrarHeader (basados en rutas absolutas sin slug) sigan funcionando sin cambios.
   */
  private quitarSlug(url: string): string {
    const slug = this.tenant.slug();
    if (slug && url.startsWith(`/${slug}`)) {
      const resto = url.slice(slug.length + 1);
      return resto ? `/${resto}` : '/';
    }
    return url;
  }

  // Cierra el modal/drawer más al frente simulando el click que cada
  // componente ya usa en su backdrop para cerrar (evita duplicar lógica
  // de cierre en cada pantalla).
  @HostListener('document:keydown.escape')
  cerrarConEscape() {
    const backdrops = document.querySelectorAll<HTMLElement>('.modal-backdrop, .drawer-backdrop, .sb-backdrop');
    const topmost = backdrops[backdrops.length - 1];
    topmost?.click();
  }
}
