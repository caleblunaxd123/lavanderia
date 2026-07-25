import { CommonModule } from '@angular/common';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { environment } from '../../../environments/environment';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ServicioEditable, ServiciosAdminService } from '../../core/services/servicios-admin.service';
import { Categoria, CategoriasService } from '../../core/services/categorias.service';
import { ToastService } from '../../core/services/toast.service';
import { EmptyStateComponent } from '../../shared/empty-state/empty-state.component';
import { PaginacionComponent } from '../../shared/paginacion/paginacion.component';
import { IconComponent } from '../../shared/icon/icon.component';
import { PageHeaderComponent } from '../../shared/page-header/page-header.component';

type FiltroEstadoServicio = 'todos' | 'activos' | 'inactivos';

interface FilaImportada {
  fila: number;
  nombre: string;
  precio: number;
  unidad: string;
  categoria: string | null;
  estado: 'ok' | 'error';
  motivo: string;
}

@Component({
  selector: 'app-ajustes-servicios',
  imports: [PageHeaderComponent, CommonModule, FormsModule, EmptyStateComponent, PaginacionComponent, IconComponent],
  templateUrl: './ajustes-servicios.component.html',
  styleUrl: './ajustes-servicios.component.scss'
})
export class AjustesServiciosComponent implements OnInit {
  private readonly svc = inject(ServiciosAdminService);
  private readonly categoriasSvc = inject(CategoriasService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  private readonly http = inject(HttpClient);

  readonly servicios = signal<ServicioEditable[]>([]);
  readonly categorias = signal<Categoria[]>([]);
  readonly cargando = signal(false);
  readonly error = signal<string | null>(null);
  readonly busqueda = signal('');
  readonly filtroEstado = signal<FiltroEstadoServicio>('todos');
  readonly filtroCategoria = signal<number | 'todas'>('todas');

  readonly serviciosFiltrados = computed(() => {
    const texto = this.normalizar(this.busqueda());
    const estado = this.filtroEstado();
    const categoria = this.filtroCategoria();
    return [...this.servicios()]
      .filter(s =>
        (!texto || this.normalizar(`${s.nombre} ${s.unidad} ${s.categoriaNombre ?? ''}`).includes(texto)) &&
        (estado === 'todos' || (estado === 'activos' ? s.activo : !s.activo)) &&
        (categoria === 'todas' || s.categoriaId === categoria)
      )
      .sort((a, b) => Number(b.activo) - Number(a.activo) || a.nombre.localeCompare(b.nombre, 'es'));
  });
  readonly totalActivos = computed(() => this.servicios().filter(s => s.activo).length);
  readonly totalInactivos = computed(() => this.servicios().length - this.totalActivos());
  readonly precioPromedio = computed(() => {
    const activos = this.servicios().filter(s => s.activo);
    return activos.length ? activos.reduce((suma, s) => suma + s.precio, 0) / activos.length : 0;
  });

  readonly pagina = signal(1);
  readonly tamanoPagina = signal(15);
  readonly serviciosPaginados = computed(() => {
    const inicio = (this.pagina() - 1) * this.tamanoPagina();
    return this.serviciosFiltrados().slice(inicio, inicio + this.tamanoPagina());
  });
  cambiarPagina(p: number) { this.pagina.set(p); }
  cambiarTamanoPagina(t: number) { this.tamanoPagina.set(t); this.pagina.set(1); }
  actualizarBusqueda(valor: string) { this.busqueda.set(valor); this.pagina.set(1); }
  actualizarEstado(valor: FiltroEstadoServicio) { this.filtroEstado.set(valor); this.pagina.set(1); }
  actualizarCategoria(valor: number | 'todas') { this.filtroCategoria.set(valor); this.pagina.set(1); }
  limpiarFiltros() {
    this.busqueda.set('');
    this.filtroEstado.set('todos');
    this.filtroCategoria.set('todas');
    this.pagina.set(1);
  }

  readonly modalAbierto = signal(false);
  readonly editando = signal<ServicioEditable | null>(null);
  readonly confirmarEliminar = signal<ServicioEditable | null>(null);
  readonly confirmarDesactivar = signal<ServicioEditable | null>(null);
  form: Partial<ServicioEditable> = this.formVacio();
  errorForm = signal<string | null>(null);
  guardando = signal(false);

  unidades = ['kg', 'prenda', 'pieza', 'und', 'servicio'];

  ngOnInit() {
    this.cargar();
    this.categoriasSvc.listar().subscribe(list => this.categorias.set(list));
  }

  cargar() {
    this.cargando.set(true);
    this.error.set(null);
    this.pagina.set(1);
    this.svc.listar().subscribe({
      next: list => { this.servicios.set(list); this.cargando.set(false); },
      error: (err: HttpErrorResponse) => {
        this.cargando.set(false);
        this.error.set(err.status === 0
          ? 'No se pudo conectar con el servidor.'
          : (err.error?.mensaje ?? 'Error al cargar servicios.'));
      }
    });
  }

  abrirCrear() {
    this.editando.set(null);
    this.form = this.formVacio();
    this.errorForm.set(null);
    this.modalAbierto.set(true);
  }

  abrirEditar(s: ServicioEditable) {
    this.editando.set(s);
    this.form = { ...s };
    this.errorForm.set(null);
    this.modalAbierto.set(true);
  }

  cerrar() {
    if (this.guardando()) return;
    this.modalAbierto.set(false);
  }

  guardar() {
    if (this.guardando()) return;
    const nombre = this.form.nombre?.trim() ?? '';
    const unidad = this.form.unidad?.trim() ?? '';
    const precio = Number(this.form.precio ?? 0);

    if (nombre.length < 2 || nombre.length > 120) {
      this.errorForm.set('El nombre debe tener entre 2 y 120 caracteres.');
      return;
    }
    if (!unidad) {
      this.errorForm.set('Selecciona la unidad de cobro del servicio.');
      return;
    }
    if (!Number.isFinite(precio) || precio <= 0 || precio > 10_000) {
      this.errorForm.set('Ingresa un precio mayor a S/ 0.00 y menor o igual a S/ 10,000.00.');
      return;
    }
    const editandoId = this.editando()?.id;
    const duplicado = this.servicios().some(s =>
      s.id !== editandoId && this.normalizar(s.nombre) === this.normalizar(nombre)
    );
    if (duplicado) {
      this.errorForm.set(`Ya existe un servicio llamado “${nombre}”. Edita el existente o usa un nombre diferente.`);
      return;
    }
    this.form = { ...this.form, nombre, unidad, precio: Math.round(precio * 100) / 100 };
    this.guardando.set(true);
    this.errorForm.set(null);

    const edit = this.editando();
    const obs$: import('rxjs').Observable<any> = edit
      ? this.svc.actualizar(edit.id, { ...edit, ...this.form } as ServicioEditable)
      : this.svc.crear(this.form);

    obs$.subscribe({
      next: () => {
        this.guardando.set(false);
        this.modalAbierto.set(false);
        this.toast.exito(edit ? 'Servicio actualizado' : 'Servicio creado');
        this.cargar();
      },
      error: (err: HttpErrorResponse) => {
        this.guardando.set(false);
        const msg = err.error?.mensaje ?? 'No se pudo guardar el servicio.';
        this.errorForm.set(msg);
        this.toast.desdeHttp(err, msg);
      }
    });
  }

  pedirEliminar(s: ServicioEditable) { this.confirmarEliminar.set(s); }

  eliminar() {
    const s = this.confirmarEliminar();
    if (!s) return;
    this.guardando.set(true);
    this.svc.desactivar(s.id).subscribe({
      next: res => {
        this.guardando.set(false);
        this.confirmarEliminar.set(null);
        this.toast.exito(res.mensaje);
        this.cargar();
      },
      error: (err: HttpErrorResponse) => {
        this.guardando.set(false);
        this.toast.desdeHttp(err, 'No se pudo desactivar.');
      }
    });
  }

  toggleActivo(s: ServicioEditable) {
    if (s.activo) { this.confirmarDesactivar.set(s); return; }
    this.aplicarCambioEstado(s, true);
  }

  confirmarDesactivarOk() {
    const s = this.confirmarDesactivar();
    if (!s) return;
    this.aplicarCambioEstado(s, false);
    this.confirmarDesactivar.set(null);
  }

  private aplicarCambioEstado(s: ServicioEditable, activo: boolean) {
    if (this.guardando()) return;
    this.guardando.set(true);
    const actualizado = { ...s, activo };
    this.svc.actualizar(s.id, actualizado).subscribe({
      next: () => {
        this.guardando.set(false);
        this.toast.info(activo ? 'Servicio reactivado' : 'Servicio desactivado');
        this.cargar();
      },
      error: (err: HttpErrorResponse) => {
        this.guardando.set(false);
        this.toast.desdeHttp(err, 'No se pudo cambiar el estado.');
      }
    });
  }

  // ---------- Importación masiva ----------
  readonly importarAbierto = signal(false);
  readonly importTexto = signal('');
  readonly importCrearCategorias = signal(true);
  readonly importando = signal(false);
  readonly importNombreArchivo = signal<string | null>(null);

  readonly importFilas = computed<FilaImportada[]>(() => {
    const texto = this.importTexto();
    if (!texto.trim()) return [];
    const filas = this.parsearTabla(texto);
    const vistos = new Set<string>();
    const existentes = new Set(this.servicios().map(s => this.normalizar(s.nombre)));
    return filas.map((cols, i) => this.evaluarFila(cols, i, vistos, existentes));
  });
  readonly importValidas = computed(() => this.importFilas().filter(f => f.estado === 'ok'));
  readonly importConError = computed(() => this.importFilas().filter(f => f.estado === 'error'));
  readonly importCategoriasNuevas = computed(() => {
    const existentes = new Set(this.categorias().map(c => this.normalizar(c.nombre)));
    const nuevas = new Set<string>();
    for (const f of this.importValidas()) {
      const c = (f.categoria ?? '').trim();
      if (c && !existentes.has(this.normalizar(c))) nuevas.add(c);
    }
    return [...nuevas];
  });

  abrirImportar() {
    this.importTexto.set('');
    this.importNombreArchivo.set(null);
    this.importCrearCategorias.set(true);
    this.importarAbierto.set(true);
  }
  cerrarImportar() {
    if (this.importando()) return;
    this.importarAbierto.set(false);
  }

  async onArchivoImport(event: Event) {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;
    this.importNombreArchivo.set(file.name);

    if (/\.(xlsx|xls)$/i.test(file.name)) {
      // Excel nativo: se convierte a CSV en el navegador y sigue el mismo flujo que un CSV.
      // La librería se carga bajo demanda (solo al importar) para no pesar en el arranque.
      try {
        const XLSX = await import('xlsx');
        const buffer = await file.arrayBuffer();
        const libro = XLSX.read(buffer, { type: 'array' });
        const hoja = libro.Sheets[libro.SheetNames[0]];
        this.importTexto.set(hoja ? XLSX.utils.sheet_to_csv(hoja, { FS: ';' }) : '');
      } catch {
        this.importTexto.set('');
        this.toast.error('No se pudo leer el archivo de Excel. Verifica que sea un .xlsx válido o usa un CSV.');
      }
    } else {
      const reader = new FileReader();
      reader.onload = () => this.importTexto.set(String(reader.result ?? ''));
      reader.readAsText(file, 'utf-8');
    }
    input.value = '';
  }

  descargarPlantilla() {
    // Plantilla Excel con estilo (encabezados, instrucciones y ejemplos) generada por el backend.
    this.http.get(`${environment.apiUrl}/plantillas/servicios`, { responseType: 'blob' }).subscribe({
      next: blob => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = 'plantilla-servicios.xlsx';
        a.click();
        URL.revokeObjectURL(url);
      },
      error: () => this.toast.error('No se pudo descargar la plantilla.')
    });
  }

  confirmarImportar() {
    if (this.importando()) return;
    const validas = this.importValidas();
    if (validas.length === 0) {
      this.toast.info('No hay filas válidas para importar.');
      return;
    }
    this.importando.set(true);
    const payload = validas.map(f => ({
      nombre: f.nombre,
      precio: f.precio,
      unidad: f.unidad,
      categoria: (f.categoria ?? '').trim() || null
    }));
    this.svc.importar(payload, this.importCrearCategorias()).subscribe({
      next: res => {
        this.importando.set(false);
        this.importarAbierto.set(false);
        const partes = [`${res.creados} servicio(s) creado(s)`];
        if (res.categoriasCreadas.length) partes.push(`${res.categoriasCreadas.length} categoría(s) nueva(s)`);
        if (res.omitidos) partes.push(`${res.omitidos} omitido(s)`);
        this.toast.exito(partes.join(' · '));
        this.categoriasSvc.listar().subscribe(list => this.categorias.set(list));
        this.cargar();
      },
      error: (err: HttpErrorResponse) => {
        this.importando.set(false);
        this.toast.desdeHttp(err, 'No se pudo importar el archivo.');
      }
    });
  }

  /** Divide el texto en filas de columnas. Detecta separador (tab, ; o ,) y omite la cabecera. */
  private parsearTabla(texto: string): string[][] {
    const lineas = texto.replace(/\r/g, '').split('\n').filter(l => l.trim().length > 0);
    if (lineas.length === 0) return [];
    const primera = lineas[0];
    const sep = primera.includes('\t') ? '\t' : primera.includes(';') ? ';' : ',';
    let filas = lineas.map(l => l.split(sep).map(c => c.trim().replace(/^"|"$/g, '')));
    // Omite la cabecera si la primera fila no tiene un precio numérico en la 2ª columna.
    if (filas.length && !Number.isFinite(this.parsearNumero(filas[0][1] ?? ''))) {
      filas = filas.slice(1);
    }
    return filas;
  }

  /** Convierte "S/ 6,50" / "6.50" / "1.234,56" a número. Devuelve NaN si no es válido. */
  private parsearNumero(valor: string): number {
    let s = (valor ?? '').replace(/[^0-9.,-]/g, '').trim();
    if (!s) return NaN;
    const tieneComa = s.includes(',');
    const tienePunto = s.includes('.');
    if (tieneComa && tienePunto) {
      // Formato europeo "1.234,56": el punto es de miles, la coma decimal.
      s = s.replace(/\./g, '').replace(',', '.');
    } else if (tieneComa) {
      s = s.replace(',', '.');
    }
    const n = Number(s);
    return Number.isFinite(n) ? n : NaN;
  }

  private evaluarFila(cols: string[], indice: number, vistos: Set<string>, existentes: Set<string>): FilaImportada {
    const nombre = (cols[0] ?? '').trim();
    const precio = this.parsearNumero(cols[1] ?? '');
    const unidad = (cols[2] ?? '').trim() || 'und';
    const categoria = (cols[3] ?? '').trim() || null;
    const base: FilaImportada = { fila: indice + 1, nombre, precio: Number.isFinite(precio) ? precio : 0, unidad, categoria, estado: 'ok', motivo: '' };

    if (nombre.length < 2 || nombre.length > 120) return { ...base, estado: 'error', motivo: 'Nombre inválido' };
    if (!Number.isFinite(precio) || precio < 0.01 || precio > 10000) return { ...base, estado: 'error', motivo: 'Precio inválido' };
    const clave = this.normalizar(nombre);
    if (vistos.has(clave)) return { ...base, estado: 'error', motivo: 'Repetido en el archivo' };
    vistos.add(clave);
    if (existentes.has(clave)) return { ...base, estado: 'error', motivo: 'Ya existe' };
    return base;
  }

  volver() { this.router.navigate(['/ajustes']); }

  private formVacio(): Partial<ServicioEditable> {
    return { nombre: '', precio: 0, unidad: 'prenda', categoriaId: null, activo: true };
  }

  private normalizar(valor: string): string {
    return valor.normalize('NFD').replace(/[\u0300-\u036f]/g, '').trim().toLowerCase();
  }
}
