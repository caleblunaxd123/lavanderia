using Lavanderia.Api.Domain;
using Lavanderia.Api.Repositories;

namespace Lavanderia.Api.Infrastructure;

/// <summary>
/// Crea el usuario administrador inicial si aun no existe ningun usuario activo.
/// Los datos vienen de appsettings.json:SeedAdmin.
/// </summary>
public class DbInitializer
{
    private readonly IUsuarioRepository _usuarios;
    private readonly IRolRepository _roles;
    private readonly INegocioRepository _negocios;
    private readonly ISedeRepository _sedes;
    private readonly IConfiguration _config;
    private readonly ILogger<DbInitializer> _log;

    public DbInitializer(
        IUsuarioRepository usuarios,
        IRolRepository roles,
        INegocioRepository negocios,
        ISedeRepository sedes,
        IConfiguration config,
        ILogger<DbInitializer> log)
    {
        _usuarios = usuarios;
        _roles = roles;
        _negocios = negocios;
        _sedes = sedes;
        _config = config;
        _log = log;
    }

    public async Task EjecutarAsync(CancellationToken ct = default)
    {
        var totalActivos = await _usuarios.ContarActivosAsync(ct);
        if (totalActivos > 0)
        {
            _log.LogInformation("Ya existen usuarios activos, no se crea admin inicial.");
            return;
        }

        var rolAdmin = await _roles.BuscarPorCodigoAsync("ADMIN", ct)
            ?? throw new InvalidOperationException("Rol ADMIN no encontrado. Ejecuta 001_schema.sql y 002_seed.sql primero.");

        // Esto solo corre en una instalacion nueva (sin usuarios aun). Para la base de
        // datos existente de Lavixa, el Negocio/Sede se crean via 018_multi_tenant.sql.
        var nombreNegocio = _config.GetValue<string>("SeedAdmin:NombreNegocio") ?? "Mi Negocio";
        var slug = _config.GetValue<string>("SeedAdmin:Slug") ?? Slugificar(nombreNegocio);
        var negocioId = await _negocios.CrearAsync(new Negocio { Nombre = nombreNegocio, Slug = slug, Activo = true }, ct);
        var sedeId = await _sedes.CrearAsync(new Sede { NegocioId = negocioId, Nombre = "Principal", Activo = true }, ct);

        var usuario = _config.GetValue<string>("SeedAdmin:Usuario") ?? "admin";
        var password = _config.GetValue<string>("SeedAdmin:Password") ?? "admin123";
        var nombre = _config.GetValue<string>("SeedAdmin:NombreCompleto") ?? "Administrador";

        var id = await _usuarios.CrearAsync(new Usuario
        {
            UsuarioLogin = usuario,
            NombreCompleto = nombre,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            RolId = rolAdmin.Id,
            RolCodigo = rolAdmin.Codigo,
            NegocioId = negocioId,
            SedeId = sedeId,
            Activo = true
        }, ct);

        _log.LogWarning(
            "Usuario admin creado con id={Id} (negocioId={NegocioId}, sedeId={SedeId}). Credenciales por defecto: {Usuario}/{Password}. CAMBIAR EN PRODUCCION.",
            id, negocioId, sedeId, usuario, password);
    }

    /// <summary>Slug de URL a partir del nombre del negocio (minusculas, sin acentos ni espacios).</summary>
    private static string Slugificar(string nombre)
    {
        var normalizado = nombre.Normalize(System.Text.NormalizationForm.FormD);
        var sinAcentos = new string(normalizado
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .ToArray());
        var slug = System.Text.RegularExpressions.Regex.Replace(sinAcentos.ToLowerInvariant(), "[^a-z0-9]+", "");
        return string.IsNullOrWhiteSpace(slug) ? "negocio" : slug;
    }
}
