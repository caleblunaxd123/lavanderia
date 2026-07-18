using System.ComponentModel.DataAnnotations;

namespace Lavanderia.Api.Dtos;

// ---------- Auth ----------
public record LoginRequest(
    [Required, StringLength(60, MinimumLength = 3)] string Usuario,
    [Required, StringLength(200, MinimumLength = 4)] string Password,
    string? EmpresaSlug = null);

public record LoginResponse(string AccessToken, DateTime Expira, string RefreshToken, UsuarioDto Usuario);

public record UsuarioDto(
    int Id, string Usuario, string NombreCompleto, string Rol, List<string> ModulosPermitidos,
    int NegocioId, int? SedeId, string? SedeNombre);

public record SeleccionarSedeRequest([Required] int SedeId);

public record RefreshTokenRequest([Required] string RefreshToken);

// ---------- Sedes ----------
public class SedeDto
{
    public int Id { get; set; }
    [Required, StringLength(120, MinimumLength = 2)] public string Nombre { get; set; } = "";
    [StringLength(200)] public string? Direccion { get; set; }
    [StringLength(30)] public string? Telefono { get; set; }
    public bool Activo { get; set; } = true;
}

// ---------- Usuarios (administración) ----------
public class UsuarioAdminDto
{
    public int Id { get; set; }
    [Required, StringLength(60, MinimumLength = 3)] public string Usuario { get; set; } = "";
    [Required, StringLength(120, MinimumLength = 2)] public string NombreCompleto { get; set; } = "";
    [EmailAddress, StringLength(120)] public string? Email { get; set; }
    [StringLength(200)] public string? Password { get; set; }
    [Required] public int RolId { get; set; }
    public int? SedeId { get; set; }
    public string? SedeNombre { get; set; }
    public string? RolCodigo { get; set; }
    public string? RolNombre { get; set; }
    public bool Activo { get; set; } = true;
}

public record RolDto(int Id, string Codigo, string Nombre);
public record CambiarEstadoUsuarioRequest(bool Activo);

// ---------- Permisos ----------
public record PermisoItemDto(int RolId, string Modulo, bool PuedeAcceder);

public class ActualizarPermisosRequest
{
    [Required, MinLength(1)] public List<PermisoItemDto> Permisos { get; set; } = new();
}

// ---------- Configuracion Negocio ----------
public class ConfiguracionNegocioDto
{
    public int Id { get; set; }
    [Required, StringLength(120, MinimumLength = 2)]
    public string NombreNegocio { get; set; } = "";
    [Url] public string? LogoUrl { get; set; }
    public string ColorPrimario { get; set; } = "#0b57d0";
    public string ColorSecundario { get; set; } = "#29b6f6";
    public string ColorAcento { get; set; } = "#f5a623";
    [StringLength(200)] public string? Direccion { get; set; }
    [StringLength(30)] public string? Telefono { get; set; }
    [StringLength(20)] public string? Ruc { get; set; }
    [StringLength(120)] public string? HorarioAtencion { get; set; }
    [Range(0, 100)] public decimal Igv { get; set; } = 18m;
    public decimal MetaMensual { get; set; }
    [Range(0.01, 100000)] public decimal SolesPorPunto { get; set; } = 1m;
    [Range(50, 120)] public int AnchoTicketMm { get; set; } = 80;
    [StringLength(300)] public string? MensajePieTicket { get; set; }
    [StringLength(6000)] public string? CondicionesServicio { get; set; }
    [StringLength(500)] public string? NotasProduccion { get; set; }
    [Range(0, 1000)] public decimal CostoDelivery { get; set; }
    [Range(0, 100)] public decimal ValorPuntoCanje { get; set; }   // S/ que vale 1 punto al canjear (0 = off)
    [Range(0, 100)] public decimal MaxDescuentoPct { get; set; }    // tope de descuento manual (0 = sin tope)
    // Solo lectura: id del servicio de sistema al que Registrar debe apuntar al agregar
    // automaticamente el cargo de delivery al carrito (ver 022_costo_delivery.sql). Se ignora
    // si viene en el body de un PUT.
    public int? ServicioDeliveryId { get; set; }
}

// ---------- Cliente ----------
public class ClienteDto
{
    public int Id { get; set; }
    [Required, StringLength(120, MinimumLength = 2)]
    public string Nombre { get; set; } = "";
    [StringLength(20)] public string? Celular { get; set; }
    [StringLength(15)] public string? Dni { get; set; }
    [StringLength(20)] public string? DocumentoFiscal { get; set; }
    [StringLength(200)] public string? Direccion { get; set; }
    public int Puntos { get; set; }
    public DateTime? FechaCreacion { get; set; }
    public DateOnly? FechaNacimiento { get; set; }
}

public record ClienteFrecuenteDto(int ClienteId, string Nombre, string? Celular, int Visitas);
public record FusionarClientesRequest([Required] int OrigenId, [Required] int DestinoId);

// ---------- CRM ----------
public record ClienteAnaliticaDto(int ClienteId, string Nombre, string? Celular, int TotalPedidos, decimal TicketPromedio, DateTime UltimaCompra, int DiasSinComprar, decimal DeudaTotal);
public record ClienteCumpleanosDto(int ClienteId, string Nombre, string? Celular, DateOnly FechaNacimiento, int DiasParaCumpleanos);

public class MovimientoPuntosDto
{
    public int Id { get; set; }
    public int ClienteId { get; set; }
    public DateTime Fecha { get; set; }
    public string Motivo { get; set; } = "";
    public int Puntos { get; set; }
    public string Tipo { get; set; } = "SUMA";
    public string? UsuarioNombre { get; set; }
}

public class CrearMovimientoPuntosRequest
{
    [Required, StringLength(200, MinimumLength = 2)] public string Motivo { get; set; } = "";
    [Range(1, 100000)] public int Puntos { get; set; }
    [Required] public string Tipo { get; set; } = "SUMA";
}

// ---------- Paginación ----------
public class PagedResultDto<T>
{
    public List<T> Items { get; set; } = new();
    public int Total { get; set; }
    public int Pagina { get; set; }
    public int TamanoPagina { get; set; }
}

// ---------- Pedido ----------
public class PedidoItemDto
{
    public int Id { get; set; }
    [Range(1, int.MaxValue)] public int ServicioId { get; set; }
    public string? ServicioNombre { get; set; }
    public string? ServicioUnidad { get; set; }
    [Range(0.01, 10000)] public decimal Cantidad { get; set; }
    public decimal PrecioUnit { get; set; }
    public decimal Total { get; set; }
    [StringLength(200)] public string? Descripcion { get; set; }
}

public class CrearPedidoRequest
{
    public int? ClienteId { get; set; }
    public ClienteDto? ClienteNuevo { get; set; }
    [Required] public string Modalidad { get; set; } = "Tienda";
    [StringLength(250)] public string? DireccionEntrega { get; set; }
    [StringLength(100)] public string? DistritoEntrega { get; set; }
    [StringLength(250)] public string? ReferenciaEntrega { get; set; }
    [Range(-90d, 90d)] public decimal? LatitudEntrega { get; set; }
    [Range(-180d, 180d)] public decimal? LongitudEntrega { get; set; }
    [Required, MinLength(1)] public List<PedidoItemDto> Items { get; set; } = new();
    [Range(0, 100)] public decimal DescuentoPct { get; set; }
    [Range(0, 100000)] public int? PuntosACanjear { get; set; }
    public bool EsUrgente { get; set; }
    [Range(0, 100)] public decimal RecargoUrgentePct { get; set; } = 20m;
    // Tarifa de domicilio acordada para este pedido. Si no llega, se usa la tarifa
    // configurada por el negocio como valor por defecto.
    [Range(0, 10000)] public decimal? CostoDelivery { get; set; }
    [Range(0, 1000000)] public decimal MontoPagado { get; set; }
    [Required, StringLength(30)] public string MetodoPagoInicial { get; set; } = "EFECTIVO";
    public DateTime? FechaEntregaEst { get; set; }
    [StringLength(500)] public string? Observaciones { get; set; }
    public int? AreaInicialId { get; set; }
    public DateTime? FechaIngreso { get; set; }
    [StringLength(30)] public string? CodigoAntiguo { get; set; }
}

public class PedidoDto
{
    public int Id { get; set; }
    public int Numero { get; set; }
    public int ClienteId { get; set; }
    public string? ClienteNombre { get; set; }
    public string? ClienteCelular { get; set; }
    public string? ClienteDni { get; set; }
    public int ClientePuntos { get; set; }
    public string? UsuarioNombre { get; set; }
    public DateTime FechaIngreso { get; set; }
    public DateTime? FechaEntregaEst { get; set; }
    public string Modalidad { get; set; } = "";
    public string? DireccionEntrega { get; set; }
    public string? DistritoEntrega { get; set; }
    public string? ReferenciaEntrega { get; set; }
    public decimal? LatitudEntrega { get; set; }
    public decimal? LongitudEntrega { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Descuento { get; set; }
    public bool EsUrgente { get; set; }
    public decimal RecargoUrgente { get; set; }
    public decimal Redondeo { get; set; }
    public decimal Total { get; set; }
    public decimal MontoPagado { get; set; }
    public string EstadoPago { get; set; } = "";
    public string EstadoProceso { get; set; } = "";
    public int? AreaActualId { get; set; }
    public string? AreaActualNombre { get; set; }
    public string? Observaciones { get; set; }
    public bool Anulado { get; set; }
    public string? MotivoAnulacion { get; set; }
    public string? CodigoAntiguo { get; set; }
    public int? MotorizadoId { get; set; }
    public string? MotorizadoNombre { get; set; }
    public string? MotorizadoCelular { get; set; }
    public List<PedidoItemDto> Items { get; set; } = new();
}

public record AvanzarAreaRequest(int? NuevaAreaId, string NuevoEstado, string? Nota);

public class PedidoHistorialDto
{
    public int Id { get; set; }
    public int? AreaId { get; set; }
    public string? AreaNombre { get; set; }
    public string EstadoProceso { get; set; } = "";
    public DateTime Fecha { get; set; }
    public string? Nota { get; set; }
    public bool NotificadoWsp { get; set; }
}

public class DashboardDto
{
    public Dictionary<string, int> PedidosPorEstado { get; set; } = new();
    public List<AreaConteoDto> PedidosPorArea { get; set; } = new();
    public decimal VentasDelDia { get; set; }
    public decimal? CobradoDelDia { get; set; }
    public decimal? SaldoPorCobrar { get; set; }
    public decimal? CajaEsperadaHoy { get; set; }
    public int PedidosEntregadosHoy { get; set; }
    public int PedidosEntregadosTiendaHoy { get; set; }
    public int PedidosEntregadosDomicilioHoy { get; set; }
    public int PedidosEntregadosSemana { get; set; }
    public int PedidosEntregadosMes { get; set; }
    public int TotalPendientes { get; set; }
    public int TotalListos { get; set; }
    public int TotalEnProceso { get; set; }
    public int PedidosDelMes { get; set; }
    public decimal MetaMensual { get; set; }
    public int? InsumosBajoStock { get; set; }
    public int? ComprobantesPendientes { get; set; }
    public int? ComprobantesRechazados { get; set; }
    public List<SlaAreaDto> SlaPorArea { get; set; } = new();
    public int TotalPedidosEstancados { get; set; }
    public List<PedidoEstancadoDto> PedidosEstancados { get; set; } = new();
    public int TotalPedidosAbandonados { get; set; }
    public List<PedidoAbandonadoDto> PedidosAbandonados { get; set; } = new();
    public DateTime ActualizadoEn { get; set; }
}

public class PedidoContadoresDto
{
    public int PedidosDelMes { get; set; }
    public int TotalPendientes { get; set; }
    public int TotalOtros { get; set; }
    public int TotalUltimos { get; set; }
}

public record AreaConteoDto(int AreaId, string AreaNombre, int Cantidad);

public record PedidoAbandonadoDto(
    int PedidoId, int Numero, string ClienteNombre, string? ClienteCelular,
    decimal Total, decimal MontoPagado, DateTime FechaListo, int DiasEsperando);

public class RegistrarPagoRequest
{
    [Range(0.01, 100000)] public decimal Monto { get; set; }
    [Required] public string Metodo { get; set; } = "EFECTIVO";  // EFECTIVO | YAPE | PLIN | TRANSFERENCIA | POS | TARJETA
    [StringLength(300)] public string? Descripcion { get; set; }
}

public class AgregarItemRequest
{
    [Range(1, int.MaxValue)] public int ServicioId { get; set; }
    [Range(0.01, 10000)] public decimal Cantidad { get; set; }
    [StringLength(200)] public string? Descripcion { get; set; }
}

public record AnularPedidoRequest([Required, StringLength(200, MinimumLength = 3)] string Motivo);

public class CambiarFechaEntregaRequest
{
    [Required] public DateTime Fecha { get; set; }
    [StringLength(200)] public string? Motivo { get; set; }
}

// Servicios editables
public class ServicioEditableDto
{
    public int Id { get; set; }
    [Required, StringLength(120, MinimumLength = 2)] public string Nombre { get; set; } = "";
    [Range(0.01, 10000)] public decimal Precio { get; set; }
    [Required, StringLength(30)] public string Unidad { get; set; } = "";
    public int? CategoriaId { get; set; }
    public string? CategoriaNombre { get; set; }
    public bool Activo { get; set; } = true;
}

// ---------- Categorías ----------
public class CategoriaDto
{
    public int Id { get; set; }
    [Required, StringLength(80, MinimumLength = 2)] public string Nombre { get; set; } = "";
    public bool Activa { get; set; } = true;
}

// ---------- Tipos de gasto ----------
public class TipoGastoEditableDto
{
    public int Id { get; set; }
    [Required, StringLength(80, MinimumLength = 2)] public string Nombre { get; set; } = "";
    public bool Activo { get; set; } = true;
}

// ---------- Inventario de consumibles ----------
public class InsumoDto
{
    public int Id { get; set; }
    [Required, StringLength(80, MinimumLength = 2)] public string Nombre { get; set; } = "";
    [Required, StringLength(20)] public string UnidadMedida { get; set; } = "";
    public decimal StockActual { get; set; }
    [Range(0, 1000000)] public decimal StockMinimo { get; set; }
    public bool Activo { get; set; } = true;
    public DateTime? UltimaCompra { get; set; }
}

public class RegistrarMovimientoInsumoRequest
{
    [Required] public string Tipo { get; set; } = "";  // COMPRA | CONSUMO | AJUSTE
    [Range(-1000000, 1000000)] public decimal Cantidad { get; set; }
    [Range(0, 1000000)] public decimal? CostoTotal { get; set; }
    [StringLength(30)] public string? MetodoPago { get; set; }
    public int? TipoGastoId { get; set; }
    [StringLength(300)] public string? Descripcion { get; set; }
    /// <summary>Fecha de la compra (solo COMPRA). Si es null se usa la fecha/hora actual.</summary>
    public DateTime? Fecha { get; set; }
}

public class MovimientoInsumoDto
{
    public int Id { get; set; }
    public int InsumoId { get; set; }
    public string? InsumoNombre { get; set; }
    public string Tipo { get; set; } = "";
    public decimal Cantidad { get; set; }
    public decimal? CostoTotal { get; set; }
    public DateTime Fecha { get; set; }
    public string? UsuarioNombre { get; set; }
    public string? Descripcion { get; set; }
}

// ---------- Motorizados (logistica de delivery) ----------
public class MotorizadoDto
{
    public int Id { get; set; }
    [Required, StringLength(120, MinimumLength = 2)] public string Nombre { get; set; } = "";
    [StringLength(20)] public string? Celular { get; set; }
    public bool Activo { get; set; } = true;
}

public record AsignarMotorizadoRequest(int? MotorizadoId);

// ---------- Roles de personal (cargos) ----------
public class RolPersonalDto
{
    public int Id { get; set; }
    [Required, StringLength(60, MinimumLength = 2)] public string Nombre { get; set; } = "";
    public bool Activo { get; set; } = true;
}

// ---------- Personal ----------
public class EmpleadoDto
{
    public int Id { get; set; }
    [Required, StringLength(120, MinimumLength = 2)] public string Nombre { get; set; } = "";
    [StringLength(15)] public string? Dni { get; set; }
    [StringLength(20)] public string? Celular { get; set; }
    [StringLength(60)] public string? Cargo { get; set; }
    public DateOnly? FechaIngreso { get; set; }
    public bool Activo { get; set; } = true;
}

// ---------- Plantillas de WhatsApp ----------
public class PlantillaWhatsappDto
{
    public int Id { get; set; }
    public string Evento { get; set; } = "";
    [Required, StringLength(1000, MinimumLength = 3)] public string Mensaje { get; set; } = "";
    public bool Activa { get; set; } = true;
}

public record PlantillaWhatsappActivaDto(string Evento, string Mensaje);

// ---------- Reportes ----------
public class ReporteResultDto
{
    public List<string> Columnas { get; set; } = new();
    public List<Dictionary<string, string>> Filas { get; set; } = new();
    /// <summary>Acción operativa disponible por fila (ej: "donar", "reenviar-almacen").
    /// Si no es null, cada fila incluye la clave "_id" con el Id del pedido.</summary>
    public string? Accion { get; set; }
}

// ---------- Tablero SLA / cuellos de botella ----------
public record SlaAreaDto(int AreaId, string AreaNombre, int Orden, int TiempoEstMinutos, double MinutosPromedioReal, int PedidosProcesados);
public record PedidoEstancadoDto(int PedidoId, int Numero, string ClienteNombre, int AreaId, string AreaNombre, int MinutosEnArea, int TiempoEstMinutos);

public class TableroSlaDto
{
    public List<SlaAreaDto> Areas { get; set; } = new();
    public List<PedidoEstancadoDto> Estancados { get; set; } = new();
}

// ---------- Vista gerencial unificada ----------
public class VistaGerencialDto
{
    public decimal VentasHoy { get; set; }
    public decimal CobradoHoy { get; set; }
    public decimal VentasMes { get; set; }
    public int PedidosEntregadosHoy { get; set; }
    public int PedidosEntregadosTiendaHoy { get; set; }
    public int PedidosEntregadosDomicilioHoy { get; set; }
    public int PedidosEntregadosSemana { get; set; }
    public int PedidosEntregadosMes { get; set; }
    public decimal SaldoPorCobrar { get; set; }
    public decimal GastosMes { get; set; }
    public decimal UtilidadMes { get; set; }
    public int PedidosActivos { get; set; }
    public int PedidosListosSinRecoger { get; set; }
    public int ComprobantesPendientes { get; set; }
    public int ComprobantesRechazados { get; set; }
    public int InsumosBajoStock { get; set; }
    public decimal CajaEsperadaHoy { get; set; }
}

public record ConsolidadoSedeDto(int SedeId, string SedeNombre, decimal VentasHoy, decimal VentasMes,
    decimal SaldoPorCobrar, int PedidosActivos, int PedidosListos);

// ---------- Áreas de lavado ----------
public class AreaLavadoEditableDto
{
    public int Id { get; set; }
    [Required, StringLength(60, MinimumLength = 2)] public string Nombre { get; set; } = "";
    [Range(1, 100)] public int Orden { get; set; }
    [Range(1, 1000)] public int TiempoEstMinutos { get; set; } = 30;
    public bool Activa { get; set; } = true;
}

// ---------- Promociones ----------
public class PromocionDto
{
    public int Id { get; set; }
    [Required, StringLength(60, MinimumLength = 2)] public string Tipo { get; set; } = "VOLUMEN";
    [Required, StringLength(200, MinimumLength = 3)] public string Descripcion { get; set; } = "";
    [Range(0, 100)] public decimal? DescuentoPct { get; set; }
    [Range(0, 100000)] public decimal? DescuentoMonto { get; set; }
    public int? ServicioId { get; set; }
    public string? ServicioNombre { get; set; }
    [Range(0.01, 100000)] public decimal CantidadMinima { get; set; } = 1;
    public DateOnly? FechaInicio { get; set; }
    public DateOnly? FechaFin { get; set; }
    public bool Activa { get; set; } = true;
    [StringLength(30)] public string? Codigo { get; set; }
}

public record CambiarEstadoPromocionRequest(bool Activa);

public class SiguienteAreaRequest
{
    [StringLength(120)] public string? RecibidoPor { get; set; }
}

public class PromocionValidaDto
{
    public int Id { get; set; }
    public string Descripcion { get; set; } = "";
    public decimal? DescuentoPct { get; set; }
    public decimal? DescuentoMonto { get; set; }
    public int? ServicioId { get; set; }
    public decimal CantidadMinima { get; set; }
}

// ---------- Catalogos ----------
public record ServicioDto(int Id, string Nombre, decimal Precio, string Unidad, int? CategoriaId);
public record AreaLavadoDto(int Id, string Nombre, int Orden, int TiempoEstMinutos);

// ---------- Caja ----------
public record TipoGastoDto(int Id, string Nombre);

public class RegistrarGastoRequest
{
    [Range(0.01, 100000)] public decimal Monto { get; set; }
    [Required] public string MetodoPago { get; set; } = "EFECTIVO";
    public int? TipoGastoId { get; set; }
    [StringLength(300)] public string? Descripcion { get; set; }
}

public class MovimientoCajaDto
{
    public int Id { get; set; }
    public DateTime Fecha { get; set; }
    public string Tipo { get; set; } = "";
    public string MetodoPago { get; set; } = "";
    public decimal Monto { get; set; }
    public string? Descripcion { get; set; }
    public int? PedidoId { get; set; }
    public int? PedidoNumero { get; set; }
    public string? ClienteNombre { get; set; }
    public int? TipoGastoId { get; set; }
    public string? TipoGastoNombre { get; set; }
}

public class GuardarCuadreRequest
{
    [Required] public DateTime Fecha { get; set; }
    /// <summary>Usuario cuyo turno se esta cuadrando. Un usuario no ADMIN solo puede cuadrarse a si mismo.</summary>
    public int? UsuarioId { get; set; }
    [Range(0, 1000000)] public decimal CajaInicial { get; set; }
    public decimal PedidosPagadosEfect { get; set; }
    public decimal Gastos { get; set; }
    [Range(0, 1000000)] public decimal TotalContado { get; set; }
    public decimal Diferencia { get; set; }
    public decimal CajaFinal { get; set; }
    /// <summary>Efectivo entregado/retirado al cierre. CajaFinal = TotalContado - Corte.</summary>
    [Range(0, 1000000)] public decimal Corte { get; set; }
    /// <summary>Pagos por transferencia móvil (Yape + Plin + Transferencia).</summary>
    public decimal IngresosDigital { get; set; }
    /// <summary>Pagos por POS / tarjeta.</summary>
    public decimal IngresosTarjeta { get; set; }
    [StringLength(300)] public string? Nota { get; set; }
    [StringLength(400)] public string? Observaciones { get; set; }
}

public class CuadreCajaDto
{
    public int Id { get; set; }
    public DateTime Fecha { get; set; }
    public int UsuarioId { get; set; }
    public string? UsuarioNombre { get; set; }
    public decimal CajaInicial { get; set; }
    public decimal PedidosPagadosEfect { get; set; }
    public decimal Gastos { get; set; }
    public decimal TotalContado { get; set; }
    public decimal Diferencia { get; set; }
    public decimal CajaFinal { get; set; }
    public decimal Corte { get; set; }
    public decimal IngresosDigital { get; set; }
    public decimal IngresosTarjeta { get; set; }
    public string? Nota { get; set; }
    public string? Observaciones { get; set; }
    public DateTime FechaCreacion { get; set; }
}

public record UsuarioDelDiaDto(int Id, string NombreCompleto, string RolNombre, int Movimientos, bool TieneCuadre);

// ---------- Reporte de Cuadres Diarios (pantalla dedicada) ----------
public record CuadreDiarioFilaDto(
    int Id,
    string UsuarioNombre,
    decimal CajaInicial,
    decimal IngresosEfectivo,
    decimal Egresos,
    decimal MontoEnCaja,
    decimal Corte,
    decimal CajaFinal,
    string Estado,          // CUADRA | SOBRA | FALTA
    decimal MargenError,
    string? Nota,
    decimal IngresosDigital,
    decimal IngresosTarjeta);

public record CuadreDiarioDiaDto(
    DateOnly Fecha,
    List<CuadreDiarioFilaDto> Cuadres,
    bool SinInformacion,
    decimal NoCuadradoIngresos,   // movimientos de un día sin cuadre guardado
    decimal NoCuadradoEgresos);

public record CuadresDiariosReporteDto(int Anio, int Mes, List<CuadreDiarioDiaDto> Dias);

// ---------- Facturación Electrónica ----------
public class ConfiguracionFacturacionDto
{
    [StringLength(150)] public string? RazonSocial { get; set; }
    [StringLength(11, MinimumLength = 11)] public string? RucEmisor { get; set; }
    public string Ambiente { get; set; } = "BETA"; // BETA | PRODUCCION
    [StringLength(50)] public string? SolUsuario { get; set; }
    /// <summary>Solo se envía al guardar una clave nueva; nunca se devuelve la clave real.</summary>
    public string? SolClaveNueva { get; set; }
    /// <summary>Certificado .pfx en base64; solo se envía al subir uno nuevo.</summary>
    public string? CertificadoPfxBase64 { get; set; }
    public string? CertificadoPasswordNueva { get; set; }
    [StringLength(4, MinimumLength = 4)] public string SerieBoleta { get; set; } = "B001";
    [StringLength(4, MinimumLength = 4)] public string SerieFactura { get; set; } = "F001";
    public bool Activo { get; set; }
    public bool TieneCertificado { get; set; }
    public bool TieneCredencialesSol { get; set; }
}

public record EmitirComprobanteRequest(string Tipo); // BOLETA | FACTURA

public class ComprobanteDto
{
    public int Id { get; set; }
    public int PedidoId { get; set; }
    public int? PedidoNumero { get; set; }
    public string Tipo { get; set; } = "";
    public string Serie { get; set; } = "";
    public int Correlativo { get; set; }
    public string NumeroCompleto => $"{Serie}-{Correlativo}";
    public string ClienteNombre { get; set; } = "";
    public string ClienteTipoDoc { get; set; } = "";
    public string? ClienteNumDoc { get; set; }
    public decimal OpGravada { get; set; }
    public decimal Igv { get; set; }
    public decimal Total { get; set; }
    public string Estado { get; set; } = "";
    public string? DescripcionRespuestaSunat { get; set; }
    public DateTime FechaEmision { get; set; }
}

// ---------- Panel de propietario de plataforma (alta de negocios/tenants) ----------

public record NegocioResumenDto(
    int Id, string Nombre, string Slug, bool Activo, DateTime FechaCreacion,
    int CantidadSedes, int CantidadUsuarios,
    string PlanSuscripcion, string EstadoSuscripcion, decimal MontoMensual,
    DateOnly? ProximoPago, DateTime? UltimoAcceso, int PedidosMes);

/// <summary>KPIs del negocio-de-negocios para el tablero del propietario.</summary>
public record PlataformaResumenDto(
    int TotalEmpresas, int EmpresasActivas, int EmpresasSuspendidas, int EmpresasNuevasMes,
    decimal IngresoMensualRecurrente, int PedidosMesTotal, int EmpresasPorVencer, int EmpresasVencidas);

public record SedeResumenDto(int Id, string Nombre, string? Direccion, bool Activo);

public record UsuarioResumenDto(int Id, string Usuario, string NombreCompleto, string RolCodigo, bool Activo, DateTime? UltimoAcceso);

/// <summary>Ficha completa de una empresa para el panel del propietario.</summary>
public class NegocioDetalleDto
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? RucEmpresa { get; set; }
    public string? TitularNombre { get; set; }
    public string? TitularEmail { get; set; }
    public string? TitularCelular { get; set; }
    public string? NotasInternas { get; set; }
    public bool Activo { get; set; }
    public DateTime FechaCreacion { get; set; }
    public string PlanSuscripcion { get; set; } = "";
    public string EstadoSuscripcion { get; set; } = "";
    public decimal MontoMensual { get; set; }
    public DateOnly? ProximoPago { get; set; }
    public int PedidosMes { get; set; }
    public DateTime? UltimoAcceso { get; set; }
    public string? AdminUsuario { get; set; }
    public List<SedeResumenDto> Sedes { get; set; } = new();
    public List<UsuarioResumenDto> Usuarios { get; set; } = new();
}

public class CrearNegocioRequest
{
    [Required, StringLength(120, MinimumLength = 2)] public string Nombre { get; set; } = "";
    [Required, StringLength(50, MinimumLength = 2)] public string Slug { get; set; } = "";
    public string? RucEmpresa { get; set; }
    public string? TitularNombre { get; set; }
    public string? TitularEmail { get; set; }
    public string? TitularCelular { get; set; }
    [Required, StringLength(80, MinimumLength = 2)] public string SedeNombre { get; set; } = "";
    [Required, StringLength(50, MinimumLength = 3)] public string AdminUsuario { get; set; } = "";
    [Required, StringLength(120, MinimumLength = 2)] public string AdminNombreCompleto { get; set; } = "";
    public string? AdminEmail { get; set; }
    [Required, StringLength(100, MinimumLength = 8)] public string AdminPassword { get; set; } = "";
}

public class EditarNegocioRequest
{
    [Required, StringLength(120, MinimumLength = 2)] public string Nombre { get; set; } = "";
    public string? RucEmpresa { get; set; }
    public string? TitularNombre { get; set; }
    public string? TitularEmail { get; set; }
    [StringLength(20)] public string? TitularCelular { get; set; }
    [StringLength(500)] public string? NotasInternas { get; set; }
}

public class CambiarSuscripcionRequest
{
    [Required] public string PlanSuscripcion { get; set; } = "BASICO";
    [Required] public string EstadoSuscripcion { get; set; } = "ACTIVA";
    [Range(0, 100000)] public decimal MontoMensual { get; set; }
    public DateOnly? ProximoPago { get; set; }
}

public class ResetPasswordAdminRequest
{
    [Required, StringLength(100, MinimumLength = 8)] public string NuevaPassword { get; set; } = "";
}

public record CambiarEstadoNegocioRequest(bool Activo);

/// <summary>Aviso de suscripción que ve la propia empresa en su dashboard.</summary>
public record MiSuscripcionDto(bool Mostrar, string Tipo, string Mensaje,
    DateOnly? ProximoPago, int? DiasParaVencer, string EstadoSuscripcion);

// ---------- Pagos online (Culqi) ----------
public class ConfiguracionPagosDto
{
    public string Proveedor { get; set; } = "CULQI";
    [StringLength(200)] public string? PublicKey { get; set; }
    /// <summary>Solo se envía al guardar una clave nueva; nunca se devuelve la clave real.</summary>
    public string? SecretKeyNueva { get; set; }
    public bool Activo { get; set; }
    public bool TieneSecretKey { get; set; }
}

public class ConvertirDeliveryRequest
{
    [Required, StringLength(250)] public string DireccionEntrega { get; set; } = "";
    [Required, StringLength(100)] public string DistritoEntrega { get; set; } = "";
    [StringLength(250)] public string? ReferenciaEntrega { get; set; }
    [Range(-90d, 90d)] public decimal? LatitudEntrega { get; set; }
    [Range(-180d, 180d)] public decimal? LongitudEntrega { get; set; }
}

public record LinkSeguimientoDto(Guid Token);

public record PasoSeguimientoDto(string Codigo, string Nombre, bool Alcanzado, bool Actual);

public class SeguimientoPedidoDto
{
    public string NombreNegocio { get; set; } = "";
    public string? LogoUrl { get; set; }
    public string ColorPrimario { get; set; } = "#0b57d0";
    public string? TelefonoNegocio { get; set; }
    public string? DireccionNegocio { get; set; }
    public int NumeroPedido { get; set; }
    public string Modalidad { get; set; } = "";
    public string? DireccionEntrega { get; set; }
    public string? DistritoEntrega { get; set; }
    public string? ReferenciaEntrega { get; set; }
    public decimal? LatitudEntrega { get; set; }
    public decimal? LongitudEntrega { get; set; }
    public string ResumenEstado { get; set; } = "";
    public DateTime? FechaCompromiso { get; set; }
    public string EtiquetaFechaCompromiso { get; set; } = "";
    public List<PasoSeguimientoDto> Pasos { get; set; } = new();
    public List<SeguimientoPedidoItemDto> Items { get; set; } = new();
    public bool Anulado { get; set; }
    public decimal Total { get; set; }
    public decimal MontoPagado { get; set; }
    public decimal Saldo { get; set; }
    public bool RequierePago { get; set; }
    public string? PublicKeyCulqi { get; set; }
    public string? MotorizadoNombre { get; set; }
    public string? MotorizadoCelular { get; set; }
    public bool PuedeReprogramar { get; set; }
}

public record SeguimientoPedidoItemDto(string Nombre, decimal Cantidad);

public record ReprogramarPedidoPublicoRequest([Required] DateTime NuevaFecha);

public class CobrarSolicitudPagoRequest
{
    [Required, StringLength(100, MinimumLength = 10)] public string CulqiTokenId { get; set; } = "";
    [Required, EmailAddress, StringLength(120)] public string Email { get; set; } = "";
}

public class CobrarSolicitudPagoResultDto
{
    public bool Exito { get; set; }
    public string? Mensaje { get; set; }
    public decimal SaldoPendiente { get; set; }
}
