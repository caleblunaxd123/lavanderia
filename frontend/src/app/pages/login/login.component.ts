import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';
import { ConfiguracionService } from '../../core/services/configuracion.service';
import { TenantContextService } from '../../core/services/tenant-context.service';

@Component({
  selector: 'app-login',
  imports: [CommonModule, FormsModule],
  templateUrl: './login.component.html',
  styleUrl: './login.component.scss'
})
export class LoginComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly config = inject(ConfiguracionService);
  private readonly tenant = inject(TenantContextService);

  readonly nombreNegocio = computed(() => this.config.configuracion().nombreNegocio);
  readonly logoUrl = computed(() => this.config.configuracion().logoUrl);

  usuario = '';
  password = '';
  cargando = signal(false);
  error = signal<string | null>(null);

  ngOnInit() {
    // Marca de la empresa segun el slug de la URL (/lavixa/login); sin slug, cae al
    // endpoint generico (compatibilidad con accesos sin empresa identificada).
    const slug = this.tenant.slug();
    const obs$ = slug ? this.config.cargarPorSlug(slug) : this.config.cargar();
    obs$.subscribe({ error: () => {} });
  }

  submit() {
    if (!this.usuario.trim() || !this.password) {
      this.error.set('Completa usuario y contraseña.');
      return;
    }
    this.cargando.set(true);
    this.error.set(null);

    this.auth.login({ usuario: this.usuario.trim(), password: this.password }).subscribe({
      next: () => {
        // La marca (logo/nombre/colores) se cargó antes del login con el negocio "por defecto";
        // hay que refrescarla ahora que el token trae el negocio real del usuario.
        this.config.cargar().subscribe({ error: () => {} });
        const destino = this.auth.usuario()?.sedeId ? '/inicio' : '/seleccionar-sede';
        this.router.navigate([destino]);
      },
      error: (err: HttpErrorResponse) => {
        this.cargando.set(false);
        this.error.set(err.error?.mensaje ?? 'No se pudo iniciar sesión. Verifica el servidor.');
      }
    });
  }
}
