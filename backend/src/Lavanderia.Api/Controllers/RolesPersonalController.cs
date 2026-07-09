using Lavanderia.Api.Domain;
using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

[Route("api/roles-personal")]
[Authorize(Roles = "ADMIN")]
public class RolesPersonalController : TenantAwareControllerBase
{
    private readonly IRolPersonalRepository _repo;
    public RolesPersonalController(IRolPersonalRepository repo) => _repo = repo;

    [HttpGet]
    public async Task<ActionResult<List<RolPersonalDto>>> Listar(CancellationToken ct)
        => Ok((await _repo.ListarTodosAsync(NegocioId, ct)).Select(Map).ToList());

    [HttpPost]
    public async Task<ActionResult<RolPersonalDto>> Crear([FromBody] RolPersonalDto dto, CancellationToken ct)
    {
        var id = await _repo.CrearAsync(new RolPersonal { NegocioId = NegocioId, Nombre = dto.Nombre.Trim(), Activo = dto.Activo }, ct);
        var creado = await _repo.ObtenerPorIdAsync(id, NegocioId, ct);
        return CreatedAtAction(nameof(Listar), Map(creado!));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Actualizar(int id, [FromBody] RolPersonalDto dto, CancellationToken ct)
    {
        var existente = await _repo.ObtenerPorIdAsync(id, NegocioId, ct);
        if (existente is null) return NotFound();

        existente.Nombre = dto.Nombre.Trim();
        existente.Activo = dto.Activo;
        await _repo.ActualizarAsync(existente, NegocioId, ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Desactivar(int id, CancellationToken ct)
    {
        var existente = await _repo.ObtenerPorIdAsync(id, NegocioId, ct);
        if (existente is null) return NotFound();

        await _repo.CambiarEstadoAsync(id, false, NegocioId, ct);
        return Ok(new { mensaje = "Rol desactivado." });
    }

    [HttpPatch("{id:int}/estado")]
    public async Task<IActionResult> CambiarEstado(int id, [FromBody] CambiarEstadoUsuarioRequest req, CancellationToken ct)
    {
        var existente = await _repo.ObtenerPorIdAsync(id, NegocioId, ct);
        if (existente is null) return NotFound();
        await _repo.CambiarEstadoAsync(id, req.Activo, NegocioId, ct);
        return NoContent();
    }

    private static RolPersonalDto Map(RolPersonal r) => new() { Id = r.Id, Nombre = r.Nombre, Activo = r.Activo };
}
