using Microsoft.AspNetCore.Authorization;

namespace Lavanderia.Api.Auth;

public static class ModuloPolicies
{
    public const string Prefix = "Modulo:";

    public static string For(string modulo) => Prefix + modulo;
}

public sealed class ModuloRequirement : IAuthorizationRequirement
{
    public ModuloRequirement(string modulo) => Modulo = modulo;

    public string Modulo { get; }
}

public sealed class ModuloAuthorizationHandler : AuthorizationHandler<ModuloRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ModuloRequirement requirement)
    {
        if (context.User.IsInRole("ADMIN"))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var modulos = context.User.FindAll("mod").Select(c => c.Value);
        if (modulos.Contains(requirement.Modulo, StringComparer.OrdinalIgnoreCase))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
