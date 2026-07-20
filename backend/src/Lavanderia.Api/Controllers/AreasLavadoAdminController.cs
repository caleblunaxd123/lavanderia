using Lavanderia.Api.Domain;
using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

/// <summary>
/// Administración de áreas de lavado (solo ADMIN). El endpoint público /api/areas-lavado
/// (en CatalogosController) devuelve solo las activas, ordenadas, para el flujo de pedidos.
/// </summary>
[Route("api/areas-lavado-admin")]
[Authorize(Roles = "ADMIN")]
[Authorize(Policy = "Modulo:AJUSTES")]
public class AreasLavadoAdminController : TenantAwareControllerBase
{
    private readonly IAreaLavadoRepository _repo;
    public AreasLavadoAdminController(IAreaLavadoRepository repo) => _repo = repo;

    [HttpGet]
    public async Task<ActionResult<List<AreaLavadoEditableDto>>> Listar(CancellationToken ct)
        => Ok((await _repo.ListarTodasAsync(SedeId!.Value, ct)).Select(Map).ToList());

    [HttpPost]
    public async Task<ActionResult<AreaLavadoEditableDto>> Crear([FromBody] AreaLavadoEditableDto dto, CancellationToken ct)
    {
        var nombre = dto.Nombre.Trim();
        if (await _repo.ExisteNombreAsync(nombre, SedeId!.Value, ct: ct))
            return Conflict(new { mensaje = "Ya existe un area con ese nombre en esta sede." });
        if (await _repo.ExisteOrdenAsync(dto.Orden, SedeId.Value, ct: ct))
            return Conflict(new { mensaje = "Ya existe un area con ese orden en esta sede." });
        var id = await _repo.CrearAsync(new AreaLavado
        {
            SedeId = SedeId!.Value,
            Nombre = nombre,
            Orden = dto.Orden,
            TiempoEstMinutos = dto.TiempoEstMinutos,
            Activa = dto.Activa
        }, ct);
        var creada = await _repo.ObtenerPorIdAsync(id, SedeId!.Value, ct);
        return CreatedAtAction(nameof(Listar), Map(creada!));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Actualizar(int id, [FromBody] AreaLavadoEditableDto dto, CancellationToken ct)
    {
        var existente = await _repo.ObtenerPorIdAsync(id, SedeId!.Value, ct);
        if (existente is null) return NotFound();

        var nombre = dto.Nombre.Trim();
        if (await _repo.ExisteNombreAsync(nombre, SedeId!.Value, id, ct))
            return Conflict(new { mensaje = "Ya existe otra area con ese nombre en esta sede." });
        if (await _repo.ExisteOrdenAsync(dto.Orden, SedeId.Value, id, ct))
            return Conflict(new { mensaje = "Ya existe otra area con ese orden en esta sede." });
        existente.Nombre = nombre;
        existente.Orden = dto.Orden;
        existente.TiempoEstMinutos = dto.TiempoEstMinutos;
        existente.Activa = dto.Activa;
        await _repo.ActualizarAsync(existente, SedeId!.Value, ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Desactivar(int id, CancellationToken ct)
    {
        var existente = await _repo.ObtenerPorIdAsync(id, SedeId!.Value, ct);
        if (existente is null) return NotFound();

        var usos = await _repo.ContarUsoAsync(id, SedeId!.Value, ct);
        await _repo.CambiarEstadoAsync(id, false, SedeId!.Value, ct);
        return Ok(new
        {
            mensaje = usos > 0
                ? $"Área desactivada (tiene {usos} pedidos actualmente en ella)."
                : "Área eliminada."
        });
    }

    [HttpPatch("{id:int}/estado")]
    public async Task<IActionResult> Reactivar(int id, [FromBody] CambiarEstadoUsuarioRequest req, CancellationToken ct)
    {
        var existente = await _repo.ObtenerPorIdAsync(id, SedeId!.Value, ct);
        if (existente is null) return NotFound();
        await _repo.CambiarEstadoAsync(id, req.Activo, SedeId!.Value, ct);
        return NoContent();
    }

    private static AreaLavadoEditableDto Map(AreaLavado a) => new()
    {
        Id = a.Id,
        Nombre = a.Nombre,
        Orden = a.Orden,
        TiempoEstMinutos = a.TiempoEstMinutos,
        Activa = a.Activa
    };
}
