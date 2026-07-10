using Lavanderia.Api.Domain;
using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

/// <summary>
/// Administración del catálogo de servicios (solo ADMIN).
/// El endpoint público /api/servicios (en CatalogosController) devuelve solo los activos para el wizard.
/// </summary>
[Route("api/servicios-admin")]
[Authorize(Roles = "ADMIN")]
[Authorize(Policy = "Modulo:AJUSTES")]
public class ServiciosAdminController : TenantAwareControllerBase
{
    private readonly IServicioRepository _repo;

    public ServiciosAdminController(IServicioRepository repo) => _repo = repo;

    [HttpGet]
    public async Task<ActionResult<List<ServicioEditableDto>>> Listar(CancellationToken ct)
        => Ok((await _repo.ListarTodosAsync(NegocioId, ct)).Select(Map).ToList());

    [HttpPost]
    public async Task<ActionResult<ServicioEditableDto>> Crear([FromBody] ServicioEditableDto dto, CancellationToken ct)
    {
        var id = await _repo.CrearAsync(new Servicio
        {
            NegocioId = NegocioId,
            Nombre = dto.Nombre.Trim(),
            Precio = dto.Precio,
            Unidad = dto.Unidad.Trim(),
            CategoriaId = dto.CategoriaId,
            Activo = dto.Activo
        }, ct);
        var creado = await _repo.ObtenerPorIdAsync(id, NegocioId, ct);
        return CreatedAtAction(nameof(Listar), Map(creado!));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Actualizar(int id, [FromBody] ServicioEditableDto dto, CancellationToken ct)
    {
        var existente = await _repo.ObtenerPorIdAsync(id, NegocioId, ct);
        if (existente is null) return NotFound();

        existente.Nombre = dto.Nombre.Trim();
        existente.Precio = dto.Precio;
        existente.Unidad = dto.Unidad.Trim();
        existente.CategoriaId = dto.CategoriaId;
        existente.Activo = dto.Activo;
        await _repo.ActualizarAsync(existente, NegocioId, ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Desactivar(int id, CancellationToken ct)
    {
        var existente = await _repo.ObtenerPorIdAsync(id, NegocioId, ct);
        if (existente is null) return NotFound();

        var usos = await _repo.ContarUsoAsync(id, NegocioId, ct);
        await _repo.CambiarEstadoAsync(id, false, NegocioId, ct);
        return Ok(new
        {
            mensaje = usos > 0
                ? $"Servicio desactivado (usado en {usos} pedidos históricos)."
                : "Servicio desactivado."
        });
    }

    private static ServicioEditableDto Map(Servicio s) => new()
    {
        Id = s.Id,
        Nombre = s.Nombre,
        Precio = s.Precio,
        Unidad = s.Unidad,
        CategoriaId = s.CategoriaId,
        CategoriaNombre = s.CategoriaNombre,
        Activo = s.Activo
    };
}
