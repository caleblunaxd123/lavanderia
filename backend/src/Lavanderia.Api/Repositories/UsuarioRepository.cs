using Lavanderia.Api.Domain;
using Lavanderia.Api.Infrastructure;

namespace Lavanderia.Api.Repositories;

public interface IUsuarioRepository
{
    Task<Usuario?> BuscarPorUsuarioAsync(string usuario, CancellationToken ct = default);
    Task<Usuario?> BuscarPorUsuarioAsync(string usuario, int negocioId, CancellationToken ct = default);
    Task<Usuario?> BuscarPropietarioPorUsuarioAsync(string usuario, CancellationToken ct = default);
    /// <summary>Sin filtro de negocio: solo para que un usuario lea/renueve SU PROPIA sesión (Me/SeleccionarSede).</summary>
    Task<Usuario?> ObtenerPorIdAsync(int id, CancellationToken ct = default);
    /// <summary>Con filtro de negocio: para el CRUD de administración (evita IDOR entre negocios).</summary>
    Task<Usuario?> ObtenerPorIdAsync(int id, int negocioId, CancellationToken ct = default);
    Task<int> CrearAsync(Usuario u, CancellationToken ct = default);
    Task<int> ContarActivosAsync(CancellationToken ct = default);
    Task<List<Usuario>> ListarTodosAsync(int negocioId, CancellationToken ct = default);
    Task ActualizarAsync(Usuario u, int negocioId, CancellationToken ct = default);
    Task ActualizarPasswordAsync(int id, string passwordHash, int negocioId, CancellationToken ct = default);
    Task CambiarEstadoAsync(int id, bool activo, int negocioId, CancellationToken ct = default);
    Task RegistrarUltimoAccesoAsync(int id, CancellationToken ct = default);
}

public class UsuarioRepository : IUsuarioRepository
{
    private readonly ISqlConnectionFactory _factory;
    public UsuarioRepository(ISqlConnectionFactory factory) => _factory = factory;

    private const string BaseSelect = @"
        SELECT u.Id, u.Usuario, u.NombreCompleto, u.Email, u.PasswordHash,
               u.RolId, r.Codigo AS RolCodigo, u.Activo, u.NegocioId, u.SedeId, s.Nombre AS SedeNombre, u.UltimoAcceso
        FROM dbo.Usuario u
        INNER JOIN dbo.Rol r ON r.Id = u.RolId
        LEFT JOIN dbo.Sede s ON s.Id = u.SedeId";

    private static Usuario Map(Microsoft.Data.SqlClient.SqlDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")),
        UsuarioLogin = r.GetString(r.GetOrdinal("Usuario")),
        NombreCompleto = r.GetString(r.GetOrdinal("NombreCompleto")),
        Email = r.GetNullableString("Email"),
        PasswordHash = r.GetString(r.GetOrdinal("PasswordHash")),
        RolId = r.GetInt32(r.GetOrdinal("RolId")),
        RolCodigo = r.GetString(r.GetOrdinal("RolCodigo")),
        Activo = r.GetBoolean(r.GetOrdinal("Activo")),
        NegocioId = r.GetInt32(r.GetOrdinal("NegocioId")),
        SedeId = r.IsDBNull(r.GetOrdinal("SedeId")) ? null : r.GetInt32(r.GetOrdinal("SedeId")),
        SedeNombre = r.GetNullableString("SedeNombre"),
        UltimoAcceso = r.GetNullableDateTime("UltimoAcceso")
    };

    public async Task<Usuario?> BuscarPorUsuarioAsync(string usuario, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = BaseSelect + @"
            WHERE u.Usuario = @Usuario
              AND u.Activo = 1
              AND 1 = (
                  SELECT COUNT(1)
                  FROM dbo.Usuario ux
                  WHERE ux.Usuario = @Usuario AND ux.Activo = 1
              )";
        cmd.AddParam("@Usuario", usuario);
        return await cmd.ReadFirstOrDefaultAsync(Map, ct);
    }

    public async Task<Usuario?> BuscarPorUsuarioAsync(string usuario, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = BaseSelect + " WHERE u.Usuario = @Usuario AND u.NegocioId = @NegocioId";
        cmd.AddParam("@Usuario", usuario);
        cmd.AddParam("@NegocioId", negocioId);
        return await cmd.ReadFirstOrDefaultAsync(Map, ct);
    }

    public async Task<Usuario?> BuscarPropietarioPorUsuarioAsync(string usuario, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = BaseSelect + @"
            INNER JOIN dbo.Negocio n ON n.Id = u.NegocioId
            WHERE u.Usuario = @Usuario AND r.Codigo = 'PROPIETARIO' AND n.Slug = 'plataforma-interna'";
        cmd.AddParam("@Usuario", usuario);
        return await cmd.ReadFirstOrDefaultAsync(Map, ct);
    }

    public async Task<Usuario?> ObtenerPorIdAsync(int id, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = BaseSelect + " WHERE u.Id = @Id";
        cmd.AddParam("@Id", id);
        return await cmd.ReadFirstOrDefaultAsync(Map, ct);
    }

    public async Task<Usuario?> ObtenerPorIdAsync(int id, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = BaseSelect + " WHERE u.Id = @Id AND u.NegocioId = @NegocioId";
        cmd.AddParam("@Id", id);
        cmd.AddParam("@NegocioId", negocioId);
        return await cmd.ReadFirstOrDefaultAsync(Map, ct);
    }

    public async Task<int> CrearAsync(Usuario u, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO dbo.Usuario (Usuario, NombreCompleto, Email, PasswordHash, RolId, Activo, NegocioId, SedeId)
            OUTPUT INSERTED.Id
            VALUES (@Usuario, @NombreCompleto, @Email, @PasswordHash, @RolId, @Activo, @NegocioId, @SedeId);";
        cmd.AddParam("@Usuario", u.UsuarioLogin);
        cmd.AddParam("@NombreCompleto", u.NombreCompleto);
        cmd.AddParam("@Email", u.Email);
        cmd.AddParam("@PasswordHash", u.PasswordHash);
        cmd.AddParam("@RolId", u.RolId);
        cmd.AddParam("@Activo", u.Activo);
        cmd.AddParam("@NegocioId", u.NegocioId);
        cmd.AddParam("@SedeId", u.SedeId);
        return await cmd.ReadScalarAsync<int>(ct);
    }

    /// <summary>Cuenta global (todos los negocios): solo para el bootstrap de instalación nueva en DbInitializer.</summary>
    public async Task<int> ContarActivosAsync(CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM dbo.Usuario WHERE Activo = 1";
        return await cmd.ReadScalarAsync<int>(ct);
    }

    public async Task<List<Usuario>> ListarTodosAsync(int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = BaseSelect + " WHERE u.NegocioId = @NegocioId ORDER BY u.Activo DESC, u.NombreCompleto";
        cmd.AddParam("@NegocioId", negocioId);
        return await cmd.ReadListAsync(Map, ct);
    }

    public async Task ActualizarAsync(Usuario u, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE dbo.Usuario
               SET Usuario = @Usuario, NombreCompleto = @NombreCompleto, Email = @Email,
                   RolId = @RolId, Activo = @Activo, SedeId = @SedeId
             WHERE Id = @Id AND NegocioId = @NegocioId";
        cmd.AddParam("@Id", u.Id);
        cmd.AddParam("@NegocioId", negocioId);
        cmd.AddParam("@Usuario", u.UsuarioLogin);
        cmd.AddParam("@NombreCompleto", u.NombreCompleto);
        cmd.AddParam("@Email", u.Email);
        cmd.AddParam("@RolId", u.RolId);
        cmd.AddParam("@Activo", u.Activo);
        cmd.AddParam("@SedeId", u.SedeId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task ActualizarPasswordAsync(int id, string passwordHash, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE dbo.Usuario SET PasswordHash = @Hash WHERE Id = @Id AND NegocioId = @NegocioId";
        cmd.AddParam("@Id", id);
        cmd.AddParam("@NegocioId", negocioId);
        cmd.AddParam("@Hash", passwordHash);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task CambiarEstadoAsync(int id, bool activo, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE dbo.Usuario SET Activo = @Activo WHERE Id = @Id AND NegocioId = @NegocioId";
        cmd.AddParam("@Id", id);
        cmd.AddParam("@NegocioId", negocioId);
        cmd.AddParam("@Activo", activo);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task RegistrarUltimoAccesoAsync(int id, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE dbo.Usuario SET UltimoAcceso = SYSDATETIME() WHERE Id = @Id";
        cmd.AddParam("@Id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

public interface IRolRepository
{
    Task<Rol?> BuscarPorCodigoAsync(string codigo, CancellationToken ct = default);
    Task<List<Rol>> ListarTodosAsync(CancellationToken ct = default);
}

public class RolRepository : IRolRepository
{
    private readonly ISqlConnectionFactory _factory;
    public RolRepository(ISqlConnectionFactory factory) => _factory = factory;

    private static Rol Map(Microsoft.Data.SqlClient.SqlDataReader r) => new()
    {
        Id = r.GetInt32(0),
        Codigo = r.GetString(1),
        Nombre = r.GetString(2)
    };

    public async Task<Rol?> BuscarPorCodigoAsync(string codigo, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Codigo, Nombre FROM dbo.Rol WHERE Codigo = @Codigo";
        cmd.AddParam("@Codigo", codigo);
        return await cmd.ReadFirstOrDefaultAsync(Map, ct);
    }

    public async Task<List<Rol>> ListarTodosAsync(CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Codigo, Nombre FROM dbo.Rol ORDER BY Id";
        return await cmd.ReadListAsync(Map, ct);
    }
}
