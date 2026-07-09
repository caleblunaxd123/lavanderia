using Lavanderia.Api.Domain;
using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

[Route("api/[controller]")]
[Authorize(Roles = "ADMIN")]
public class PermisosController : TenantAwareControllerBase
{
    private readonly IRolPermisoRepository _repo;
    public PermisosController(IRolPermisoRepository repo) => _repo = repo;

    [HttpGet("modulos")]
    public ActionResult<List<string>> Modulos() => Ok(Domain.Modulos.Todos.ToList());

    [HttpGet]
    public async Task<ActionResult<List<PermisoItemDto>>> ObtenerMatriz(CancellationToken ct)
    {
        var matriz = await _repo.ObtenerMatrizAsync(NegocioId, ct);
        return Ok(matriz
            .Where(p => p.RolCodigo != "ADMIN") // ADMIN siempre tiene acceso total, no se edita
            .Select(p => new PermisoItemDto(p.RolId, p.Modulo, p.PuedeAcceder))
            .ToList());
    }

    [HttpPut]
    public async Task<IActionResult> Guardar([FromBody] ActualizarPermisosRequest req, CancellationToken ct)
    {
        foreach (var item in req.Permisos)
        {
            if (!Domain.Modulos.Todos.Contains(item.Modulo)) continue;
            await _repo.GuardarAsync(item.RolId, item.Modulo, item.PuedeAcceder, NegocioId, ct);
        }
        return NoContent();
    }
}
