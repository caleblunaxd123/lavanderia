using Lavanderia.Api.Domain;
using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

[Route("api/[controller]")]
[Authorize(Roles = "ADMIN")]
[Authorize(Policy = "Modulo:PROMOCIONES")]
public class PromocionesController : TenantAwareControllerBase
{
    private readonly IPromocionRepository _repo;
    private readonly IServicioRepository _servicios;
    public PromocionesController(IPromocionRepository repo, IServicioRepository servicios)
    {
        _repo = repo;
        _servicios = servicios;
    }

    [HttpGet]
    public async Task<ActionResult<List<PromocionDto>>> Listar(CancellationToken ct)
        => Ok((await _repo.ListarTodasAsync(NegocioId, ct)).Select(Map).ToList());

    [HttpPost]
    public async Task<ActionResult<PromocionDto>> Crear([FromBody] PromocionDto dto, CancellationToken ct)
    {
        var error = await ValidarAsync(dto, null, ct);
        if (error is not null) return error;

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

        var error = await ValidarAsync(dto, id, ct);
        if (error is not null) return error;

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

    private async Task<ObjectResult?> ValidarAsync(PromocionDto dto, int? excluirId, CancellationToken ct)
    {
        var tiposValidos = new[] { "VOLUMEN", "FRECUENCIA", "FIJA", "CODIGO" };
        dto.Tipo = dto.Tipo.Trim().ToUpperInvariant();
        dto.Codigo = string.IsNullOrWhiteSpace(dto.Codigo) ? null : dto.Codigo.Trim().ToUpperInvariant();

        if (!tiposValidos.Contains(dto.Tipo))
            return BadRequest(new { mensaje = "El tipo de promocion no es valido." });
        if (dto.FechaInicio.HasValue && dto.FechaFin.HasValue && dto.FechaFin < dto.FechaInicio)
            return BadRequest(new { mensaje = "La fecha final no puede ser anterior a la fecha inicial." });

        var tienePorcentaje = dto.DescuentoPct is > 0;
        var tieneMonto = dto.DescuentoMonto is > 0;
        if (!tienePorcentaje && !tieneMonto)
            return BadRequest(new { mensaje = "Indica un descuento mayor a cero, en porcentaje o en soles." });
        if (tienePorcentaje && tieneMonto)
            return BadRequest(new { mensaje = "Usa solo un tipo de descuento: porcentaje o monto fijo." });

        if (dto.ServicioId is int servicioId)
        {
            var servicio = await _servicios.ObtenerPorIdAsync(servicioId, NegocioId, ct);
            if (servicio is null || !servicio.Activo)
                return BadRequest(new { mensaje = "El servicio seleccionado no existe o esta inactivo." });
        }

        if (dto.Codigo is not null)
        {
            var duplicada = await _repo.BuscarPorCodigoAsync(dto.Codigo, NegocioId, ct);
            if (duplicada is not null && duplicada.Id != excluirId)
                return Conflict(new { mensaje = "Ya existe una promocion con ese codigo." });
        }

        return null;
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
