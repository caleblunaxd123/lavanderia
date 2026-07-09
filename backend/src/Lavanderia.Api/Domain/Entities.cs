namespace Lavanderia.Api.Domain;

public class CuadreCaja
{
    public int Id { get; set; }
    public int SedeId { get; set; }
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

public class ConfiguracionNegocio
{
    public int Id { get; set; }
    public int NegocioId { get; set; }
    public string NombreNegocio { get; set; } = "";
    public string? LogoUrl { get; set; }
    public string ColorPrimario { get; set; } = "#0b57d0";
    public string ColorSecundario { get; set; } = "#29b6f6";
    public string ColorAcento { get; set; } = "#f5a623";
    public string? Direccion { get; set; }
    public string? Telefono { get; set; }
    public string? Ruc { get; set; }
    public string? HorarioAtencion { get; set; }
    public decimal Igv { get; set; } = 18.00m;
    public decimal MetaMensual { get; set; }
    public decimal SolesPorPunto { get; set; } = 1m;
    public int AnchoTicketMm { get; set; } = 80;
    public string? MensajePieTicket { get; set; }
    public string? CondicionesServicio { get; set; }
    public string? NotasProduccion { get; set; }
    public decimal CostoDelivery { get; set; }
}

public class Rol
{
    public int Id { get; set; }
    public string Codigo { get; set; } = "";
    public string Nombre { get; set; } = "";
}

public class RolPermiso
{
    public int Id { get; set; }
    public int NegocioId { get; set; }
    public int RolId { get; set; }
    public string RolCodigo { get; set; } = "";
    public string RolNombre { get; set; } = "";
    public string Modulo { get; set; } = "";
    public bool PuedeAcceder { get; set; }
}

public class Usuario
{
    public int Id { get; set; }
    public string UsuarioLogin { get; set; } = "";
    public string NombreCompleto { get; set; } = "";
    public string? Email { get; set; }
    public string PasswordHash { get; set; } = "";
    public int RolId { get; set; }
    public string RolCodigo { get; set; } = "";
    public bool Activo { get; set; } = true;
    public int NegocioId { get; set; }
    public int? SedeId { get; set; }
    public string? SedeNombre { get; set; }
}

public class Negocio
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? RucEmpresa { get; set; }
    public string? TitularNombre { get; set; }
    public string? TitularEmail { get; set; }
    public bool Activo { get; set; } = true;
    public DateTime FechaCreacion { get; set; }
}

public class Sede
{
    public int Id { get; set; }
    public int NegocioId { get; set; }
    public string Nombre { get; set; } = "";
    public string? Direccion { get; set; }
    public string? Telefono { get; set; }
    public bool Activo { get; set; } = true;
    public DateTime FechaCreacion { get; set; }
}

public class Cliente
{
    public int Id { get; set; }
    public int NegocioId { get; set; }
    public string Nombre { get; set; } = "";
    public string? Celular { get; set; }
    public string? Dni { get; set; }
    public string? DocumentoFiscal { get; set; }
    public string? Direccion { get; set; }
    public int Puntos { get; set; }
    public bool Activo { get; set; } = true;
    public DateTime FechaCreacion { get; set; }
}

public class MovimientoPuntos
{
    public int Id { get; set; }
    public int NegocioId { get; set; }
    public int ClienteId { get; set; }
    public DateTime Fecha { get; set; }
    public string Motivo { get; set; } = "";
    public int Puntos { get; set; }
    public string Tipo { get; set; } = "SUMA"; // SUMA | RESTA
    public int? UsuarioId { get; set; }
    public string? UsuarioNombre { get; set; }
}

public class Categoria
{
    public int Id { get; set; }
    public int NegocioId { get; set; }
    public string Nombre { get; set; } = "";
    public bool Activa { get; set; } = true;
}

public class Servicio
{
    public int Id { get; set; }
    public int NegocioId { get; set; }
    public string Nombre { get; set; } = "";
    public decimal Precio { get; set; }
    public string Unidad { get; set; } = "";
    public int? CategoriaId { get; set; }
    public string? CategoriaNombre { get; set; }
    public bool Activo { get; set; } = true;
    public bool EsCargoDelivery { get; set; }
}

public class AreaLavado
{
    public int Id { get; set; }
    public int SedeId { get; set; }
    public string Nombre { get; set; } = "";
    public int Orden { get; set; }
    public int TiempoEstMinutos { get; set; }
    public bool Activa { get; set; } = true;
}

public class Promocion
{
    public int Id { get; set; }
    public int NegocioId { get; set; }
    public string Tipo { get; set; } = "";  // VOLUMEN | FRECUENCIA | FIJA
    public string Descripcion { get; set; } = "";
    public decimal? DescuentoPct { get; set; }
    public decimal? DescuentoMonto { get; set; }
    public int? ServicioId { get; set; }
    public string? ServicioNombre { get; set; }
    public decimal CantidadMinima { get; set; } = 1;
    public DateOnly? FechaInicio { get; set; }
    public DateOnly? FechaFin { get; set; }
    public bool Activa { get; set; } = true;
    public string? Codigo { get; set; }
}

public class Pedido
{
    public int Id { get; set; }
    public int SedeId { get; set; }
    public int Numero { get; set; }
    public int ClienteId { get; set; }
    public string? ClienteNombre { get; set; }
    public string? ClienteCelular { get; set; }
    public string? ClienteDni { get; set; }
    public int UsuarioId { get; set; }
    public string? UsuarioNombre { get; set; }
    public DateTime FechaIngreso { get; set; }
    public DateTime? FechaEntregaEst { get; set; }
    public string Modalidad { get; set; } = "Tienda";
    public decimal Subtotal { get; set; }
    public decimal Descuento { get; set; }
    public bool EsUrgente { get; set; }
    public decimal RecargoUrgente { get; set; }
    public decimal Redondeo { get; set; }
    public decimal Total { get; set; }
    public decimal MontoPagado { get; set; }
    public string MetodoPagoInicial { get; set; } = "EFECTIVO";
    public string EstadoPago { get; set; } = "PENDIENTE";
    public string EstadoProceso { get; set; } = "PENDIENTE";
    public int? AreaActualId { get; set; }
    public string? AreaActualNombre { get; set; }
    public string? Observaciones { get; set; }
    public DateTime? FechaEntregaReal { get; set; }
    public bool Anulado { get; set; }
    public string? MotivoAnulacion { get; set; }
    public string? CodigoAntiguo { get; set; }
    public List<PedidoItem> Items { get; set; } = new();
}

public class PedidoItem
{
    public int Id { get; set; }
    public int PedidoId { get; set; }
    public int ServicioId { get; set; }
    public string? ServicioNombre { get; set; }
    public decimal Cantidad { get; set; }
    public decimal PrecioUnit { get; set; }
    public decimal Total { get; set; }
    public string? Descripcion { get; set; }
}

public class PedidoHistorial
{
    public int Id { get; set; }
    public int PedidoId { get; set; }
    public int? AreaId { get; set; }
    public string? AreaNombre { get; set; }
    public string EstadoProceso { get; set; } = "";
    public int? UsuarioId { get; set; }
    public DateTime Fecha { get; set; }
    public string? Nota { get; set; }
    public bool NotificadoWsp { get; set; }
}

public class MovimientoCaja
{
    public int Id { get; set; }
    public int SedeId { get; set; }
    public DateTime Fecha { get; set; }
    public string Tipo { get; set; } = "";       // INGRESO | GASTO
    public string MetodoPago { get; set; } = ""; // EFECTIVO | YAPE | PLIN | TRANSFERENCIA | POS
    public decimal Monto { get; set; }
    public string? Descripcion { get; set; }
    public int? PedidoId { get; set; }
    public int UsuarioId { get; set; }
    public int? TipoGastoId { get; set; }
    public string? TipoGastoNombre { get; set; }
}

public class TipoGasto
{
    public int Id { get; set; }
    public int NegocioId { get; set; }
    public string Nombre { get; set; } = "";
    public bool Activo { get; set; } = true;
}

public class Insumo
{
    public int Id { get; set; }
    public int SedeId { get; set; }
    public string Nombre { get; set; } = "";
    public string UnidadMedida { get; set; } = "";
    public decimal StockActual { get; set; }
    public decimal StockMinimo { get; set; }
    public bool Activo { get; set; } = true;
}

public class MovimientoInsumo
{
    public int Id { get; set; }
    public int SedeId { get; set; }
    public int InsumoId { get; set; }
    public string? InsumoNombre { get; set; }
    public string Tipo { get; set; } = "";  // COMPRA | CONSUMO | AJUSTE
    public decimal Cantidad { get; set; }
    public decimal? CostoTotal { get; set; }
    public DateTime Fecha { get; set; }
    public int UsuarioId { get; set; }
    public string? UsuarioNombre { get; set; }
    public string? Descripcion { get; set; }
    public int? MovimientoCajaId { get; set; }
}

public class RolPersonal
{
    public int Id { get; set; }
    public int NegocioId { get; set; }
    public string Nombre { get; set; } = "";
    public bool Activo { get; set; } = true;
}

public class Empleado
{
    public int Id { get; set; }
    public int SedeId { get; set; }
    public string Nombre { get; set; } = "";
    public string? Dni { get; set; }
    public string? Celular { get; set; }
    public string? Cargo { get; set; }
    public DateOnly? FechaIngreso { get; set; }
    public bool Activo { get; set; } = true;
}

public class PlantillaWhatsapp
{
    public int Id { get; set; }
    public int NegocioId { get; set; }
    public string Evento { get; set; } = "";
    public string Mensaje { get; set; } = "";
    public bool Activa { get; set; } = true;
}

public class PedidoAbandonado
{
    public int PedidoId { get; set; }
    public int Numero { get; set; }
    public string ClienteNombre { get; set; } = "";
    public string? ClienteCelular { get; set; }
    public decimal Total { get; set; }
    public decimal MontoPagado { get; set; }
    public DateTime FechaListo { get; set; }
    public int DiasEsperando { get; set; }
}

public class ConfiguracionFacturacion
{
    public int Id { get; set; }
    public int NegocioId { get; set; }
    public string? RazonSocial { get; set; }
    public string? RucEmisor { get; set; }
    public string Ambiente { get; set; } = "BETA"; // BETA | PRODUCCION
    public string? SolUsuario { get; set; }
    public string? SolClaveCifrada { get; set; }
    public byte[]? CertificadoPfx { get; set; }
    public string? CertificadoPasswordCifrada { get; set; }
    public string SerieBoleta { get; set; } = "B001";
    public string SerieFactura { get; set; } = "F001";
    public int CorrelativoBoleta { get; set; }
    public int CorrelativoFactura { get; set; }
    public bool Activo { get; set; }
}

public class ComprobanteElectronico
{
    public int Id { get; set; }
    public int NegocioId { get; set; }
    public int SedeId { get; set; }
    public int PedidoId { get; set; }
    public int? PedidoNumero { get; set; }
    public string Tipo { get; set; } = "";          // BOLETA | FACTURA
    public string Serie { get; set; } = "";
    public int Correlativo { get; set; }
    public string ClienteNombre { get; set; } = "";
    public string ClienteTipoDoc { get; set; } = ""; // DNI | RUC | SIN_DOC
    public string? ClienteNumDoc { get; set; }
    public decimal OpGravada { get; set; }
    public decimal Igv { get; set; }
    public decimal Total { get; set; }
    public string Estado { get; set; } = "PENDIENTE"; // PENDIENTE|ACEPTADO|RECHAZADO|ANULADO|ERROR
    public string? CodigoRespuestaSunat { get; set; }
    public string? DescripcionRespuestaSunat { get; set; }
    public byte[]? XmlFirmado { get; set; }
    public byte[]? CdrZip { get; set; }
    public string? HashCpe { get; set; }
    public DateTime FechaEmision { get; set; }
    public DateTime? FechaEnvio { get; set; }
    public int UsuarioId { get; set; }
}
