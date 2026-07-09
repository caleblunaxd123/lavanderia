using Lavanderia.Api.Auth;
using Lavanderia.Api.Domain;
using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Lavanderia.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IUsuarioRepository _usuarios;
    private readonly IRolPermisoRepository _permisos;
    private readonly ISedeRepository _sedes;
    private readonly INegocioRepository _negocios;
    private readonly ITokenService _tokens;

    public AuthController(IUsuarioRepository usuarios, IRolPermisoRepository permisos, ISedeRepository sedes, INegocioRepository negocios, ITokenService tokens)
    {
        _usuarios = usuarios;
        _permisos = permisos;
        _sedes = sedes;
        _negocios = negocios;
        _tokens = tokens;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        var usuario = await _usuarios.BuscarPorUsuarioAsync(req.Usuario, ct);
        if (usuario is null || !usuario.Activo)
            return Unauthorized(new { mensaje = "Usuario o contraseña incorrectos." });

        if (!BCrypt.Net.BCrypt.Verify(req.Password, usuario.PasswordHash))
            return Unauthorized(new { mensaje = "Usuario o contraseña incorrectos." });

        // Si la URL trae el slug de una empresa, el usuario autenticado debe pertenecer a ella.
        // Sin esto, cualquier usuario global podria "entrar" visualmente por la URL de otra empresa.
        if (!string.IsNullOrWhiteSpace(req.EmpresaSlug))
        {
            var negocioDeLaUrl = await _negocios.ObtenerPorSlugAsync(req.EmpresaSlug, ct);
            if (negocioDeLaUrl is null || negocioDeLaUrl.Id != usuario.NegocioId)
                return Unauthorized(new { mensaje = "Usuario o contraseña incorrectos." });
        }

        var (token, expira) = _tokens.GenerarAccessToken(usuario);
        var modulos = await ObtenerModulosAsync(usuario, ct);

        return Ok(new LoginResponse(
            token,
            expira,
            new UsuarioDto(usuario.Id, usuario.UsuarioLogin, usuario.NombreCompleto, usuario.RolCodigo, modulos,
                usuario.NegocioId, usuario.SedeId, usuario.SedeNombre)
        ));
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<UsuarioDto>> Me(CancellationToken ct)
    {
        var id = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var u = await _usuarios.ObtenerPorIdAsync(id, ct);
        if (u is null) return NotFound();
        var modulos = await ObtenerModulosAsync(u, ct);
        return Ok(new UsuarioDto(u.Id, u.UsuarioLogin, u.NombreCompleto, u.RolCodigo, modulos,
            u.NegocioId, u.SedeId, u.SedeNombre));
    }

    /// <summary>
    /// Cambia la sede activa de la sesion actual (no persiste el cambio en la BD, solo
    /// reemite el JWT con el SedeId elegido). Pensado para ADMIN con acceso a varias sedes.
    /// </summary>
    [HttpPost("seleccionar-sede")]
    [Authorize]
    public async Task<ActionResult<LoginResponse>> SeleccionarSede([FromBody] SeleccionarSedeRequest req, CancellationToken ct)
    {
        var id = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var usuario = await _usuarios.ObtenerPorIdAsync(id, ct);
        if (usuario is null) return NotFound();

        var sede = await _sedes.ObtenerPorIdAsync(req.SedeId, ct);
        if (sede is null || sede.NegocioId != usuario.NegocioId)
            return BadRequest(new { mensaje = "La sede no pertenece a tu negocio." });

        if (usuario.SedeId is int sedeAsignada && sedeAsignada != req.SedeId)
            return BadRequest(new { mensaje = "Tu usuario esta asignado a otra sede." });

        usuario.SedeId = sede.Id;
        usuario.SedeNombre = sede.Nombre;
        var (token, expira) = _tokens.GenerarAccessToken(usuario);
        var modulos = await ObtenerModulosAsync(usuario, ct);

        return Ok(new LoginResponse(
            token,
            expira,
            new UsuarioDto(usuario.Id, usuario.UsuarioLogin, usuario.NombreCompleto, usuario.RolCodigo, modulos,
                usuario.NegocioId, usuario.SedeId, usuario.SedeNombre)
        ));
    }

    private async Task<List<string>> ObtenerModulosAsync(Domain.Usuario usuario, CancellationToken ct)
    {
        if (usuario.RolCodigo == "ADMIN") return Modulos.Todos.ToList();
        return await _permisos.ObtenerModulosPermitidosPorRolAsync(usuario.RolId, usuario.NegocioId, ct);
    }
}
