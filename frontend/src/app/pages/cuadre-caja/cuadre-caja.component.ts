import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { CajaService, UsuarioDelDia } from '../../core/services/caja.service';
import { AuthService } from '../../core/services/auth.service';
import { ToastService } from '../../core/services/toast.service';
import { MovimientoCaja, TipoGasto } from '../../core/models/models';
import { IconComponent } from '../../shared/icon/icon.component';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';

interface Denominacion {
  valor: number;
  cantidad: number;
}

function fechaLocalIso(fecha: Date): string {
  const anio = fecha.getFullYear();
  const mes = String(fecha.getMonth() + 1).padStart(2, '0');
  const dia = String(fecha.getDate()).padStart(2, '0');
  return `${anio}-${mes}-${dia}`;
}

@Component({
  selector: 'app-cuadre-caja',
  imports: [CommonModule, FormsModule, IconComponent, PageHeaderComponent],
  templateUrl: './cuadre-caja.component.html',
  styleUrl: './cuadre-caja.component.scss'
})
export class CuadreCajaComponent implements OnInit {
  private readonly cajaSvc = inject(CajaService);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  private readonly route = inject(ActivatedRoute);

  fecha = fechaLocalIso(new Date());
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

  cajaInicial = signal(0);  // signal para que enCajaDeberiaHaber/diferencia recalculen al tipear
  corte = 0;        // efectivo que se entrega/retira al cierre
  nota = '';        // observación libre del cuadre
  // false = solo los movimientos del colaborador seleccionado; true = toda la caja del día (todos).
  readonly verTodos = signal(false);
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
    // Si venimos del reporte con ?fecha=YYYY-MM-DD, cuadrar ese día.
    const fechaQp = this.route.snapshot.queryParamMap.get('fecha');
    if (fechaQp) this.fecha = fechaQp;
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
    this.cajaInicial.set(0);
    this.corte = 0;
    this.nota = '';
    this.guardado = false;
    this.cargarMovimientos();
    this.cargarCuadreExistente();
  }

  cargarCuadreExistente() {
    const uid = this.usuarioSeleccionadoId();
    if (!uid) return;
    this.cajaSvc.cuadreDelUsuario(this.fecha, uid).subscribe({
      next: c => {
        this.cajaInicial.set(c.cajaInicial);
        this.corte = c.corte ?? 0;
        this.nota = c.nota ?? '';
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
    if (s) this.cajaInicial.set(s.monto);
  }

  alternarVerTodos() {
    this.verTodos.set(!this.verTodos());
    this.cargarMovimientos();
  }

  cargarMovimientos() {
    this.cargandoMovimientos.set(true);
    const uid = this.verTodos() ? undefined : (this.usuarioSeleccionadoId() ?? undefined);
    this.cajaSvc.movimientos(this.fecha, uid).subscribe({
      next: list => { this.movimientos.set(list); this.cargandoMovimientos.set(false); },
      error: () => this.cargandoMovimientos.set(false)
    });
  }

  gastosDelDia = computed(() => this.movimientos().filter(m => m.tipo === 'GASTO'));
  gastosTotal = computed(() => this.gastosDelDia().reduce((acc, m) => acc + m.monto, 0));
  // Solo los gastos EN EFECTIVO salen del cajón físico; los digitales (Yape/Plin) no.
  gastosEfectivo = computed(() =>
    this.gastosDelDia().filter(m => m.metodoPago === 'EFECTIVO').reduce((acc, m) => acc + m.monto, 0)
  );

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

  // Tabla "Movimientos Digitales": por cada método NO efectivo, cuánto se pagó y cuánto se gastó.
  readonly metodosDigitales = ['YAPE', 'PLIN', 'TRANSFERENCIA', 'POS'];
  movimientosDigitales = computed(() => {
    const acc = new Map<string, { pago: number; gasto: number }>();
    for (const m of this.movimientos()) {
      if (m.metodoPago === 'EFECTIVO') continue;
      const cur = acc.get(m.metodoPago) ?? { pago: 0, gasto: 0 };
      if (m.tipo === 'INGRESO') cur.pago += m.monto; else cur.gasto += m.monto;
      acc.set(m.metodoPago, cur);
    }
    return Array.from(acc.entries()).map(([metodo, v]) => ({ metodo, pago: v.pago, gasto: v.gasto }));
  });

  // Detalle pago por pago de los INGRESOS digitales (Yape/Plin/Transferencia/POS/Tarjeta),
  // ordenado por hora — el operador puede reconciliar cada pago contra su app.
  ingresosDigitalesDetalle = computed(() =>
    this.ingresosDelDia()
      .filter(m => m.metodoPago !== 'EFECTIVO')
      .sort((a, b) => a.fecha.localeCompare(b.fecha))
  );
  ingresosDigitalesTotal = computed(() =>
    this.ingresosDigitalesDetalle().reduce((acc, m) => acc + m.monto, 0)
  );

  ingresosBilleteras = computed(() =>
    this.ingresosDelDia().filter(m => ['YAPE', 'PLIN'].includes(m.metodoPago)).reduce((a, m) => a + m.monto, 0)
  );

  ingresosTransferencia = computed(() =>
    this.ingresosDelDia().filter(m => m.metodoPago === 'TRANSFERENCIA').reduce((a, m) => a + m.monto, 0)
  );

  // Ingresos digitales agrupados para el reporte: transferencia móvil (Yape/Plin/Transferencia) vs tarjeta (POS).
  ingresosTransferenciaMovil = computed(() =>
    this.ingresosDelDia().filter(m => ['YAPE', 'PLIN', 'TRANSFERENCIA'].includes(m.metodoPago)).reduce((a, m) => a + m.monto, 0)
  );
  ingresosTarjeta = computed(() =>
    this.ingresosDelDia().filter(m => ['POS', 'TARJETA'].includes(m.metodoPago)).reduce((a, m) => a + m.monto, 0)
  );

  pagosDigitalesTotal = computed(() =>
    this.ingresosPorMetodo().filter(i => i.metodo !== 'EFECTIVO').reduce((acc, i) => acc + i.cantidad, 0)
  );

  // Detalle itemizado de pagos, agrupado por método (para las tablas al pie del cuadre).
  detalleIngresos = computed(() => {
    const orden = ['EFECTIVO', 'YAPE', 'PLIN', 'TRANSFERENCIA', 'POS', 'TARJETA'];
    const grupos = new Map<string, MovimientoCaja[]>();
    for (const m of this.ingresosDelDia()) {
      const lista = grupos.get(m.metodoPago) ?? [];
      lista.push(m);
      grupos.set(m.metodoPago, lista);
    }
    return Array.from(grupos.entries())
      .map(([metodo, items]) => ({ metodo, items, total: items.reduce((a, x) => a + x.monto, 0) }))
      .sort((a, b) => orden.indexOf(a.metodo) - orden.indexOf(b.metodo));
  });

  pedidosPagadosEfectivo = computed(() =>
    this.ingresosDelDia().filter(m => m.metodoPago === 'EFECTIVO').reduce((acc, m) => acc + m.monto, 0)
  );

  gananciaNeta = computed(() => this.ingresosTotal() - this.gastosTotal());

  readonly etiquetasMetodo: Record<string, string> = {
    EFECTIVO: 'Efectivo', YAPE: 'Yape', PLIN: 'Plin', TRANSFERENCIA: 'Transferencia', POS: 'POS/Tarjeta', TARJETA: 'Tarjeta'
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
      error: err => {
        this.guardandoGasto.set(false);
        this.toast.desdeHttp(err, 'No se pudo registrar el gasto.');
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
    this.cajaInicial() + this.pedidosPagadosEfectivo() - this.gastosEfectivo()
  );

  diferencia = computed(() => this.totalContado() - this.enCajaDeberiaHaber());

  // Remanente físico que queda tras entregar el corte (getter: depende del campo plano `corte`).
  get cajaFinalRemanente(): number { return this.totalContado() - this.corte; }

  estadoCaja = computed<'SOBRA' | 'CUADRA' | 'FALTA'>(() => {
    const d = this.diferencia();
    if (Math.abs(d) < 0.01) return 'CUADRA';
    return d > 0 ? 'SOBRA' : 'FALTA';
  });

  guardando = signal(false);
  confirmarRegrabar = signal(false);

  guardarCuadre() {
    if (this.guardando()) return;
    // Ya existe un cuadre guardado para esta fecha/usuario: confirmar antes de sobrescribirlo
    // en vez de reemplazarlo en silencio (el conteo anterior se pierde sin aviso).
    if (this.guardado) {
      this.confirmarRegrabar.set(true);
      return;
    }
    this.procederGuardado();
  }

  confirmarRegrabarCuadre() {
    this.confirmarRegrabar.set(false);
    this.procederGuardado();
  }

  private procederGuardado() {
    this.guardando.set(true);

    this.cajaSvc.guardarCuadre({
      fecha: this.fecha,
      usuarioId: this.usuarioSeleccionadoId() ?? undefined,
      cajaInicial: this.cajaInicial(),
      pedidosPagadosEfect: this.pedidosPagadosEfectivo(),
      gastos: this.gastosEfectivo(),
      totalContado: this.totalContado(),
      diferencia: this.diferencia(),
      cajaFinal: this.cajaFinalRemanente,
      corte: this.corte,
      ingresosDigital: this.ingresosTransferenciaMovil(),
      ingresosTarjeta: this.ingresosTarjeta(),
      nota: this.nota.trim() || undefined,
      observaciones: undefined,
    }).subscribe({
      next: guardado => {
        this.guardando.set(false);
        this.guardado = true;
        this.toast.exito('Cuadre guardado. Abriendo PDF…');
        // Abre la vista imprimible en pestaña nueva (auto-lanza print)
        window.open(`/cuadre-caja/imprimir/${guardado.id}`, '_blank');
      },
      error: err => {
        this.guardando.set(false);
        this.toast.desdeHttp(err, 'No se pudo guardar el cuadre.');
      }
    });
  }
}
