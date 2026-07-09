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
    private readonly ISedeRepository _sedes;

    public UsuariosController(IUsuarioRepository usuarios, IRolRepository roles, ISedeRepository sedes)
    {
        _usuarios = usuarios;
        _roles = roles;
        _sedes = sedes;
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

        if (!await RolSedeValidaAsync(dto.RolId, dto.SedeId, ct))
            return BadRequest(new { mensaje = "Los usuarios no administradores deben estar asignados a una sede." });

        if (!await SedeValidaAsync(dto.SedeId, ct))
            return BadRequest(new { mensaje = "La sede seleccionada no existe o no esta activa." });

        var id = await _usuarios.CrearAsync(new Usuario
        {
            UsuarioLogin = dto.Usuario.Trim(),
            NombreCompleto = dto.NombreCompleto.Trim(),
            Email = dto.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
            RolId = dto.RolId,
            Activo = dto.Activo,
            NegocioId = NegocioIdActual,
            SedeId = dto.SedeId
        }, ct);

        var creado = await _usuarios.ObtenerPorIdAsync(id, NegocioIdActual, ct);
        return CreatedAtAction(nameof(Listar), Map(creado!));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Actualizar(int id, [FromBody] UsuarioAdminDto dto, CancellationToken ct)
    {
        var existente = await _usuarios.ObtenerPorIdAsync(id, NegocioIdActual, ct);
        if (existente is null) return NotFound();

        if (!await RolSedeValidaAsync(dto.RolId, dto.SedeId, ct))
            return BadRequest(new { mensaje = "Los usuarios no administradores deben estar asignados a una sede." });

        if (!await SedeValidaAsync(dto.SedeId, ct))
            return BadRequest(new { mensaje = "La sede seleccionada no existe o no esta activa." });

        existente.UsuarioLogin = dto.Usuario.Trim();
        existente.NombreCompleto = dto.NombreCompleto.Trim();
        existente.Email = dto.Email;
        existente.RolId = dto.RolId;
        existente.Activo = dto.Activo;
        existente.SedeId = dto.SedeId;
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
        SedeId = u.SedeId,
        SedeNombre = u.SedeNombre,
        RolCodigo = u.RolCodigo,
        Activo = u.Activo
    };

    private async Task<bool> SedeValidaAsync(int? sedeId, CancellationToken ct)
    {
        if (sedeId is null) return true;
        var sede = await _sedes.ObtenerPorIdAsync(sedeId.Value, ct);
        return sede is not null && sede.NegocioId == NegocioIdActual && sede.Activo;
    }

    private async Task<bool> RolSedeValidaAsync(int rolId, int? sedeId, CancellationToken ct)
    {
        if (sedeId is not null) return true;
        var admin = await _roles.BuscarPorCodigoAsync("ADMIN", ct);
        return admin is not null && admin.Id == rolId;
    }
}
