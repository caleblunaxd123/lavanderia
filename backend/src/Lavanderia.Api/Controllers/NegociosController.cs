using System.Text.RegularExpressions;
using Lavanderia.Api.Domain;
using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

/// <summary>
/// Alta y administracion de empresas (negocios/tenants) del SaaS. Solo el rol PROPIETARIO
/// (dueno de la plataforma) puede usarlo. A proposito NO hereda de TenantAwareControllerBase:
/// no opera acotado a un negocio, opera SOBRE los negocios. El aislamiento frente a los datos
/// operativos de cada tenant (pedidos, clientes, caja, etc.) es automatico: PROPIETARIO no es
/// ADMIN/COORDINADOR/TRABAJADOR, asi que los demas controllers lo rechazan (403) sin cambios.
/// </summary>
[ApiController]
[Authorize(Roles = "PROPIETARIO")]
[Route("api/negocios")]
public class NegociosController : ControllerBase
{
    // Debe reflejar core/routing/tenant-url-serializer.ts:SEGMENTOS_RESERVADOS del frontend
    // (lista chica y estatica) para que un negocio nuevo no pueda elegir un slug que colisione
    // con una ruta real de la app.
    private static readonly HashSet<string> SegmentosReservados = new(StringComparer.OrdinalIgnoreCase)
    {
        "login", "ticket", "cuadre-caja", "seleccionar-sede", "inicio", "pedidos", "registrar",
        "registro-antiguo", "clientes", "promociones", "reportes", "inventario", "ajustes",
        "facturacion", "assets", "plataforma", "seguimiento",
    };
    private static readonly Regex SlugValido = new("^[a-z0-9][a-z0-9-]{1,49}$", RegexOptions.IgnoreCase);
    private static readonly Regex UsuarioValido = new("^[a-z0-9._-]{3,50}$", RegexOptions.IgnoreCase);
    private static readonly Regex EmailBasicoValido = new("^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$", RegexOptions.IgnoreCase);

    private readonly INegocioRepository _negocios;
    private readonly ISedeRepository _sedes;
    private readonly IUsuarioRepository _usuarios;
    private readonly IRolRepository _roles;
    private readonly IRolPermisoRepository _permisos;
    private readonly IServicioRepository _servicios;
    private readonly IConfiguracionNegocioRepository _configuracion;

    public NegociosController(INegocioRepository negocios, ISedeRepository sedes,
        IUsuarioRepository usuarios, IRolRepository roles, IRolPermisoRepository permisos,
        IServicioRepository servicios, IConfiguracionNegocioRepository configuracion)
    {
        _negocios = negocios;
        _sedes = sedes;
        _usuarios = usuarios;
        _roles = roles;
        _permisos = permisos;
        _servicios = servicios;
        _configuracion = configuracion;
    }

    [HttpGet]
    public async Task<ActionResult<List<NegocioResumenDto>>> Listar(CancellationToken ct)
        => Ok(await _negocios.ListarConConteosAsync(ct));

    [HttpPost]
    public async Task<ActionResult<NegocioResumenDto>> Crear([FromBody] CrearNegocioRequest req, CancellationToken ct)
    {
        var slug = req.Slug.Trim().ToLowerInvariant();
        if (!SlugValido.IsMatch(slug) || SegmentosReservados.Contains(slug))
            return BadRequest(new { mensaje = "El slug debe ser corto (letras, numeros y guiones) y no puede ser una palabra reservada del sistema." });
        if (await _negocios.ExisteSlugAsync(slug, ct))
            return Conflict(new { mensaje = "Ya existe una empresa con ese slug." });

        var adminUsuario = req.AdminUsuario.Trim();
        if (!UsuarioValido.IsMatch(adminUsuario))
            return BadRequest(new { mensaje = "El usuario administrador solo puede usar letras, numeros, punto, guion y guion bajo." });

        var rucEmpresa = NormalizarOpcional(req.RucEmpresa);
        if (rucEmpresa is not null && !Regex.IsMatch(rucEmpresa, "^\\d{11}$"))
            return BadRequest(new { mensaje = "El RUC debe tener 11 digitos." });

        var titularEmail = NormalizarOpcional(req.TitularEmail);
        var adminEmail = NormalizarOpcional(req.AdminEmail);
        if (!EmailValido(titularEmail) || !EmailValido(adminEmail))
            return BadRequest(new { mensaje = "El email ingresado no tiene un formato valido." });

        var usuarioExistente = await _usuarios.BuscarPorUsuarioAsync(adminUsuario, ct);
        if (usuarioExistente is not null)
            return Conflict(new { mensaje = "Ya existe un usuario con ese nombre de acceso." });

        var rolAdmin = await _roles.BuscarPorCodigoAsync("ADMIN", ct)
            ?? throw new InvalidOperationException("Rol ADMIN no encontrado.");

        var negocioId = await _negocios.CrearAsync(new Negocio
        {
            Nombre = req.Nombre.Trim(),
            Slug = slug,
            RucEmpresa = rucEmpresa,
            TitularNombre = NormalizarOpcional(req.TitularNombre),
            TitularEmail = titularEmail,
            Activo = true
        }, ct);

        var sedeId = await _sedes.CrearAsync(new Sede
        {
            NegocioId = negocioId,
            Nombre = req.SedeNombre.Trim(),
            Activo = true
        }, ct);

        await _usuarios.CrearAsync(new Usuario
        {
            UsuarioLogin = adminUsuario,
            NombreCompleto = req.AdminNombreCompleto.Trim(),
            Email = adminEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.AdminPassword),
            RolId = rolAdmin.Id,
            RolCodigo = rolAdmin.Codigo,
            NegocioId = negocioId,
            SedeId = sedeId,
            Activo = true
        }, ct);

        await SembrarPermisosDefectoAsync(negocioId, ct);

        await _configuracion.ActualizarAsync(new ConfiguracionNegocio
        {
            NombreNegocio = req.Nombre.Trim(),
            ColorPrimario = "#0b57d0",
            ColorSecundario = "#29b6f6",
            ColorAcento = "#f5a623",
            Ruc = rucEmpresa,
            Igv = 18m,
            MetaMensual = 0m,
            SolesPorPunto = 1m,
            AnchoTicketMm = 80,
            MensajePieTicket = "Gracias por su preferencia.",
            CostoDelivery = 0m
        }, negocioId, ct);

        // Servicio de sistema que ancla el cargo de delivery configurable (ver 022_costo_delivery.sql).
        await _servicios.CrearAsync(new Servicio
        {
            NegocioId = negocioId,
            Nombre = "Servicio a Domicilio",
            Precio = 0,
            Unidad = "Unidad",
            Activo = true,
            EsCargoDelivery = true
        }, ct);

        var creado = (await _negocios.ListarConConteosAsync(ct)).First(n => n.Id == negocioId);
        return CreatedAtAction(nameof(Listar), creado);
    }

    [HttpPatch("{id:int}/estado")]
    public async Task<IActionResult> CambiarEstado(int id, [FromBody] CambiarEstadoNegocioRequest req, CancellationToken ct)
    {
        var actualizado = await _negocios.CambiarEstadoAsync(id, req.Activo, ct);
        if (!actualizado) return NotFound(new { mensaje = "Empresa no encontrada." });
        return NoContent();
    }

    // Mismos defaults que ya usa Lavixa en produccion (ver 021_propietario_plataforma.sql).
    // ADMIN no necesita filas: AuthController.ObtenerModulosAsync le da Modulos.Todos directo.
    private async Task SembrarPermisosDefectoAsync(int negocioId, CancellationToken ct)
    {
        var rolCoordinador = await _roles.BuscarPorCodigoAsync("COORDINADOR", ct);
        var rolTrabajador = await _roles.BuscarPorCodigoAsync("TRABAJADOR", ct);

        var coordinador = new (string Modulo, bool Puede)[]
        {
            ("INICIO", true), ("PEDIDOS", true), ("REGISTRAR", true), ("CAJA", true),
            ("CLIENTES", true), ("PROMOCIONES", false), ("REPORTES", true),
            ("INVENTARIO", true), ("AJUSTES", false)
        };
        var trabajador = new (string Modulo, bool Puede)[]
        {
            ("INICIO", true), ("PEDIDOS", true), ("REGISTRAR", true), ("CAJA", false),
            ("CLIENTES", false), ("PROMOCIONES", false), ("REPORTES", false),
            ("INVENTARIO", false), ("AJUSTES", false)
        };

        if (rolCoordinador is not null)
            foreach (var (modulo, puede) in coordinador)
                await _permisos.GuardarAsync(rolCoordinador.Id, modulo, puede, negocioId, ct);

        if (rolTrabajador is not null)
            foreach (var (modulo, puede) in trabajador)
                await _permisos.GuardarAsync(rolTrabajador.Id, modulo, puede, negocioId, ct);
    }

    private static string? NormalizarOpcional(string? valor)
    {
        var limpio = valor?.Trim();
        return string.IsNullOrWhiteSpace(limpio) ? null : limpio;
    }

    private static bool EmailValido(string? valor) => valor is null || EmailBasicoValido.IsMatch(valor);
}
