using Lavanderia.Api.Domain;
using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

[Route("api/insumos")]
[Microsoft.AspNetCore.Authorization.Authorize(Policy = "Modulo:INVENTARIO")]
public class InsumosController : TenantAwareControllerBase
{
    private readonly IInsumoRepository _repo;
    private readonly ICajaRepository _caja;
    public InsumosController(IInsumoRepository repo, ICajaRepository caja)
    {
        _repo = repo;
        _caja = caja;
    }

    [HttpGet]
    public async Task<ActionResult<List<InsumoDto>>> Listar(CancellationToken ct)
        => Ok((await _repo.ListarTodosAsync(SedeId!.Value, ct)).Select(Map).ToList());

    [HttpGet("bajo-stock")]
    public async Task<ActionResult<List<InsumoDto>>> BajoStock(CancellationToken ct)
        => Ok((await _repo.ListarBajoStockAsync(SedeId!.Value, ct)).Select(Map).ToList());

    [HttpPost]
    public async Task<ActionResult<InsumoDto>> Crear([FromBody] InsumoDto dto, CancellationToken ct)
    {
        var id = await _repo.CrearAsync(new Insumo
        {
            SedeId = SedeId!.Value,
            Nombre = dto.Nombre.Trim(),
            UnidadMedida = dto.UnidadMedida.Trim(),
            StockActual = Math.Max(0, dto.StockActual),
            StockMinimo = dto.StockMinimo,
            Activo = dto.Activo
        }, ct);
        var creado = await _repo.ObtenerPorIdAsync(id, SedeId!.Value, ct);
        return CreatedAtAction(nameof(Listar), Map(creado!));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Actualizar(int id, [FromBody] InsumoDto dto, CancellationToken ct)
    {
        var existente = await _repo.ObtenerPorIdAsync(id, SedeId!.Value, ct);
        if (existente is null) return NotFound();

        existente.Nombre = dto.Nombre.Trim();
        existente.UnidadMedida = dto.UnidadMedida.Trim();
        existente.StockMinimo = dto.StockMinimo;
        existente.Activo = dto.Activo;
        await _repo.ActualizarAsync(existente, SedeId!.Value, ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Desactivar(int id, CancellationToken ct)
    {
        var existente = await _repo.ObtenerPorIdAsync(id, SedeId!.Value, ct);
        if (existente is null) return NotFound();
        await _repo.CambiarEstadoAsync(id, false, SedeId!.Value, ct);
        return Ok(new { mensaje = "Insumo desactivado." });
    }

    [HttpPatch("{id:int}/estado")]
    public async Task<IActionResult> CambiarEstado(int id, [FromBody] CambiarEstadoUsuarioRequest req, CancellationToken ct)
    {
        var existente = await _repo.ObtenerPorIdAsync(id, SedeId!.Value, ct);
        if (existente is null) return NotFound();
        await _repo.CambiarEstadoAsync(id, req.Activo, SedeId!.Value, ct);
        return NoContent();
    }

    [HttpPost("{id:int}/movimientos")]
    public async Task<IActionResult> RegistrarMovimiento(int id, [FromBody] RegistrarMovimientoInsumoRequest req, CancellationToken ct)
    {
        req.Tipo = req.Tipo.Trim().ToUpperInvariant();
        var tiposValidos = new[] { "COMPRA", "CONSUMO", "AJUSTE" };
        if (!tiposValidos.Contains(req.Tipo)) return BadRequest(new { mensaje = "Tipo de movimiento inválido." });

        var insumo = await _repo.ObtenerPorIdAsync(id, SedeId!.Value, ct);
        if (insumo is null) return NotFound();

        if (req.Tipo != "AJUSTE" && req.Cantidad <= 0)
            return BadRequest(new { mensaje = "La cantidad debe ser mayor a 0." });
        if (req.Tipo == "AJUSTE" && req.Cantidad == 0)
            return BadRequest(new { mensaje = "El ajuste debe aumentar o disminuir el stock; la cantidad no puede ser 0." });

        if (req.CostoTotal is < 0)
            return BadRequest(new { mensaje = "El costo total no puede ser negativo." });

        if (req.Fecha is DateTime fecha && fecha.Date > DateTime.Today)
            return BadRequest(new { mensaje = "La fecha de compra no puede estar en el futuro." });

        if (!string.IsNullOrWhiteSpace(req.MetodoPago))
        {
            req.MetodoPago = req.MetodoPago.Trim().ToUpperInvariant();
            var metodosValidos = new[] { "EFECTIVO", "YAPE", "PLIN", "TRANSFERENCIA", "POS", "TARJETA" };
            if (!metodosValidos.Contains(req.MetodoPago))
                return BadRequest(new { mensaje = "Método de pago inválido." });
        }

        if (req.Tipo == "CONSUMO" && req.Cantidad > insumo.StockActual)
            return BadRequest(new { mensaje = $"No hay suficiente stock. Disponible: {insumo.StockActual} {insumo.UnidadMedida}." });

        if (req.Tipo == "COMPRA" && req.TipoGastoId is int tipoGastoId)
        {
            var tipoGasto = await _caja.ObtenerTipoGastoPorIdAsync(tipoGastoId, NegocioId, ct);
            if (tipoGasto is null)
                return BadRequest(new { mensaje = "El tipo de gasto no pertenece a este negocio." });
        }

        var movimiento = new MovimientoInsumo
        {
            SedeId = SedeId!.Value,
            InsumoId = id,
            InsumoNombre = insumo.Nombre,
            Tipo = req.Tipo,
            Cantidad = req.Cantidad,
            CostoTotal = req.CostoTotal,
            // Solo COMPRA permite fechar en el pasado (registrar una compra anterior).
            Fecha = req.Tipo == "COMPRA" && req.Fecha is DateTime f ? f.Date + DateTime.Now.TimeOfDay : DateTime.Now,
            UsuarioId = UsuarioId,
            Descripcion = req.Descripcion
        };

        try
        {
            var movimientoId = await _repo.RegistrarMovimientoAsync(movimiento, req.MetodoPago, req.TipoGastoId, ct);
            return Ok(new { id = movimientoId, mensaje = "Movimiento registrado." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    [HttpGet("movimientos")]
    public async Task<ActionResult<List<MovimientoInsumoDto>>> ListarMovimientos(
        [FromQuery] int? insumoId,
        [FromQuery] DateTime? desde,
        [FromQuery] DateTime? hasta,
        CancellationToken ct = default)
    {
        var h = (hasta ?? DateTime.Today).Date.AddDays(1);
        var d = (desde ?? h.AddDays(-31)).Date;
        var list = await _repo.ListarMovimientosAsync(insumoId, d, h, SedeId!.Value, ct);
        return Ok(list.Select(m => new MovimientoInsumoDto
        {
            Id = m.Id,
            InsumoId = m.InsumoId,
            InsumoNombre = m.InsumoNombre,
            Tipo = m.Tipo,
            Cantidad = m.Cantidad,
            CostoTotal = m.CostoTotal,
            Fecha = m.Fecha,
            UsuarioNombre = m.UsuarioNombre,
            Descripcion = m.Descripcion
        }).ToList());
    }

    private static InsumoDto Map(Insumo i) => new()
    {
        Id = i.Id,
        Nombre = i.Nombre,
        UnidadMedida = i.UnidadMedida,
        StockActual = i.StockActual,
        StockMinimo = i.StockMinimo,
        Activo = i.Activo,
        UltimaCompra = i.UltimaCompra
    };
}
