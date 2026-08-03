import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Sede } from '../../core/models/models';
import { AuthService } from '../../core/services/auth.service';
import { SedesService } from '../../core/services/sedes.service';
import { Rol, UsuarioAdmin, UsuariosService } from '../../core/services/usuarios.service';
import { ToastService } from '../../core/services/toast.service';
import { EmptyStateComponent } from '../../shared/empty-state/empty-state.component';
import { PaginacionComponent } from '../../shared/paginacion/paginacion.component';
import { IconComponent } from '../../shared/icon/icon.component';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';

@Component({
  selector: 'app-ajustes-usuarios',
  imports: [PageHeaderComponent, CommonModule, FormsModule, EmptyStateComponent, PaginacionComponent, IconComponent],
  templateUrl: './ajustes-usuarios.component.html',
  styleUrl: './ajustes-usuarios.component.scss'
})
export class AjustesUsuariosComponent implements OnInit {
  private readonly svc = inject(UsuariosService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  private readonly auth = inject(AuthService);
  private readonly sedesSvc = inject(SedesService);

  readonly usuarios = signal<UsuarioAdmin[]>([]);
  readonly roles = signal<Rol[]>([]);
  readonly sedes = signal<Sede[]>([]);
  readonly cargando = signal(false);
  readonly error = signal<string | null>(null);

  readonly pagina = signal(1);
  readonly tamanoPagina = signal(15);
  readonly usuariosPaginados = computed(() => {
    const inicio = (this.pagina() - 1) * this.tamanoPagina();
    return this.usuarios().slice(inicio, inicio + this.tamanoPagina());
  });
  cambiarPagina(p: number) { this.pagina.set(p); }
  cambiarTamanoPagina(t: number) { this.tamanoPagina.set(t); this.pagina.set(1); }

  readonly modalAbierto = signal(false);
  readonly editando = signal<UsuarioAdmin | null>(null);
  readonly confirmarDesactivar = signal<UsuarioAdmin | null>(null);
  form: Partial<UsuarioAdmin> = this.formVacio();
  errorForm = signal<string | null>(null);
  guardando = signal(false);

  // --- Restablecer contraseña ---
  // Las claves se guardan hasheadas (BCrypt): no se pueden consultar, solo reemplazar.
  readonly reseteando = signal<UsuarioAdmin | null>(null);
  readonly passwordNueva = signal('');
  readonly passwordAplicada = signal<string | null>(null);  // se muestra una sola vez
  readonly errorReset = signal<string | null>(null);
  readonly copiada = signal(false);

  get miPropioUsuarioId(): number | undefined { return this.auth.usuario()?.id; }

  ngOnInit() {
    this.cargar();
    this.svc.roles().subscribe(r => this.roles.set(r));
    this.sedesSvc.listar().subscribe(s => this.sedes.set(s.filter(x => x.activo)));
  }

  cargar() {
    this.cargando.set(true);
    this.error.set(null);
    this.pagina.set(1);
    this.svc.listar().subscribe({
      next: list => { this.usuarios.set(list); this.cargando.set(false); },
      error: (err: HttpErrorResponse) => {
        this.cargando.set(false);
        this.error.set(err.status === 0
          ? 'No se pudo conectar con el servidor.'
          : (err.error?.mensaje ?? 'Error al cargar usuarios.'));
      }
    });
  }

  abrirCrear() {
    this.editando.set(null);
    this.form = this.formVacio();
    this.errorForm.set(null);
    this.modalAbierto.set(true);
  }

  abrirEditar(u: UsuarioAdmin) {
    this.editando.set(u);
    this.form = { ...u, password: '' };
    this.errorForm.set(null);
    this.modalAbierto.set(true);
  }

  cerrar() { this.modalAbierto.set(false); }

  guardar() {
    if (!this.form.usuario?.trim() || !this.form.nombreCompleto?.trim() || !this.form.rolId) {
      this.errorForm.set('Usuario, nombre completo y rol son obligatorios.');
      return;
    }
    if (!this.rolSeleccionadoEsAdmin() && !this.form.sedeId) {
      this.errorForm.set('Los usuarios no administradores deben estar asignados a una sede.');
      return;
    }
    const edit = this.editando();
    if (!edit && !this.form.password?.trim()) {
      this.errorForm.set('Debes definir una contraseña para el nuevo usuario.');
      return;
    }
    this.guardando.set(true);
    this.errorForm.set(null);

    const payload = { ...this.form, email: this.form.email?.trim() || null, sedeId: this.form.sedeId ?? null };
    const obs$: import('rxjs').Observable<any> = edit
      ? this.svc.actualizar(edit.id, payload)
      : this.svc.crear(payload);

    obs$.subscribe({
      next: () => {
        this.guardando.set(false);
        this.modalAbierto.set(false);
        this.toast.exito(edit ? 'Usuario actualizado' : 'Usuario creado');
        this.cargar();
      },
      error: (err: HttpErrorResponse) => {
        this.guardando.set(false);
        const msg = err.error?.mensaje ?? 'No se pudo guardar el usuario.';
        this.errorForm.set(msg);
        this.toast.desdeHttp(err, msg);
      }
    });
  }

  toggleActivo(u: UsuarioAdmin) {
    if (u.id === this.miPropioUsuarioId) return;
    if (u.activo) { this.confirmarDesactivar.set(u); return; }
    this.aplicarCambioEstado(u, true);
  }

  confirmarDesactivarOk() {
    const u = this.confirmarDesactivar();
    if (!u) return;
    this.aplicarCambioEstado(u, false);
    this.confirmarDesactivar.set(null);
  }

  private aplicarCambioEstado(u: UsuarioAdmin, activo: boolean) {
    this.svc.cambiarEstado(u.id, activo).subscribe({
      next: () => {
        this.toast.info(activo ? 'Usuario activado' : 'Usuario desactivado');
        this.cargar();
      },
      error: () => this.toast.error('No se pudo cambiar el estado.')
    });
  }

  // --- Restablecer contraseña ---
  abrirReset(u: UsuarioAdmin) {
    this.reseteando.set(u);
    this.passwordNueva.set('');
    this.passwordAplicada.set(null);
    this.errorReset.set(null);
    this.copiada.set(false);
  }

  cerrarReset() { this.reseteando.set(null); }

  /** Clave temporal legible (fácil de dictar) y con la fuerza que exige el backend:
   *  8+ caracteres, con letras y números. Usa crypto para no ser predecible. */
  generarClaveTemporal() {
    const consonantes = 'bcdfgjklmnprstv';
    const vocales = 'aeiou';
    const azar = (max: number) => {
      const buf = new Uint32Array(1);
      crypto.getRandomValues(buf);
      return buf[0] % max;
    };
    let palabra = '';
    for (let i = 0; i < 3; i++) palabra += consonantes[azar(consonantes.length)] + vocales[azar(vocales.length)];
    const numero = String(100 + azar(900));
    this.passwordNueva.set(palabra.charAt(0).toUpperCase() + palabra.slice(1) + numero);
    this.errorReset.set(null);
  }

  restablecerPassword() {
    const u = this.reseteando();
    if (!u) return;
    const nueva = this.passwordNueva().trim();
    if (nueva.length < 8 || !/[A-Za-z]/.test(nueva) || !/\d/.test(nueva)) {
      this.errorReset.set('La contraseña debe tener al menos 8 caracteres e incluir letras y números.');
      return;
    }
    this.guardando.set(true);
    this.errorReset.set(null);
    this.svc.actualizar(u.id, { ...u, password: nueva }).subscribe({
      next: () => {
        this.guardando.set(false);
        this.passwordAplicada.set(nueva);   // se muestra una sola vez para entregarla
        this.toast.exito(`Contraseña restablecida para ${u.usuario}`);
      },
      error: (err: HttpErrorResponse) => {
        this.guardando.set(false);
        const msg = err.error?.mensaje ?? 'No se pudo restablecer la contraseña.';
        this.errorReset.set(msg);
        this.toast.desdeHttp(err, msg);
      }
    });
  }

  async copiarPassword() {
    const clave = this.passwordAplicada();
    if (!clave) return;
    try {
      await navigator.clipboard.writeText(clave);
      this.copiada.set(true);
      setTimeout(() => this.copiada.set(false), 2000);
    } catch {
      this.toast.advertencia('No se pudo copiar. Selecciona la clave y cópiala manualmente.');
    }
  }

  volver() { this.router.navigate(['/ajustes']); }

  rolSeleccionadoEsAdmin(): boolean {
    const rol = this.roles().find(r => r.id === this.form.rolId);
    return rol?.codigo === 'ADMIN';
  }

  private formVacio(): Partial<UsuarioAdmin> {
    return { usuario: '', nombreCompleto: '', email: '', password: '', rolId: this.roles()[0]?.id, sedeId: null, activo: true };
  }
}
