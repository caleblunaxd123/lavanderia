using Lavanderia.Api.Domain;
using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

[Route("api/[controller]")]
public class CajaController : TenantAwareControllerBase
{
    private readonly ICajaRepository _repo;
    public CajaController(ICajaRepository repo) => _repo = repo;

    [HttpGet("tipos-gasto")]
    public async Task<ActionResult<List<TipoGastoDto>>> TiposGasto(CancellationToken ct)
        => Ok((await _repo.ListarTiposGastoAsync(NegocioId, ct)).Select(t => new TipoGastoDto(t.Id, t.Nombre)).ToList());

    [HttpGet("movimientos")]
    public async Task<ActionResult<List<MovimientoCajaDto>>> Movimientos(
        [FromQuery] DateTime? fecha, [FromQuery] int? usuarioId, CancellationToken ct)
    {
        var lista = await _repo.ListarMovimientosAsync(fecha ?? DateTime.Today, usuarioId, SedeId!.Value, ct);
        return Ok(lista.Select(Map).ToList());
    }

    [HttpGet("usuarios-dia")]
    public async Task<ActionResult<List<UsuarioDelDiaDto>>> UsuariosDelDia([FromQuery] DateTime? fecha, CancellationToken ct)
        => Ok(await _repo.UsuariosDelDiaAsync(fecha ?? DateTime.Today, SedeId!.Value, ct));

    [HttpGet("cuadres/del-usuario")]
    public async Task<ActionResult<CuadreCajaDto>> CuadreDelUsuario(
        [FromQuery] DateTime fecha, [FromQuery] int usuarioId, CancellationToken ct)
    {
        var c = await _repo.ObtenerCuadreDelUsuarioAsync(fecha, usuarioId, SedeId!.Value, ct);
        if (c is null) return NotFound();
        return Ok(MapCuadre(c));
    }

    [HttpPost("gastos")]
    public async Task<ActionResult<MovimientoCajaDto>> RegistrarGasto([FromBody] RegistrarGastoRequest req, CancellationToken ct)
    {
        var metodosValidos = new[] { "EFECTIVO", "YAPE", "PLIN", "TRANSFERENCIA", "POS" };
        if (!metodosValidos.Contains(req.MetodoPago.ToUpperInvariant()))
            return BadRequest(new { mensaje = "Método de pago inválido." });
        if (req.Monto <= 0)
            return BadRequest(new { mensaje = "El monto debe ser mayor a 0." });
        if (req.TipoGastoId is int tipoGastoId)
        {
            var tipoGasto = await _repo.ObtenerTipoGastoPorIdAsync(tipoGastoId, NegocioId, ct);
            if (tipoGasto is null)
                return BadRequest(new { mensaje = "El tipo de gasto no pertenece a este negocio." });
        }

        var mov = new MovimientoCaja
        {
            SedeId = SedeId!.Value,
            Fecha = DateTime.Now,
            Tipo = "GASTO",
            MetodoPago = req.MetodoPago.ToUpperInvariant(),
            Monto = req.Monto,
            Descripcion = req.Descripcion,
            TipoGastoId = req.TipoGastoId,
            UsuarioId = UsuarioId
        };
        mov.Id = await _repo.RegistrarGastoAsync(mov, NegocioId, ct);
        return CreatedAtAction(nameof(Movimientos), Map(mov));
    }

    [HttpPost("cuadres")]
    public async Task<ActionResult<CuadreCajaDto>> GuardarCuadre([FromBody] GuardarCuadreRequest req, CancellationToken ct)
    {
        var cuadre = new CuadreCaja
        {
            SedeId = SedeId!.Value,
            Fecha = req.Fecha.Date,
            UsuarioId = UsuarioId,
            CajaInicial = req.CajaInicial,
            PedidosPagadosEfect = req.PedidosPagadosEfect,
            Gastos = req.Gastos,
            TotalContado = req.TotalContado,
            Diferencia = req.Diferencia,
            CajaFinal = req.CajaFinal,
            Observaciones = req.Observaciones
        };
        var id = await _repo.GuardarCuadreAsync(cuadre, ct);
        var guardado = await _repo.ObtenerCuadreAsync(id, SedeId!.Value, ct);
        return Ok(MapCuadre(guardado!));
    }

    [HttpGet("cuadres/{id:int}")]
    public async Task<ActionResult<CuadreCajaDto>> ObtenerCuadre(int id, CancellationToken ct)
    {
        var c = await _repo.ObtenerCuadreAsync(id, SedeId!.Value, ct);
        if (c is null) return NotFound();
        return Ok(MapCuadre(c));
    }

    [HttpGet("cuadres/ultimo-anterior")]
    public async Task<ActionResult<CuadreCajaDto>> ObtenerUltimoAnterior([FromQuery] DateTime fecha, CancellationToken ct)
    {
        var c = await _repo.ObtenerUltimoAnteriorAsync(fecha, SedeId!.Value, ct);
        if (c is null) return NotFound();
        return Ok(MapCuadre(c));
    }

    private static MovimientoCajaDto Map(MovimientoCaja m) => new()
    {
        Id = m.Id,
        Fecha = m.Fecha,
        Tipo = m.Tipo,
        MetodoPago = m.MetodoPago,
        Monto = m.Monto,
        Descripcion = m.Descripcion,
        PedidoId = m.PedidoId,
        TipoGastoId = m.TipoGastoId,
        TipoGastoNombre = m.TipoGastoNombre
    };

    private static CuadreCajaDto MapCuadre(CuadreCaja c) => new()
    {
        Id = c.Id,
        Fecha = c.Fecha,
        UsuarioId = c.UsuarioId,
        UsuarioNombre = c.UsuarioNombre,
        CajaInicial = c.CajaInicial,
        PedidosPagadosEfect = c.PedidosPagadosEfect,
        Gastos = c.Gastos,
        TotalContado = c.TotalContado,
        Diferencia = c.Diferencia,
        CajaFinal = c.CajaFinal,
        Observaciones = c.Observaciones,
        FechaCreacion = c.FechaCreacion
    };
}
