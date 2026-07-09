using Lavanderia.Api.Domain;
using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Lavanderia.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "ADMIN")]
public class UsuariosController : ControllerBase
{
    private readonly IUsuarioRepository _usuarios;
    private readonly IRolRepository _roles;

    public UsuariosController(IUsuarioRepository usuarios, IRolRepository roles)
    {
        _usuarios = usuarios;
        _roles = roles;
    }

    // El nuevo usuario siempre pertenece al mismo negocio que el admin que lo crea.
    private int NegocioIdActual => int.Parse(User.FindFirstValue("negocioId") ?? "0");

    [HttpGet]
    public async Task<ActionResult<List<UsuarioAdminDto>>> Listar(CancellationToken ct)
        => Ok((await _usuarios.ListarTodosAsync(NegocioIdActual, ct)).Select(Map).ToList());

    [HttpGet("roles")]
    public async Task<ActionResult<List<RolDto>>> Roles(CancellationToken ct)
        => Ok((await _roles.ListarTodosAsync(ct)).Select(r => new RolDto(r.Id, r.Codigo, r.Nombre)).ToList());

    [HttpPost]
    public async Task<ActionResult<UsuarioAdminDto>> Crear([FromBody] UsuarioAdminDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Password) || dto.Password.Length < 4)
            return BadRequest(new { mensaje = "La contraseña debe tener al menos 4 caracteres." });

        var existente = await _usuarios.BuscarPorUsuarioAsync(dto.Usuario.Trim(), ct);
        if (existente is not null)
            return Conflict(new { mensaje = "Ya existe un usuario con ese nombre de acceso." });

        var id = await _usuarios.CrearAsync(new Usuario
        {
            UsuarioLogin = dto.Usuario.Trim(),
            NombreCompleto = dto.NombreCompleto.Trim(),
            Email = dto.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
            RolId = dto.RolId,
            Activo = dto.Activo,
            NegocioId = NegocioIdActual
        }, ct);

        var creado = await _usuarios.ObtenerPorIdAsync(id, NegocioIdActual, ct);
        return CreatedAtAction(nameof(Listar), Map(creado!));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Actualizar(int id, [FromBody] UsuarioAdminDto dto, CancellationToken ct)
    {
        var existente = await _usuarios.ObtenerPorIdAsync(id, NegocioIdActual, ct);
        if (existente is null) return NotFound();

        existente.UsuarioLogin = dto.Usuario.Trim();
        existente.NombreCompleto = dto.NombreCompleto.Trim();
        existente.Email = dto.Email;
        existente.RolId = dto.RolId;
        existente.Activo = dto.Activo;
        await _usuarios.ActualizarAsync(existente, NegocioIdActual, ct);

        if (!string.IsNullOrWhiteSpace(dto.Password))
        {
            if (dto.Password.Length < 4)
                return BadRequest(new { mensaje = "La contraseña debe tener al menos 4 caracteres." });
            await _usuarios.ActualizarPasswordAsync(id, BCrypt.Net.BCrypt.HashPassword(dto.Password), NegocioIdActual, ct);
        }

        return NoContent();
    }

    [HttpPatch("{id:int}/estado")]
    public async Task<IActionResult> CambiarEstado(int id, [FromBody] CambiarEstadoUsuarioRequest req, CancellationToken ct)
    {
        var existente = await _usuarios.ObtenerPorIdAsync(id, NegocioIdActual, ct);
        if (existente is null) return NotFound();
        await _usuarios.CambiarEstadoAsync(id, req.Activo, NegocioIdActual, ct);
        return NoContent();
    }

    private static UsuarioAdminDto Map(Usuario u) => new()
    {
        Id = u.Id,
        Usuario = u.UsuarioLogin,
        NombreCompleto = u.NombreCompleto,
        Email = u.Email,
        RolId = u.RolId,
        RolCodigo = u.RolCodigo,
        Activo = u.Activo
    };
}
