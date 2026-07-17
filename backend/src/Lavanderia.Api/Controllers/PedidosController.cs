using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;
using Lavanderia.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

[Route("api/[controller]")]
public class PedidosController : TenantAwareControllerBase
{
    private readonly IPedidoService _service;
    private readonly IPromocionRepository _promociones;
    public PedidosController(IPedidoService service, IPromocionRepository promociones)
    {
        _service = service;
        _promociones = promociones;
    }

    [HttpGet]
    [Authorize(Policy = "Modulo:PEDIDOS")]
    public async Task<ActionResult<PagedResultDto<PedidoDto>>> Listar(
        [FromQuery] string? filtro,
        [FromQuery] string? busqueda,
        [FromQuery] DateTime? desde,
        [FromQuery] DateTime? hasta,
        [FromQuery] string? campoFecha,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamanoPagina = 15,
        CancellationToken ct = default)
        => Ok(await _service.ListarPaginadoAsync(
            filtro, busqueda, desde, hasta, campoFecha,
            Math.Max(1, pagina), Math.Clamp(tamanoPagina, 1, 200), SedeId!.Value, ct));

    [HttpGet("por-cliente/{clienteId:int}")]
    [Authorize(Policy = "Modulo:CLIENTES")]
    public async Task<ActionResult<PagedResultDto<PedidoDto>>> ListarPorCliente(
        int clienteId,
        [FromQuery] string? filtro,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamanoPagina = 10,
        CancellationToken ct = default)
        => Ok(await _service.ListarPorClienteAsync(clienteId, filtro, Math.Max(1, pagina), Math.Clamp(tamanoPagina, 1, 200), SedeId!.Value, ct));

    [HttpGet("{id:int}")]
    [Authorize(Policy = "Modulo:PEDIDOS")]
    public async Task<ActionResult<PedidoDto>> Obtener(int id, CancellationToken ct)
    {
        var p = await _service.ObtenerAsync(id, SedeId!.Value, ct);
        if (p is null) return NotFound();
        return Ok(p);
    }

    [HttpPost]
    [Authorize(Policy = "Modulo:REGISTRAR")]
    public async Task<ActionResult<PedidoDto>> Crear([FromBody] CrearPedidoRequest req, CancellationToken ct)
    {
        try
        {
            var pedido = await _service.CrearAsync(req, UsuarioId, NegocioId, SedeId!.Value, ct);
            return CreatedAtAction(nameof(Obtener), new { id = pedido.Id }, pedido);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    [HttpPost("{id:int}/avanzar")]
    [Authorize(Policy = "Modulo:PEDIDOS")]
    public async Task<IActionResult> Avanzar(int id, [FromBody] AvanzarAreaRequest req, CancellationToken ct)
    {
        try
        {
            await _service.AvanzarAreaAsync(id, req, UsuarioId, SedeId!.Value, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    /// <summary>
    /// Un click: mueve el pedido a la siguiente area del flujo (o LISTO si termino).
    /// Es la operacion mas usada por el trabajador.
    /// </summary>
    [HttpPost("{id:int}/siguiente-area")]
    [Authorize(Policy = "Modulo:PEDIDOS")]
    public async Task<IActionResult> SiguienteArea(int id, [FromBody] SiguienteAreaRequest? req, CancellationToken ct)
    {
        try
        {
            await _service.AvanzarSiguienteAreaAsync(id, UsuarioId, SedeId!.Value, req?.RecibidoPor, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    [HttpGet("{id:int}/historial")]
    [Authorize(Policy = "Modulo:PEDIDOS")]
    public async Task<ActionResult<List<PedidoHistorialDto>>> Historial(int id, CancellationToken ct)
        => Ok(await _service.ObtenerHistorialAsync(id, SedeId!.Value, ct));

    [HttpGet("dashboard")]
    [Authorize(Policy = "Modulo:INICIO")]
    public async Task<ActionResult<DashboardDto>> Dashboard(CancellationToken ct)
        => Ok(await _service.DashboardAsync(SedeId!.Value, ct));

    [HttpGet("siguiente-numero")]
    [Authorize(Policy = "Modulo:REGISTRAR")]
    public async Task<ActionResult<int>> SiguienteNumero(CancellationToken ct)
        => Ok(await _service.SiguienteNumeroAsync(SedeId!.Value, ct));

    /// <summary>
    /// Valida un código de promoción para usarlo en Registrar. Cualquier usuario autenticado puede
    /// consultarlo (no solo ADMIN), a diferencia del CRUD de promociones.
    /// </summary>
    [HttpGet("promocion/validar")]
    [Authorize(Policy = "Modulo:REGISTRAR")]
    public async Task<ActionResult<PromocionValidaDto>> ValidarCodigoPromocion([FromQuery] string codigo, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(codigo)) return BadRequest(new { mensaje = "Indica un código." });

        var promo = await _promociones.BuscarPorCodigoAsync(codigo, NegocioId, ct);
        if (promo is null) return NotFound(new { mensaje = "Código no encontrado." });
        if (!promo.Activa) return BadRequest(new { mensaje = "Esta promoción ya no está activa." });

        var hoy = DateOnly.FromDateTime(DateTime.Today);
        if (promo.FechaInicio.HasValue && hoy < promo.FechaInicio.Value)
            return BadRequest(new { mensaje = "Esta promoción todavía no empieza." });
        if (promo.FechaFin.HasValue && hoy > promo.FechaFin.Value)
            return BadRequest(new { mensaje = "Esta promoción ya venció." });

        return Ok(new PromocionValidaDto
        {
            Id = promo.Id,
            Descripcion = promo.Descripcion,
            DescuentoPct = promo.DescuentoPct,
            DescuentoMonto = promo.DescuentoMonto,
            ServicioId = promo.ServicioId,
            CantidadMinima = promo.CantidadMinima
        });
    }

    [HttpGet("abandonados")]
    [Authorize(Policy = "Modulo:PEDIDOS")]
    public async Task<ActionResult<List<PedidoAbandonadoDto>>> Abandonados([FromQuery] int dias = 3, CancellationToken ct = default)
        => Ok(await _service.ListarAbandonadosAsync(Math.Max(1, dias), SedeId!.Value, ct));

    [HttpPost("{id:int}/pagos")]
    [Authorize(Policy = "Modulo:PEDIDOS")]
    public async Task<IActionResult> RegistrarPago(int id, [FromBody] RegistrarPagoRequest req, CancellationToken ct)
    {
        try
        {
            await _service.RegistrarPagoAsync(id, req, UsuarioId, SedeId!.Value, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    [HttpPost("{id:int}/items")]
    [Authorize(Policy = "Modulo:PEDIDOS")]
    public async Task<IActionResult> AgregarItem(int id, [FromBody] AgregarItemRequest req, CancellationToken ct)
    {
        try
        {
            await _service.AgregarItemAsync(id, req, NegocioId, SedeId!.Value, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    [HttpPut("{id:int}/fecha-entrega")]
    [Authorize(Policy = "Modulo:PEDIDOS")]
    public async Task<IActionResult> CambiarFechaEntrega(int id, [FromBody] CambiarFechaEntregaRequest req, CancellationToken ct)
    {
        try
        {
            await _service.CambiarFechaEntregaAsync(id, req, UsuarioId, SedeId!.Value, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    /// <summary>Convierte un pedido de Tienda a Delivery y, si le queda saldo, genera de una
    /// vez su link de seguimiento/pago (ver <see cref="LinkSeguimiento"/>).</summary>
    [HttpPost("{id:int}/convertir-delivery")]
    [Authorize(Policy = "Modulo:PEDIDOS")]
    public async Task<IActionResult> ConvertirDelivery(int id, CancellationToken ct)
    {
        try
        {
            await _service.ConvertirADeliveryAsync(id, NegocioId, SedeId!.Value, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    /// <summary>Asigna (o quita, con motorizadoId null) el repartidor a cargo de este pedido.</summary>
    [HttpPut("{id:int}/motorizado")]
    [Authorize(Policy = "Modulo:PEDIDOS")]
    public async Task<IActionResult> AsignarMotorizado(int id, [FromBody] AsignarMotorizadoRequest req, CancellationToken ct)
    {
        try
        {
            await _service.AsignarMotorizadoAsync(id, req.MotorizadoId, SedeId!.Value, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    /// <summary>Devuelve el token del link público de seguimiento/pago de este pedido,
    /// generándolo si todavía no existe uno vigente. Solo aplica a pedidos Delivery.</summary>
    [HttpGet("{id:int}/link-seguimiento")]
    [Authorize(Policy = "Modulo:PEDIDOS")]
    public async Task<ActionResult<LinkSeguimientoDto>> LinkSeguimiento(int id, CancellationToken ct)
    {
        try
        {
            var token = await _service.ObtenerOCrearLinkPagoAsync(id, NegocioId, SedeId!.Value, ct);
            if (token is null) return NotFound(new { mensaje = "Este pedido ya está pagado, no necesita link de pago." });
            return Ok(new LinkSeguimientoDto(token.Value));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    [HttpPost("{id:int}/anular")]
    [Authorize(Policy = "Modulo:PEDIDOS")]
    public async Task<IActionResult> Anular(int id, [FromBody] AnularPedidoRequest req, CancellationToken ct)
    {
        try
        {
            await _service.AnularAsync(id, req.Motivo, UsuarioId, NegocioId, SedeId!.Value, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    /// <summary>Envía a donación un pedido con mucho tiempo en custodia (desde el reporte de Almacén).</summary>
    [HttpPost("{id:int}/donar")]
    [Authorize(Policy = "Modulo:REPORTES")]
    public async Task<IActionResult> Donar(int id, CancellationToken ct)
    {
        try
        {
            await _service.DonarAsync(id, UsuarioId, SedeId!.Value, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    /// <summary>Reenvía un pedido pendiente directo a almacén (LISTO), desde el reporte de Órdenes Pendientes.</summary>
    [HttpPost("{id:int}/reenviar-almacen")]
    [Authorize(Policy = "Modulo:REPORTES")]
    public async Task<IActionResult> ReenviarAlmacen(int id, CancellationToken ct)
    {
        try
        {
            await _service.ReenviarAlmacenAsync(id, UsuarioId, SedeId!.Value, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }
}
