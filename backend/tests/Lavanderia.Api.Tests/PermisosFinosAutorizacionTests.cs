using System.Security.Claims;
using Lavanderia.Api.Auth;
using Lavanderia.Api.Domain;
using Microsoft.AspNetCore.Authorization;

namespace Lavanderia.Api.Tests;

/// <summary>
/// Los permisos finos (ej. PEDIDOS_ANULAR) deben autorizar por el permiso asignado al rol, no por el nombre del rol:
/// un rol creado por el negocio (TRABAJADOR/COORDINADOR) con el permiso marcado debe poder usar la acción.
/// </summary>
public class PermisosFinosAutorizacionTests
{
    private static async Task<bool> Autoriza(string rol, string[] permisos, string requerido)
    {
        var claims = new List<Claim> { new(ClaimTypes.Role, rol) };
        claims.AddRange(permisos.Select(p => new Claim("mod", p)));
        var usuario = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        var requisito = new ModuloRequirement(requerido);
        var ctx = new AuthorizationHandlerContext(new[] { requisito }, usuario, null);
        await new ModuloAuthorizationHandler().HandleAsync(ctx);
        return ctx.HasSucceeded;
    }

    [Fact]
    public async Task RolTrabajadorConPermisoAnular_Puede()
        => Assert.True(await Autoriza("TRABAJADOR", new[] { "PEDIDOS", "PEDIDOS_ANULAR" }, "PEDIDOS_ANULAR"));

    [Fact]
    public async Task RolCoordinadorConPermisoAnular_Puede()
        => Assert.True(await Autoriza("COORDINADOR", new[] { "PEDIDOS", "PEDIDOS_ANULAR" }, "PEDIDOS_ANULAR"));

    [Fact]
    public async Task RolSinElPermisoAnular_NoPuede_AunqueTengaElModuloPedidos()
        => Assert.False(await Autoriza("TRABAJADOR", new[] { "PEDIDOS" }, "PEDIDOS_ANULAR"));

    [Fact]
    public async Task Administrador_SiemprePuede()
        => Assert.True(await Autoriza("ADMIN", Array.Empty<string>(), "PEDIDOS_ANULAR"));

    [Fact]
    public void PedidosAnular_EstaEnElCatalogoDePermisosFinos()
        => Assert.Contains("PEDIDOS_ANULAR", PermisosFinos.ClavesTodas);
}
