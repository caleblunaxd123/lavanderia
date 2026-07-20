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
    private readonly ICategoriaRepository _categorias;

    public ServiciosAdminController(IServicioRepository repo, ICategoriaRepository categorias)
    {
        _repo = repo;
        _categorias = categorias;
    }

    [HttpGet]
    public async Task<ActionResult<List<ServicioEditableDto>>> Listar(CancellationToken ct)
        => Ok((await _repo.ListarTodosAsync(NegocioId, ct)).Select(Map).ToList());

    [HttpPost]
    public async Task<ActionResult<ServicioEditableDto>> Crear([FromBody] ServicioEditableDto dto, CancellationToken ct)
    {
        var validacion = await ValidarCatalogoAsync(dto, null, ct);
        if (validacion is not null) return validacion;

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
        if (existente.EsCargoDelivery)
            return BadRequest(new { mensaje = "El cargo interno de delivery se configura desde Negocio y marca." });

        var validacion = await ValidarCatalogoAsync(dto, id, ct);
        if (validacion is not null) return validacion;

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
        if (existente.EsCargoDelivery)
            return BadRequest(new { mensaje = "El cargo interno de delivery no puede desactivarse desde Servicios." });

        var usos = await _repo.ContarUsoAsync(id, NegocioId, ct);
        await _repo.CambiarEstadoAsync(id, false, NegocioId, ct);
        return Ok(new
        {
            mensaje = usos > 0
                ? $"Servicio desactivado (usado en {usos} pedidos históricos)."
                : "Servicio desactivado."
        });
    }

    private async Task<ActionResult?> ValidarCatalogoAsync(ServicioEditableDto dto, int? excluirId, CancellationToken ct)
    {
        var nombre = dto.Nombre.Trim();
        if (await _repo.ExisteNombreAsync(nombre, NegocioId, excluirId, ct))
            return Conflict(new { mensaje = $"Ya existe un servicio llamado '{nombre}' en esta empresa." });

        if (dto.CategoriaId.HasValue &&
            await _categorias.ObtenerPorIdAsync(dto.CategoriaId.Value, NegocioId, ct) is null)
            return BadRequest(new { mensaje = "La categoría seleccionada no pertenece a esta empresa." });

        return null;
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
