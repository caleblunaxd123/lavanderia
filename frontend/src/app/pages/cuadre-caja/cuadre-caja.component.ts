import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { CajaService, UsuarioDelDia } from '../../core/services/caja.service';
import { AuthService } from '../../core/services/auth.service';
import { ToastService } from '../../core/services/toast.service';
import { MovimientoCaja, TipoGasto } from '../../core/models/models';
import { IconComponent } from '../../shared/icon/icon.component';

interface Denominacion {
  valor: number;
  cantidad: number;
}

@Component({
  selector: 'app-cuadre-caja',
  imports: [CommonModule, FormsModule, IconComponent],
  templateUrl: './cuadre-caja.component.html',
  styleUrl: './cuadre-caja.component.scss'
})
export class CuadreCajaComponent implements OnInit {
  private readonly cajaSvc = inject(CajaService);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);

  fecha = new Date().toISOString().slice(0, 10);
  guardado = false;

  // Cuadre por colaborador
  readonly usuariosDelDia = signal<UsuarioDelDia[]>([]);
  readonly cargandoUsuarios = signal(false);
  readonly usuarioSeleccionadoId = signal<number | null>(null);
  readonly usuarioSeleccionadoNombre = computed(() =>
    this.usuariosDelDia().find(u => u.id === this.usuarioSeleccionadoId())?.nombreCompleto ?? '');

  denominaciones = signal<Denominacion[]>([
    { valor: 100, cantidad: 0 },
    { valor: 50, cantidad: 0 },
    { valor: 20, cantidad: 1 },
    { valor: 10, cantidad: 2 },
    { valor: 5, cantidad: 3 },
    { valor: 2, cantidad: 0 },
    { valor: 1, cantidad: 0 },
    { valor: 0.5, cantidad: 0 },
    { valor: 0.2, cantidad: 0 },
    { valor: 0.1, cantidad: 0 },
  ]);

  cajaInicial = 0;
  readonly sugerenciaCajaInicial = signal<{ monto: number; usuarioNombre?: string; fecha: string } | null>(null);

  readonly movimientos = signal<MovimientoCaja[]>([]);
  readonly cargandoMovimientos = signal(false);
  readonly tiposGasto = signal<TipoGasto[]>([]);

  // Formulario de nuevo gasto
  readonly modalGasto = signal(false);
  gastoMonto = 0;
  gastoTipoId: number | '' = '';
  gastoMetodo: 'EFECTIVO' | 'YAPE' | 'PLIN' | 'TRANSFERENCIA' | 'POS' = 'EFECTIVO';
  gastoDescripcion = '';
  guardandoGasto = signal(false);

  ngOnInit() {
    this.cajaSvc.tiposGasto().subscribe(t => this.tiposGasto.set(t));
    // Por defecto, el usuario actualmente logueado es el seleccionado
    this.usuarioSeleccionadoId.set(this.auth.usuario()?.id ?? null);
    this.cargarUsuariosDelDia();
    this.cargarMovimientos();
    this.cargarSugerenciaCajaInicial();
    this.cargarCuadreExistente();
  }

  cambiarFecha(fecha: string) {
    this.fecha = fecha;
    this.cargarUsuariosDelDia();
    this.cargarMovimientos();
    this.cargarSugerenciaCajaInicial();
    this.cargarCuadreExistente();
  }

  cargarUsuariosDelDia() {
    this.cargandoUsuarios.set(true);
    this.cajaSvc.usuariosDelDia(this.fecha).subscribe({
      next: list => {
        this.usuariosDelDia.set(list);
        this.cargandoUsuarios.set(false);
        // Asegura que el usuario seleccionado esté en la lista; si no, elige el actual
        const sel = this.usuarioSeleccionadoId();
        if (sel && !list.some(u => u.id === sel)) {
          this.usuarioSeleccionadoId.set(this.auth.usuario()?.id ?? (list[0]?.id ?? null));
        }
      },
      error: () => this.cargandoUsuarios.set(false)
    });
  }

  seleccionarUsuario(id: number) {
    if (this.usuarioSeleccionadoId() === id) return;
    this.usuarioSeleccionadoId.set(id);
    this.cajaInicial = 0;
    this.guardado = false;
    this.cargarMovimientos();
    this.cargarCuadreExistente();
  }

  cargarCuadreExistente() {
    const uid = this.usuarioSeleccionadoId();
    if (!uid) return;
    this.cajaSvc.cuadreDelUsuario(this.fecha, uid).subscribe({
      next: c => {
        this.cajaInicial = c.cajaInicial;
        this.guardado = true;
      },
      error: () => { this.guardado = false; }
    });
  }

  cargarSugerenciaCajaInicial() {
    this.sugerenciaCajaInicial.set(null);
    this.cajaSvc.obtenerUltimoAnterior(this.fecha).subscribe({
      next: c => this.sugerenciaCajaInicial.set({ monto: c.cajaFinal, usuarioNombre: c.usuarioNombre, fecha: c.fecha }),
      error: () => {}
    });
  }

  usarSugerenciaCajaInicial() {
    const s = this.sugerenciaCajaInicial();
    if (s) this.cajaInicial = s.monto;
  }

  cargarMovimientos() {
    this.cargandoMovimientos.set(true);
    const uid = this.usuarioSeleccionadoId() ?? undefined;
    this.cajaSvc.movimientos(this.fecha, uid).subscribe({
      next: list => { this.movimientos.set(list); this.cargandoMovimientos.set(false); },
      error: () => this.cargandoMovimientos.set(false)
    });
  }

  gastosDelDia = computed(() => this.movimientos().filter(m => m.tipo === 'GASTO'));
  gastosTotal = computed(() => this.gastosDelDia().reduce((acc, m) => acc + m.monto, 0));

  ingresosDelDia = computed(() => this.movimientos().filter(m => m.tipo === 'INGRESO'));
  ingresosTotal = computed(() => this.ingresosDelDia().reduce((acc, m) => acc + m.monto, 0));

  ingresosPorMetodo = computed(() => {
    const acc = new Map<string, { monto: number; cantidad: number }>();
    for (const m of this.ingresosDelDia()) {
      const actual = acc.get(m.metodoPago) ?? { monto: 0, cantidad: 0 };
      acc.set(m.metodoPago, { monto: actual.monto + m.monto, cantidad: actual.cantidad + 1 });
    }
    return Array.from(acc.entries()).map(([metodo, v]) => ({ metodo, monto: v.monto, cantidad: v.cantidad }));
  });

  pagosDigitalesTotal = computed(() =>
    this.ingresosPorMetodo().filter(i => i.metodo !== 'EFECTIVO').reduce((acc, i) => acc + i.cantidad, 0)
  );

  pedidosPagadosEfectivo = computed(() =>
    this.ingresosDelDia().filter(m => m.metodoPago === 'EFECTIVO').reduce((acc, m) => acc + m.monto, 0)
  );

  gananciaNeta = computed(() => this.ingresosTotal() - this.gastosTotal());

  readonly etiquetasMetodo: Record<string, string> = {
    EFECTIVO: 'Efectivo', YAPE: 'Yape', PLIN: 'Plin', TRANSFERENCIA: 'Transferencia', POS: 'POS/Tarjeta'
  };

  abrirModalGasto() {
    this.gastoMonto = 0;
    this.gastoTipoId = '';
    this.gastoMetodo = 'EFECTIVO';
    this.gastoDescripcion = '';
    this.modalGasto.set(true);
  }

  confirmarGasto() {
    if (this.gastoMonto <= 0) return;
    this.guardandoGasto.set(true);
    this.cajaSvc.registrarGasto(
      this.gastoMonto,
      this.gastoMetodo,
      this.gastoTipoId ? (this.gastoTipoId as number) : null,
      this.gastoDescripcion.trim() || undefined
    ).subscribe({
      next: () => {
        this.guardandoGasto.set(false);
        this.modalGasto.set(false);
        this.toast.exito('Gasto registrado');
        this.cargarMovimientos();
      },
      error: () => {
        this.guardandoGasto.set(false);
        this.toast.error('No se pudo registrar el gasto.');
      }
    });
  }

  actualizarCantidad(valor: number, cantidad: number) {
    this.denominaciones.update(list =>
      list.map(d => d.valor === valor ? { ...d, cantidad: Math.max(0, cantidad) } : d)
    );
  }

  totalContado = computed(() =>
    this.denominaciones().reduce((acc, d) => acc + d.valor * d.cantidad, 0)
  );

  enCajaDeberiaHaber = computed(() =>
    this.cajaInicial + this.pedidosPagadosEfectivo() - this.gastosTotal()
  );

  diferencia = computed(() => this.totalContado() - this.enCajaDeberiaHaber());

  estadoCaja = computed<'SOBRA' | 'CUADRA' | 'FALTA'>(() => {
    const d = this.diferencia();
    if (Math.abs(d) < 0.01) return 'CUADRA';
    return d > 0 ? 'SOBRA' : 'FALTA';
  });

  guardando = signal(false);

  guardarCuadre() {
    if (this.guardando()) return;
    this.guardando.set(true);

    this.cajaSvc.guardarCuadre({
      fecha: this.fecha,
      cajaInicial: this.cajaInicial,
      pedidosPagadosEfect: this.pedidosPagadosEfectivo(),
      gastos: this.gastosTotal(),
      totalContado: this.totalContado(),
      diferencia: this.diferencia(),
      cajaFinal: this.enCajaDeberiaHaber(),
      observaciones: undefined,
    }).subscribe({
      next: guardado => {
        this.guardando.set(false);
        this.guardado = true;
        this.toast.exito('Cuadre guardado. Abriendo PDF…');
        // Abre la vista imprimible en pestaña nueva (auto-lanza print)
        window.open(`/cuadre-caja/imprimir/${guardado.id}`, '_blank');
      },
      error: () => {
        this.guardando.set(false);
        this.toast.error('No se pudo guardar el cuadre.');
      }
    });
  }
}
