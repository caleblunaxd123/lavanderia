using System.Text.RegularExpressions;
using Lavanderia.Api.Domain;
using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;
using Lavanderia.Api.Services.Facturacion;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

[Route("api/pagos")]
[Authorize(Policy = "Modulo:AJUSTES")]
public class PagosController : TenantAwareControllerBase
{
    private static readonly Regex PublicKeyRegex = new("^pk_(test|live)_[A-Za-z0-9]+$", RegexOptions.Compiled);
    private static readonly Regex SecretKeyRegex = new("^sk_(test|live)_[A-Za-z0-9]+$", RegexOptions.Compiled);

    private readonly IPagosRepository _pagos;
    private readonly SecretProtector _secretos;

    public PagosController(IPagosRepository pagos, SecretProtector secretos)
    {
        _pagos = pagos;
        _secretos = secretos;
    }

    [HttpGet("configuracion")]
    [Authorize(Roles = "ADMIN")]
    public async Task<ActionResult<ConfiguracionPagosDto>> ObtenerConfiguracion(CancellationToken ct)
    {
        var c = await _pagos.ObtenerConfigAsync(NegocioId, ct);
        return Ok(new ConfiguracionPagosDto
        {
            Proveedor = c?.Proveedor ?? "CULQI",
            PublicKey = c?.PublicKey,
            Activo = c?.Activo ?? false,
            TieneSecretKey = !string.IsNullOrEmpty(c?.SecretKeyCifrada)
        });
    }

    [HttpPut("configuracion")]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> GuardarConfiguracion([FromBody] ConfiguracionPagosDto dto, CancellationToken ct)
    {
        var existente = await _pagos.ObtenerConfigAsync(NegocioId, ct) ?? new ConfiguracionPagos { NegocioId = NegocioId };
        var publicKey = string.IsNullOrWhiteSpace(dto.PublicKey) ? null : dto.PublicKey.Trim();
        var secretKeyNueva = string.IsNullOrWhiteSpace(dto.SecretKeyNueva) ? null : dto.SecretKeyNueva.Trim();

        if (publicKey is not null && !PublicKeyRegex.IsMatch(publicKey))
            return BadRequest(new { mensaje = "La llave pública de Culqi no tiene un formato válido." });

        if (secretKeyNueva is not null && !SecretKeyRegex.IsMatch(secretKeyNueva))
            return BadRequest(new { mensaje = "La llave secreta de Culqi no tiene un formato válido." });

        var entornoPublico = ObtenerEntorno(publicKey);
        var entornoSecreto = ObtenerEntorno(secretKeyNueva);
        if (entornoPublico is not null && entornoSecreto is not null && entornoPublico != entornoSecreto)
            return BadRequest(new { mensaje = "La llave pública y la llave secreta deben ser del mismo entorno (test o live)." });

        var tieneSecreta = secretKeyNueva is not null || !string.IsNullOrWhiteSpace(existente.SecretKeyCifrada);
        if (dto.Activo && (publicKey is null || !tieneSecreta))
            return BadRequest(new { mensaje = "Para activar pagos online debes guardar la llave pública y la llave secreta." });

        existente.Proveedor = string.IsNullOrWhiteSpace(dto.Proveedor) ? "CULQI" : dto.Proveedor.Trim().ToUpperInvariant();
        existente.PublicKey = publicKey;
        existente.Activo = dto.Activo;

        if (secretKeyNueva is not null)
            existente.SecretKeyCifrada = _secretos.Proteger(secretKeyNueva);

        await _pagos.GuardarConfigAsync(existente, ct);
        return NoContent();
    }

    private static string? ObtenerEntorno(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        return key.Contains("_test_", StringComparison.OrdinalIgnoreCase) ? "test"
            : key.Contains("_live_", StringComparison.OrdinalIgnoreCase) ? "live"
            : null;
    }
}
