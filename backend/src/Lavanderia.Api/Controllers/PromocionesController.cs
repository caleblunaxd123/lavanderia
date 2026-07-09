using Lavanderia.Api.Domain;
using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

[Route("api/[controller]")]
[Authorize(Roles = "ADMIN")]
public class PromocionesController : TenantAwareControllerBase
{
    private readonly IPromocionRepository _repo;
    public PromocionesController(IPromocionRepository repo) => _repo = repo;

    [HttpGet]
    public async Task<ActionResult<List<PromocionDto>>> Listar(CancellationToken ct)
        => Ok((await _repo.ListarTodasAsync(NegocioId, ct)).Select(Map).ToList());

    [HttpPost]
    public async Task<ActionResult<PromocionDto>> Crear([FromBody] PromocionDto dto, CancellationToken ct)
    {
        var id = await _repo.CrearAsync(new Promocion
        {
            NegocioId = NegocioId,
            Tipo = dto.Tipo.Trim(),
            Descripcion = dto.Descripcion.Trim(),
            DescuentoPct = dto.DescuentoPct,
            DescuentoMonto = dto.DescuentoMonto,
            ServicioId = dto.ServicioId,
            CantidadMinima = dto.CantidadMinima,
            FechaInicio = dto.FechaInicio,
            FechaFin = dto.FechaFin,
            Activa = dto.Activa,
            Codigo = dto.Codigo
        }, ct);
        var creada = await _repo.ObtenerPorIdAsync(id, NegocioId, ct);
        return CreatedAtAction(nameof(Listar), Map(creada!));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Actualizar(int id, [FromBody] PromocionDto dto, CancellationToken ct)
    {
        var existente = await _repo.ObtenerPorIdAsync(id, NegocioId, ct);
        if (existente is null) return NotFound();

        existente.Tipo = dto.Tipo.Trim();
        existente.Descripcion = dto.Descripcion.Trim();
        existente.DescuentoPct = dto.DescuentoPct;
        existente.DescuentoMonto = dto.DescuentoMonto;
        existente.ServicioId = dto.ServicioId;
        existente.CantidadMinima = dto.CantidadMinima;
        existente.FechaInicio = dto.FechaInicio;
        existente.FechaFin = dto.FechaFin;
        existente.Activa = dto.Activa;
        existente.Codigo = dto.Codigo;
        await _repo.ActualizarAsync(existente, NegocioId, ct);
        return NoContent();
    }

    [HttpPatch("{id:int}/estado")]
    public async Task<IActionResult> CambiarEstado(int id, [FromBody] CambiarEstadoPromocionRequest req, CancellationToken ct)
    {
        var existente = await _repo.ObtenerPorIdAsync(id, NegocioId, ct);
        if (existente is null) return NotFound();
        await _repo.CambiarEstadoAsync(id, req.Activa, NegocioId, ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Eliminar(int id, CancellationToken ct)
    {
        var existente = await _repo.ObtenerPorIdAsync(id, NegocioId, ct);
        if (existente is null) return NotFound();
        await _repo.EliminarAsync(id, NegocioId, ct);
        return NoContent();
    }

    private static PromocionDto Map(Promocion p) => new()
    {
        Id = p.Id,
        Tipo = p.Tipo,
        Descripcion = p.Descripcion,
        DescuentoPct = p.DescuentoPct,
        DescuentoMonto = p.DescuentoMonto,
        ServicioId = p.ServicioId,
        ServicioNombre = p.ServicioNombre,
        CantidadMinima = p.CantidadMinima,
        FechaInicio = p.FechaInicio,
        FechaFin = p.FechaFin,
        Activa = p.Activa,
        Codigo = p.Codigo
    };
}
