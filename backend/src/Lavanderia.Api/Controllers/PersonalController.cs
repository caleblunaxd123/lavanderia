using Lavanderia.Api.Domain;
using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

[Route("api/personal")]
[Authorize(Roles = "ADMIN")]
public class PersonalController : TenantAwareControllerBase
{
    private readonly IEmpleadoRepository _repo;
    public PersonalController(IEmpleadoRepository repo) => _repo = repo;

    [HttpGet]
    public async Task<ActionResult<List<EmpleadoDto>>> Listar(CancellationToken ct)
        => Ok((await _repo.ListarTodosAsync(SedeId!.Value, ct)).Select(Map).ToList());

    [HttpPost]
    public async Task<ActionResult<EmpleadoDto>> Crear([FromBody] EmpleadoDto dto, CancellationToken ct)
    {
        var id = await _repo.CrearAsync(new Empleado
        {
            SedeId = SedeId!.Value,
            Nombre = dto.Nombre.Trim(),
            Dni = dto.Dni?.Trim(),
            Celular = dto.Celular?.Trim(),
            Cargo = dto.Cargo?.Trim(),
            FechaIngreso = dto.FechaIngreso,
            Activo = dto.Activo
        }, ct);
        var creado = await _repo.ObtenerPorIdAsync(id, SedeId!.Value, ct);
        return CreatedAtAction(nameof(Listar), Map(creado!));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Actualizar(int id, [FromBody] EmpleadoDto dto, CancellationToken ct)
    {
        var existente = await _repo.ObtenerPorIdAsync(id, SedeId!.Value, ct);
        if (existente is null) return NotFound();

        existente.Nombre = dto.Nombre.Trim();
        existente.Dni = dto.Dni?.Trim();
        existente.Celular = dto.Celular?.Trim();
        existente.Cargo = dto.Cargo?.Trim();
        existente.FechaIngreso = dto.FechaIngreso;
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
        return Ok(new { mensaje = "Empleado desactivado." });
    }

    [HttpPatch("{id:int}/estado")]
    public async Task<IActionResult> CambiarEstado(int id, [FromBody] CambiarEstadoUsuarioRequest req, CancellationToken ct)
    {
        var existente = await _repo.ObtenerPorIdAsync(id, SedeId!.Value, ct);
        if (existente is null) return NotFound();
        await _repo.CambiarEstadoAsync(id, req.Activo, SedeId!.Value, ct);
        return NoContent();
    }

    private static EmpleadoDto Map(Empleado e) => new()
    {
        Id = e.Id,
        Nombre = e.Nombre,
        Dni = e.Dni,
        Celular = e.Celular,
        Cargo = e.Cargo,
        FechaIngreso = e.FechaIngreso,
        Activo = e.Activo
    };
}
