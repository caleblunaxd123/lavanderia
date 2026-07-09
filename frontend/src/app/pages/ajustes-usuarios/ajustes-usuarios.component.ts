import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';
import { Rol, UsuarioAdmin, UsuariosService } from '../../core/services/usuarios.service';
import { ToastService } from '../../core/services/toast.service';
import { EmptyStateComponent } from '../../shared/empty-state/empty-state.component';
import { PaginacionComponent } from '../../shared/paginacion/paginacion.component';
import { IconComponent } from '../../shared/icon/icon.component';

@Component({
  selector: 'app-ajustes-usuarios',
  imports: [CommonModule, FormsModule, EmptyStateComponent, PaginacionComponent, IconComponent],
  templateUrl: './ajustes-usuarios.component.html',
  styleUrl: './ajustes-usuarios.component.scss'
})
export class AjustesUsuariosComponent implements OnInit {
  private readonly svc = inject(UsuariosService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  private readonly auth = inject(AuthService);

  readonly usuarios = signal<UsuarioAdmin[]>([]);
  readonly roles = signal<Rol[]>([]);
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
  form: Partial<UsuarioAdmin> = this.formVacio();
  errorForm = signal<string | null>(null);
  guardando = signal(false);

  get miPropioUsuarioId(): number | undefined { return this.auth.usuario()?.id; }

  ngOnInit() {
    this.cargar();
    this.svc.roles().subscribe(r => this.roles.set(r));
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
    const edit = this.editando();
    if (!edit && !this.form.password?.trim()) {
      this.errorForm.set('Debes definir una contraseña para el nuevo usuario.');
      return;
    }
    this.guardando.set(true);
    this.errorForm.set(null);

    const payload = { ...this.form, email: this.form.email?.trim() || null };
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
        this.toast.error(msg);
      }
    });
  }

  toggleActivo(u: UsuarioAdmin) {
    this.svc.cambiarEstado(u.id, !u.activo).subscribe({
      next: () => {
        this.toast.info(!u.activo ? 'Usuario activado' : 'Usuario desactivado');
        this.cargar();
      },
      error: () => this.toast.error('No se pudo cambiar el estado.')
    });
  }

  volver() { this.router.navigate(['/ajustes']); }

  private formVacio(): Partial<UsuarioAdmin> {
    return { usuario: '', nombreCompleto: '', email: '', password: '', rolId: this.roles()[0]?.id, activo: true };
  }
}
