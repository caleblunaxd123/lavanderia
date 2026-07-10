export interface UsuarioSesion {
  id: number;
  usuario: string;
  nombreCompleto: string;
  rol: 'ADMIN' | 'COORDINADOR' | 'TRABAJADOR' | 'PROPIETARIO' | string;
  modulosPermitidos: string[];
  negocioId: number;
  sedeId: number | null;
  sedeNombre?: string | null;
}

export interface Sede {
  id: number;
  nombre: string;
  direccion?: string | null;
  telefono?: string | null;
  activo: boolean;
}

export interface LoginRequest {
  usuario: string;
  password: string;
  empresaSlug?: string;
}

export interface LoginResponse {
  accessToken: string;
  expira: string;
  usuario: UsuarioSesion;
}

export interface ConfiguracionNegocio {
  id: number;
  nombreNegocio: string;
  logoUrl?: string | null;
  colorPrimario: string;
  colorSecundario: string;
  colorAcento: string;
  direccion?: string | null;
  telefono?: string | null;
  ruc?: string | null;
  horarioAtencion?: string | null;
  igv: number;
  metaMensual: number;
  solesPorPunto: number;
  anchoTicketMm: number;
  mensajePieTicket?: string | null;
  condicionesServicio?: string | null;
  notasProduccion?: string | null;
  costoDelivery: number;
  servicioDeliveryId?: number | null;
}

export interface Cliente {
  id: number;
  nombre: string;
  celular?: string | null;
  dni?: string | null;
  documentoFiscal?: string | null;
  direccion?: string | null;
  puntos: number;
  fechaCreacion?: string;
}

export interface Servicio {
  id: number;
  nombre: string;
  precio: number;
  unidad: string;
  categoriaId?: number | null;
}

export interface AreaLavado {
  id: number;
  nombre: string;
  orden: number;
  tiempoEstMinutos: number;
}

export interface PedidoItem {
  id?: number;
  servicioId: number;
  servicioNombre?: string;
  cantidad: number;
  precioUnit: number;
  total: number;
  descripcion?: string | null;
}

export type EstadoPago = 'PENDIENTE' | 'PARCIAL' | 'PAGADO';
export type EstadoProceso = 'PENDIENTE' | 'EN_PROCESO' | 'LISTO' | 'ENTREGADO' | 'ANULADO';
export type ModalidadPedido = 'Tienda' | 'Recojo' | 'Delivery';

export interface Pedido {
  id: number;
  numero: number;
  clienteId: number;
  clienteNombre?: string;
  clienteCelular?: string;
  clienteDni?: string | null;
  usuarioNombre?: string | null;
  fechaIngreso: string;
  fechaEntregaEst?: string | null;
  modalidad: ModalidadPedido;
  subtotal: number;
  descuento: number;
  esUrgente: boolean;
  recargoUrgente: number;
  redondeo: number;
  total: number;
  montoPagado: number;
  estadoPago: EstadoPago;
  estadoProceso: EstadoProceso;
  areaActualId?: number | null;
  areaActualNombre?: string | null;
  observaciones?: string | null;
  anulado: boolean;
  motivoAnulacion?: string | null;
  codigoAntiguo?: string | null;
  items: PedidoItem[];
}

export interface TipoGasto {
  id: number;
  nombre: string;
}

export interface MovimientoCaja {
  id: number;
  fecha: string;
  tipo: 'INGRESO' | 'GASTO';
  metodoPago: string;
  monto: number;
  descripcion?: string | null;
  pedidoId?: number | null;
  tipoGastoId?: number | null;
  tipoGastoNombre?: string | null;
}

export interface PedidoAbandonado {
  pedidoId: number;
  numero: number;
  clienteNombre: string;
  clienteCelular?: string | null;
  total: number;
  montoPagado: number;
  fechaListo: string;
  diasEsperando: number;
}

export interface NegocioResumen {
  id: number;
  nombre: string;
  slug: string;
  activo: boolean;
  fechaCreacion: string;
  cantidadSedes: number;
  cantidadUsuarios: number;
}

export interface CrearNegocioRequest {
  nombre: string;
  slug: string;
  rucEmpresa?: string | null;
  titularNombre?: string | null;
  titularEmail?: string | null;
  sedeNombre: string;
  adminUsuario: string;
  adminNombreCompleto: string;
  adminEmail?: string | null;
  adminPassword: string;
}

export interface CrearPedidoRequest {
  clienteId?: number;
  clienteNuevo?: Partial<Cliente>;
  modalidad: ModalidadPedido;
  items: Array<{
    servicioId: number;
    cantidad: number;
    precioUnit: number;
    total: number;
    descripcion?: string | null;
  }>;
  descuentoPct: number;
  esUrgente: boolean;
  recargoUrgentePct: number;
  montoPagado: number;
  metodoPagoInicial: string;
  fechaEntregaEst?: string | null;
  observaciones?: string | null;
  areaInicialId?: number | null;
  fechaIngreso?: string | null;
  codigoAntiguo?: string | null;
}
