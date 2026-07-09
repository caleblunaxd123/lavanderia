import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { tap } from 'rxjs';
import { environment } from '../../../environments/environment';
import { LoginRequest, LoginResponse, UsuarioSesion } from '../models/models';
import { TenantContextService } from './tenant-context.service';

const STORAGE_KEY = 'lav.session';

interface SesionAlmacenada {
  accessToken: string;
  expira: string;
  usuario: UsuarioSesion;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly tenant = inject(TenantContextService);

  private readonly sesion = signal<SesionAlmacenada | null>(this.restaurar());

  readonly usuario = computed<UsuarioSesion | null>(() => this.sesion()?.usuario ?? null);
  readonly autenticado = computed<boolean>(() => this.sesion() !== null && !this.tokenExpirado());

  login(req: LoginRequest) {
    const body: LoginRequest = { ...req, empresaSlug: this.tenant.slug() ?? undefined };
    return this.http.post<LoginResponse>(`${environment.apiUrl}/auth/login`, body).pipe(
      tap(res => this.guardar(res))
    );
  }

  /** Cambia la sede activa de la sesión (re-emite el JWT con el nuevo SedeId). */
  cambiarSede(sedeId: number) {
    return this.http.post<LoginResponse>(`${environment.apiUrl}/auth/seleccionar-sede`, { sedeId }).pipe(
      tap(res => this.guardar(res))
    );
  }

  logout() {
    localStorage.removeItem(STORAGE_KEY);
    this.sesion.set(null);
    this.router.navigate(['/login']);
  }

  obtenerToken(): string | null {
    const s = this.sesion();
    if (!s) return null;
    if (this.tokenExpirado()) {
      this.logout();
      return null;
    }
    return s.accessToken;
  }

  esRol(rol: string): boolean {
    return this.usuario()?.rol === rol;
  }

  private tokenExpirado(): boolean {
    const s = this.sesion();
    if (!s) return true;
    return new Date(s.expira).getTime() <= Date.now();
  }

  private guardar(res: LoginResponse) {
    const s: SesionAlmacenada = { accessToken: res.accessToken, expira: res.expira, usuario: res.usuario };
    localStorage.setItem(STORAGE_KEY, JSON.stringify(s));
    this.sesion.set(s);
  }

  private restaurar(): SesionAlmacenada | null {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return null;
    try {
      return JSON.parse(raw) as SesionAlmacenada;
    } catch {
      localStorage.removeItem(STORAGE_KEY);
      return null;
    }
  }
}
