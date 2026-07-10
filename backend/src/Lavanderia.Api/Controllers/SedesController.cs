using Lavanderia.Api.Domain;
using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

/// <summary>
/// CRUD de sedes/sucursales del negocio (solo ADMIN). Cualquier usuario autenticado puede
/// listar las sedes de su negocio (para el selector del header); crear/editar es solo ADMIN.
/// </summary>
[Route("api/[controller]")]
[Authorize(Policy = "Modulo:AJUSTES")]
public class SedesController : TenantAwareControllerBase
{
    private readonly ISedeRepository _repo;
    public SedesController(ISedeRepository repo) => _repo = repo;

    [HttpGet]
    public async Task<ActionResult<List<SedeDto>>> Listar(CancellationToken ct)
        => Ok((await _repo.ListarPorNegocioAsync(NegocioId, ct)).Select(Map).ToList());

    [HttpPost]
    [Authorize(Roles = "ADMIN")]
    public async Task<ActionResult<SedeDto>> Crear([FromBody] SedeDto dto, CancellationToken ct)
    {
        var id = await _repo.CrearAsync(new Sede
        {
            NegocioId = NegocioId,
            Nombre = dto.Nombre.Trim(),
            Direccion = dto.Direccion,
            Telefono = dto.Telefono,
            Activo = dto.Activo
        }, ct);
        var creada = await _repo.ObtenerPorIdAsync(id, ct);
        return CreatedAtAction(nameof(Listar), Map(creada!));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> Actualizar(int id, [FromBody] SedeDto dto, CancellationToken ct)
    {
        var existente = await _repo.ObtenerPorIdAsync(id, ct);
        if (existente is null || existente.NegocioId != NegocioId) return NotFound();

        existente.Nombre = dto.Nombre.Trim();
        existente.Direccion = dto.Direccion;
        existente.Telefono = dto.Telefono;
        existente.Activo = dto.Activo;
        await _repo.ActualizarAsync(existente, NegocioId, ct);
        return NoContent();
    }

    [HttpPatch("{id:int}/estado")]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> CambiarEstado(int id, [FromBody] CambiarEstadoUsuarioRequest req, CancellationToken ct)
    {
        var existente = await _repo.ObtenerPorIdAsync(id, ct);
        if (existente is null || existente.NegocioId != NegocioId) return NotFound();
        await _repo.CambiarEstadoAsync(id, req.Activo, NegocioId, ct);
        return NoContent();
    }

    private static SedeDto Map(Sede s) => new()
    {
        Id = s.Id,
        Nombre = s.Nombre,
        Direccion = s.Direccion,
        Telefono = s.Telefono,
        Activo = s.Activo
    };
}
