import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { NegocioDetalle } from '../../core/models/models';
import { NegociosPlataformaService } from '../../core/services/negocios-plataforma.service';
import { ToastService } from '../../core/services/toast.service';
import { IconComponent } from '../../shared/icon/icon.component';

@Component({
  selector: 'app-plataforma-negocio-detalle',
  imports: [CommonModule, FormsModule, IconComponent],
  templateUrl: './plataforma-negocio-detalle.component.html',
  styleUrl: './plataforma-negocio-detalle.component.scss'
})
export class PlataformaNegocioDetalleComponent implements OnInit {
  private readonly svc = inject(NegociosPlataformaService);
  private readonly toast = inject(ToastService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  private id = 0;
  readonly negocio = signal<NegocioDetalle | null>(null);
  readonly cargando = signal(true);
  readonly guardando = signal(false);

  // Edición de datos
  readonly editandoDatos = signal(false);
  formDatos = { nombre: '', rucEmpresa: '', titularNombre: '', titularEmail: '', titularCelular: '', notasInternas: '' };

  // Edición de suscripción
  readonly editandoSuscripcion = signal(false);
  formSusc = { planSuscripcion: 'BASICO', estadoSuscripcion: 'ACTIVA', montoMensual: 0, proximoPago: '' };
  readonly planes = ['BASICO', 'PRO', 'PREMIUM'];
  readonly estados = ['PRUEBA', 'ACTIVA', 'VENCIDA', 'SUSPENDIDA'];

  // Reset password
  readonly mostrarReset = signal(false);
  passwordNueva = '';
  readonly credencialesReset = signal<{ usuario: string; password: string } | null>(null);

  // Suspender
  readonly confirmarEstado = signal(false);

  readonly urlAcceso = computed(() => {
    const n = this.negocio();
    return n ? `${window.location.origin}/${n.slug}/login` : '';
  });

  ngOnInit() {
    this.id = Number(this.route.snapshot.paramMap.get('id'));
    this.cargar();
  }

  cargar() {
    this.cargando.set(true);
    this.svc.detalle(this.id).subscribe({
      next: n => { this.negocio.set(n); this.cargando.set(false); },
      error: () => { this.cargando.set(false); this.toast.error('No se pudo cargar la empresa.'); }
    });
  }

  volver() { this.router.navigate(['/plataforma']); }

  // ---------- Editar datos ----------
  abrirEdicionDatos() {
    const n = this.negocio(); if (!n) return;
    this.formDatos = {
      nombre: n.nombre,
      rucEmpresa: n.rucEmpresa ?? '',
      titularNombre: n.titularNombre ?? '',
      titularEmail: n.titularEmail ?? '',
      titularCelular: n.titularCelular ?? '',
      notasInternas: n.notasInternas ?? ''
    };
    this.editandoDatos.set(true);
  }

  guardarDatos() {
    this.guardando.set(true);
    this.svc.editar(this.id, {
      nombre: this.formDatos.nombre.trim(),
      rucEmpresa: this.formDatos.rucEmpresa.trim() || null,
      titularNombre: this.formDatos.titularNombre.trim() || null,
      titularEmail: this.formDatos.titularEmail.trim() || null,
      titularCelular: this.formDatos.titularCelular.trim() || null,
      notasInternas: this.formDatos.notasInternas.trim() || null
    }).subscribe({
      next: () => { this.guardando.set(false); this.editandoDatos.set(false); this.toast.exito('Datos actualizados'); this.cargar(); },
      error: (err: HttpErrorResponse) => { this.guardando.set(false); this.toast.error(err.error?.mensaje ?? 'No se pudo guardar.'); }
    });
  }

  // ---------- Suscripción ----------
  abrirEdicionSuscripcion() {
    const n = this.negocio(); if (!n) return;
    this.formSusc = {
      planSuscripcion: n.planSuscripcion,
      estadoSuscripcion: n.estadoSuscripcion,
      montoMensual: n.montoMensual,
      proximoPago: n.proximoPago ? n.proximoPago.substring(0, 10) : ''
    };
    this.editandoSuscripcion.set(true);
  }

  guardarSuscripcion() {
    this.guardando.set(true);
    this.svc.cambiarSuscripcion(this.id, {
      planSuscripcion: this.formSusc.planSuscripcion,
      estadoSuscripcion: this.formSusc.estadoSuscripcion,
      montoMensual: Number(this.formSusc.montoMensual) || 0,
      proximoPago: this.formSusc.proximoPago || null
    }).subscribe({
      next: () => { this.guardando.set(false); this.editandoSuscripcion.set(false); this.toast.exito('Suscripción actualizada'); this.cargar(); },
      error: (err: HttpErrorResponse) => { this.guardando.set(false); this.toast.error(err.error?.mensaje ?? 'No se pudo guardar.'); }
    });
  }

  /** Registrar el pago del mes: renueva el próximo pago +30 días y deja la suscripción activa. */
  registrarPago() {
    const n = this.negocio(); if (!n) return;
    const base = n.proximoPago && new Date(n.proximoPago) > new Date() ? new Date(n.proximoPago) : new Date();
    base.setDate(base.getDate() + 30);
    this.guardando.set(true);
    this.svc.cambiarSuscripcion(this.id, {
      planSuscripcion: n.planSuscripcion,
      estadoSuscripcion: 'ACTIVA',
      montoMensual: n.montoMensual,
      proximoPago: base.toISOString().substring(0, 10)
    }).subscribe({
      next: () => { this.guardando.set(false); this.toast.exito('Pago registrado. Próximo pago +30 días.'); this.cargar(); },
      error: () => { this.guardando.set(false); this.toast.error('No se pudo registrar el pago.'); }
    });
  }

  // ---------- Reset password ----------
  generarPassword() {
    // Contraseña legible de 9 chars: 3 sílabas (6) + 3 dígitos. Cumple el mínimo (letras + números, 8+).
    const s = ['la', 've', 'ro', 'mi', 'sa', 'to', 'ni', 'ba', 'lu', 'ca'];
    const pick = () => s[Math.floor(Math.random() * s.length)];
    const p = pick() + pick() + pick();
    this.passwordNueva = p.charAt(0).toUpperCase() + p.slice(1) + Math.floor(100 + Math.random() * 900);
  }

  confirmarReset() {
    if (this.passwordNueva.trim().length < 8) { this.toast.error('La contraseña debe tener al menos 8 caracteres.'); return; }
    this.guardando.set(true);
    this.svc.resetPasswordAdmin(this.id, this.passwordNueva.trim()).subscribe({
      next: res => {
        this.guardando.set(false);
        this.credencialesReset.set({ usuario: res.usuario, password: this.passwordNueva.trim() });
        this.mostrarReset.set(false);
        this.passwordNueva = '';
        this.toast.exito('Contraseña restablecida');
      },
      error: (err: HttpErrorResponse) => { this.guardando.set(false); this.toast.error(err.error?.mensaje ?? 'No se pudo restablecer.'); }
    });
  }

  copiar(texto: string) {
    navigator.clipboard?.writeText(texto).then(() => this.toast.info('Copiado'), () => {});
  }

  // ---------- Estado empresa ----------
  toggleEstado() {
    const n = this.negocio(); if (!n) return;
    if (n.activo) { this.confirmarEstado.set(true); return; }
    this.aplicarEstado(true);
  }

  confirmarSuspender() { this.aplicarEstado(false); this.confirmarEstado.set(false); }

  private aplicarEstado(activo: boolean) {
    this.svc.cambiarEstado(this.id, activo).subscribe({
      next: () => { this.toast.info(activo ? 'Empresa reactivada' : 'Empresa suspendida'); this.cargar(); },
      error: () => this.toast.error('No se pudo cambiar el estado.')
    });
  }

  // ---------- Helpers de vista ----------
  diasParaVencer(): number | null {
    const n = this.negocio();
    if (!n?.proximoPago) return null;
    const hoy = new Date(); hoy.setHours(0, 0, 0, 0);
    return Math.round((new Date(n.proximoPago).getTime() - hoy.getTime()) / 86_400_000);
  }

  estadoBadge(): { texto: string; clase: string } {
    const n = this.negocio();
    if (!n) return { texto: '', clase: '' };
    if (!n.activo) return { texto: 'Suspendida', clase: 'badge--gris' };
    const dias = this.diasParaVencer();
    if (n.estadoSuscripcion === 'VENCIDA' || (dias !== null && dias < 0)) return { texto: 'Vencida', clase: 'badge--rojo' };
    if (n.estadoSuscripcion === 'PRUEBA') return { texto: 'En prueba', clase: 'badge--azul' };
    if (dias !== null && dias <= 7) return { texto: `Vence en ${dias} días`, clase: 'badge--naranja' };
    return { texto: 'Al día', clase: 'badge--verde' };
  }
}
