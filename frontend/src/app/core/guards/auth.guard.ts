import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../services/auth.service';

export const authGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (auth.autenticado()) return true;
  router.navigate(['/login']);
  return false;
};

export const rolGuard = (rolesPermitidos: string[]): CanActivateFn => () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (!auth.autenticado()) {
    router.navigate(['/login']);
    return false;
  }
  const rol = auth.usuario()?.rol;
  if (rol && rolesPermitidos.includes(rol)) return true;
  router.navigate(['/pedidos']);
  return false;
};

export const moduloGuard = (modulo: string): CanActivateFn => () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (!auth.autenticado()) {
    router.navigate(['/login']);
    return false;
  }
  const usuario = auth.usuario();
  const modulos = usuario?.modulosPermitidos;
  // Sesión guardada antes de existir este campo (o corrupta): forzar re-login en vez de
  // redirigir a una ruta que también podría estar protegida (evita loops de navegación).
  if (!modulos) {
    auth.logout();
    return false;
  }
  // Admin con acceso a varias sedes que aún no eligió con cuál trabajar.
  if (!usuario?.sedeId) {
    router.navigate(['/seleccionar-sede']);
    return false;
  }
  if (modulos.includes(modulo)) return true;
  if (modulo !== 'INICIO') {
    router.navigate(['/inicio']);
  } else {
    auth.logout();
  }
  return false;
};
