using Lavanderia.Api.Auth;
using Lavanderia.Api.Infrastructure;
using Lavanderia.Api.Repositories;
using Lavanderia.Api.Services;
using Lavanderia.Api.Services.Facturacion;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Text.Json.Serialization;

QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

// ------------------------------------------------------------
// Configuracion
// ------------------------------------------------------------
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()
    ?? throw new InvalidOperationException("Configuracion Jwt faltante.");

// ------------------------------------------------------------
// Servicios
// ------------------------------------------------------------
builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

// Infra
builder.Services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();

// Repositorios (transient — sin estado)
builder.Services.AddTransient<IUsuarioRepository, UsuarioRepository>();
builder.Services.AddTransient<IRolRepository, RolRepository>();
builder.Services.AddTransient<IClienteRepository, ClienteRepository>();
builder.Services.AddTransient<IServicioRepository, ServicioRepository>();
builder.Services.AddTransient<IAreaLavadoRepository, AreaLavadoRepository>();
builder.Services.AddTransient<IPedidoRepository, PedidoRepository>();
builder.Services.AddTransient<IConfiguracionNegocioRepository, ConfiguracionNegocioRepository>();
builder.Services.AddTransient<ICajaRepository, CajaRepository>();
builder.Services.AddTransient<IPromocionRepository, PromocionRepository>();
builder.Services.AddTransient<IRolPermisoRepository, RolPermisoRepository>();
builder.Services.AddTransient<ICategoriaRepository, CategoriaRepository>();
builder.Services.AddTransient<IReporteRepository, ReporteRepository>();
builder.Services.AddTransient<IEmpleadoRepository, EmpleadoRepository>();
builder.Services.AddTransient<IPlantillaWhatsappRepository, PlantillaWhatsappRepository>();
builder.Services.AddTransient<IRolPersonalRepository, RolPersonalRepository>();
builder.Services.AddTransient<IInsumoRepository, InsumoRepository>();
builder.Services.AddTransient<INegocioRepository, NegocioRepository>();
builder.Services.AddTransient<ISedeRepository, SedeRepository>();
builder.Services.AddTransient<IFacturacionRepository, FacturacionRepository>();

// Facturacion electronica (SUNAT directo)
builder.Services.AddDataProtection();
builder.Services.AddTransient<SecretProtector>();
builder.Services.AddHttpClient<SunatSoapClient>();
builder.Services.AddTransient<IFacturacionElectronicaProvider, SunatDirectoProvider>();
builder.Services.AddTransient<ComprobantePdfGenerator>();

// Contexto de tenant (negocio/sede) leido de los claims del JWT actual
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, TenantContext>();

// Servicios de dominio
builder.Services.AddTransient<IPedidoService, PedidoService>();

// Auth
builder.Services.AddSingleton<ITokenService, TokenService>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SecretKey)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });
builder.Services.AddAuthorization();

// CORS
// En Development se permite cualquier puerto de localhost/127.0.0.1 (el dev server
// de Angular cambia de puerto seguido si 4300 ya esta ocupado). En produccion se
// restringe estrictamente a los origenes configurados en appsettings.
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(o =>
{
    o.AddDefaultPolicy(p =>
    {
        if (builder.Environment.IsDevelopment())
        {
            p.SetIsOriginAllowed(origin =>
                Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
                (uri.Host is "localhost" or "127.0.0.1"));
        }
        else
        {
            p.WithOrigins(corsOrigins);
        }
        p.AllowAnyHeader().AllowAnyMethod();
    });
});

// Seeder
builder.Services.AddTransient<DbInitializer>();

// ------------------------------------------------------------
// App
// ------------------------------------------------------------
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Seed inicial (usuario admin) — no bloquea el arranque si SQL no está disponible aún.
using (var scope = app.Services.CreateScope())
{
    try
    {
        var seeder = scope.ServiceProvider.GetRequiredService<DbInitializer>();
        await seeder.EjecutarAsync();
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex,
            "No se pudo ejecutar el seed inicial. Verifica que SQL Server este corriendo y que se hayan ejecutado los scripts 001_schema.sql y 002_seed.sql.");
    }
}

app.Run();
