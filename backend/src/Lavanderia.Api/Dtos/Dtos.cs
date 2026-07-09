using System.ComponentModel.DataAnnotations;

namespace Lavanderia.Api.Dtos;

// ---------- Auth ----------
public record LoginRequest(
    [Required, StringLength(60, MinimumLength = 3)] string Usuario,
    [Required, StringLength(200, MinimumLength = 4)] string Password,
    string? EmpresaSlug = null);

public record LoginResponse(string AccessToken, DateTime Expira, UsuarioDto Usuario);

public record UsuarioDto(
    int Id, string Usuario, string NombreCompleto, string Rol, List<string> ModulosPermitidos,
    int NegocioId, int? SedeId, string? SedeNombre);

public record SeleccionarSedeRequest([Required] int SedeId);

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
    public List<PermisoItemDto> Permisos { get; set; } = new();
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
    public string? Direccion { get; set; }
    public string? Telefono { get; set; }
    public string? Ruc { get; set; }
    public string? HorarioAtencion { get; set; }
    [Range(0, 100)] public decimal Igv { get; set; } = 18m;
    public decimal MetaMensual { get; set; }
    [Range(0.01, 100000)] public decimal SolesPorPunto { get; set; } = 1m;
    [Range(50, 120)] public int AnchoTicketMm { get; set; } = 80;
    [StringLength(300)] public string? MensajePieTicket { get; set; }
    [StringLength(2000)] public string? CondicionesServicio { get; set; }
    [StringLength(500)] public string? NotasProduccion { get; set; }
    [Range(0, 1000)] public decimal CostoDelivery { get; set; }
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
}

public record ClienteFrecuenteDto(int ClienteId, string Nombre, string? Celular, int Visitas);
public record FusionarClientesRequest([Required] int OrigenId, [Required] int DestinoId);

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
    [Range(0.01, 10000)] public decimal Cantidad { get; set; }
    public decimal PrecioUnit { get; set; }
    public decimal Total { get; set; }
    public string? Descripcion { get; set; }
}

public class CrearPedidoRequest
{
    public int? ClienteId { get; set; }
    public ClienteDto? ClienteNuevo { get; set; }
    [Required] public string Modalidad { get; set; } = "Tienda";
    [Required, MinLength(1)] public List<PedidoItemDto> Items { get; set; } = new();
    [Range(0, 100)] public decimal DescuentoPct { get; set; }
    public bool EsUrgente { get; set; }
    [Range(0, 100)] public decimal RecargoUrgentePct { get; set; } = 20m;
    public decimal MontoPagado { get; set; }
    public string MetodoPagoInicial { get; set; } = "EFECTIVO";
    public DateTime? FechaEntregaEst { get; set; }
    public string? Observaciones { get; set; }
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
    public string? UsuarioNombre { get; set; }
    public DateTime FechaIngreso { get; set; }
    public DateTime? FechaEntregaEst { get; set; }
    public string Modalidad { get; set; } = "";
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
    public int TotalPendientes { get; set; }
    public int TotalListos { get; set; }
    public int TotalEnProceso { get; set; }
    public int PedidosDelMes { get; set; }
    public int TotalPendientesTab { get; set; }
    public int TotalOtrosTab { get; set; }
    public int TotalUltimosTab { get; set; }
}

public record AreaConteoDto(int AreaId, string AreaNombre, int Cantidad);

public record PedidoAbandonadoDto(
    int PedidoId, int Numero, string ClienteNombre, string? ClienteCelular,
    decimal Total, decimal MontoPagado, DateTime FechaListo, int DiasEsperando);

public class RegistrarPagoRequest
{
    [Range(0.01, 100000)] public decimal Monto { get; set; }
    [Required] public string Metodo { get; set; } = "EFECTIVO";  // EFECTIVO | YAPE | PLIN | TRANSFERENCIA | POS
    public string? Descripcion { get; set; }
}

public class AgregarItemRequest
{
    [Range(1, int.MaxValue)] public int ServicioId { get; set; }
    [Range(0.01, 10000)] public decimal Cantidad { get; set; }
    public string? Descripcion { get; set; }
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
}

public class RegistrarMovimientoInsumoRequest
{
    [Required] public string Tipo { get; set; } = "";  // COMPRA | CONSUMO | AJUSTE
    [Range(-1000000, 1000000)] public decimal Cantidad { get; set; }
    public decimal? CostoTotal { get; set; }
    public string? MetodoPago { get; set; }
    public int? TipoGastoId { get; set; }
    [StringLength(300)] public string? Descripcion { get; set; }
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
}

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
    public int? TipoGastoId { get; set; }
    public string? TipoGastoNombre { get; set; }
}

public class GuardarCuadreRequest
{
    [Required] public DateTime Fecha { get; set; }
    public decimal CajaInicial { get; set; }
    public decimal PedidosPagadosEfect { get; set; }
    public decimal Gastos { get; set; }
    public decimal TotalContado { get; set; }
    public decimal Diferencia { get; set; }
    public decimal CajaFinal { get; set; }
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
    public string? Observaciones { get; set; }
    public DateTime FechaCreacion { get; set; }
}

public record UsuarioDelDiaDto(int Id, string NombreCompleto, string RolNombre, int Movimientos, bool TieneCuadre);

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
    int CantidadSedes, int CantidadUsuarios);

public class CrearNegocioRequest
{
    [Required, StringLength(120, MinimumLength = 2)] public string Nombre { get; set; } = "";
    [Required, StringLength(50, MinimumLength = 2)] public string Slug { get; set; } = "";
    public string? RucEmpresa { get; set; }
    public string? TitularNombre { get; set; }
    public string? TitularEmail { get; set; }
    [Required, StringLength(80, MinimumLength = 2)] public string SedeNombre { get; set; } = "";
    [Required, StringLength(50, MinimumLength = 3)] public string AdminUsuario { get; set; } = "";
    [Required, StringLength(120, MinimumLength = 2)] public string AdminNombreCompleto { get; set; } = "";
    public string? AdminEmail { get; set; }
    [Required, StringLength(100, MinimumLength = 4)] public string AdminPassword { get; set; } = "";
}

public record CambiarEstadoNegocioRequest(bool Activo);
