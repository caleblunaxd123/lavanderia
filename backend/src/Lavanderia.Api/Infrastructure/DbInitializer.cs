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
    private readonly IConfiguracionNegocioRepository _configuracion;
    private readonly IConfiguration _config;
    private readonly ILogger<DbInitializer> _log;

    public DbInitializer(
        IUsuarioRepository usuarios,
        IRolRepository roles,
        INegocioRepository negocios,
        ISedeRepository sedes,
        IConfiguracionNegocioRepository configuracion,
        IConfiguration config,
        ILogger<DbInitializer> log)
    {
        _usuarios = usuarios;
        _roles = roles;
        _negocios = negocios;
        _sedes = sedes;
        _configuracion = configuracion;
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
        await _configuracion.ActualizarAsync(new ConfiguracionNegocio
        {
            NombreNegocio = nombreNegocio,
            ColorPrimario = "#0b57d0",
            ColorSecundario = "#29b6f6",
            ColorAcento = "#f5a623",
            Igv = 18m,
            MetaMensual = 0m,
            SolesPorPunto = 1m,
            AnchoTicketMm = 80,
            MensajePieTicket = "Gracias por su preferencia.",
            CostoDelivery = 0m
        }, negocioId, ct);

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

        // No se loguea la contrasena en texto plano: cualquier sistema de recoleccion de logs
        // quedaria con la clave real del admin. El operador ya la conoce (la puso en config).
        _log.LogWarning(
            "Usuario admin '{Usuario}' creado con id={Id} (negocioId={NegocioId}, sedeId={SedeId}) usando la contrasena de SeedAdmin:Password. CAMBIALA apenas inicies sesion.",
            usuario, id, negocioId, sedeId);
    }

    /// <summary>
    /// Crea el primer usuario PROPIETARIO (dueno de la plataforma SaaS) si aun no existe.
    /// Independiente de EjecutarAsync(): ese corta en seco si YA hay usuarios activos (que es
    /// el caso normal, ya que este sistema viene con datos), asi que este seed necesita su
    /// propio gate de idempotencia (existe el usuario configurado?) en vez de reusar aquel.
    /// El rol PROPIETARIO y el negocio reservado "plataforma-interna" los crea la migracion
    /// 021_propietario_plataforma.sql antes de que esto corra.
    /// </summary>
    public async Task EjecutarPropietarioAsync(CancellationToken ct = default)
    {
        var usuario = _config.GetValue<string>("SeedPropietario:Usuario") ?? "propietario";

        var existente = await _usuarios.BuscarPropietarioPorUsuarioAsync(usuario, ct);
        if (existente is not null)
        {
            _log.LogInformation("Usuario propietario ya existe, no se crea de nuevo.");
            return;
        }

        var rolPropietario = await _roles.BuscarPorCodigoAsync("PROPIETARIO", ct);
        var negocioPlataforma = await _negocios.ObtenerPorSlugIncluyendoInactivoAsync("plataforma-interna", ct);
        if (rolPropietario is null || negocioPlataforma is null)
        {
            _log.LogWarning("No se pudo crear el usuario propietario: falta aplicar 021_propietario_plataforma.sql.");
            return;
        }

        var password = _config.GetValue<string>("SeedPropietario:Password") ?? "propietario123";
        var nombre = _config.GetValue<string>("SeedPropietario:NombreCompleto") ?? "Propietario";

        var id = await _usuarios.CrearAsync(new Usuario
        {
            UsuarioLogin = usuario,
            NombreCompleto = nombre,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            RolId = rolPropietario.Id,
            RolCodigo = rolPropietario.Codigo,
            NegocioId = negocioPlataforma.Id,
            SedeId = null,
            Activo = true
        }, ct);

        _log.LogWarning(
            "Usuario propietario '{Usuario}' creado con id={Id} usando la contrasena de SeedPropietario:Password. CAMBIALA apenas inicies sesion.",
            usuario, id);
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
