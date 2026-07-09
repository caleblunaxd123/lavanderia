using Lavanderia.Api.Domain;
using Lavanderia.Api.Infrastructure;

namespace Lavanderia.Api.Repositories;

public interface INegocioRepository
{
    Task<int> CrearAsync(Negocio n, CancellationToken ct = default);
    Task<Negocio?> ObtenerPorSlugAsync(string slug, CancellationToken ct = default);
}

public class NegocioRepository : INegocioRepository
{
    private readonly ISqlConnectionFactory _factory;
    public NegocioRepository(ISqlConnectionFactory factory) => _factory = factory;

    private static Negocio Map(Microsoft.Data.SqlClient.SqlDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")),
        Nombre = r.GetString(r.GetOrdinal("Nombre")),
        Slug = r.GetString(r.GetOrdinal("Slug")),
        RucEmpresa = r.GetNullableString("RucEmpresa"),
        TitularNombre = r.GetNullableString("TitularNombre"),
        TitularEmail = r.GetNullableString("TitularEmail"),
        Activo = r.GetBoolean(r.GetOrdinal("Activo")),
        FechaCreacion = r.GetDateTime(r.GetOrdinal("FechaCreacion"))
    };

    public async Task<int> CrearAsync(Negocio n, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO dbo.Negocio (Nombre, Slug, RucEmpresa, TitularNombre, TitularEmail, Activo)
            OUTPUT INSERTED.Id
            VALUES (@Nombre, @Slug, @RucEmpresa, @TitularNombre, @TitularEmail, @Activo);";
        cmd.AddParam("@Nombre", n.Nombre);
        cmd.AddParam("@Slug", n.Slug);
        cmd.AddParam("@RucEmpresa", n.RucEmpresa);
        cmd.AddParam("@TitularNombre", n.TitularNombre);
        cmd.AddParam("@TitularEmail", n.TitularEmail);
        cmd.AddParam("@Activo", n.Activo);
        return await cmd.ReadScalarAsync<int>(ct);
    }

    public async Task<Negocio?> ObtenerPorSlugAsync(string slug, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, Nombre, Slug, RucEmpresa, TitularNombre, TitularEmail, Activo, FechaCreacion
            FROM dbo.Negocio
            WHERE Slug = @Slug AND Activo = 1";
        cmd.AddParam("@Slug", slug);
        return await cmd.ReadFirstOrDefaultAsync(Map, ct);
    }
}

public interface ISedeRepository
{
    Task<Sede?> ObtenerPorIdAsync(int id, CancellationToken ct = default);
    Task<List<Sede>> ListarPorNegocioAsync(int negocioId, CancellationToken ct = default);
    Task<int> CrearAsync(Sede s, CancellationToken ct = default);
    Task ActualizarAsync(Sede s, int negocioId, CancellationToken ct = default);
    Task CambiarEstadoAsync(int id, bool activo, int negocioId, CancellationToken ct = default);
}

public class SedeRepository : ISedeRepository
{
    private readonly ISqlConnectionFactory _factory;
    public SedeRepository(ISqlConnectionFactory factory) => _factory = factory;

    private const string BaseSelect = @"
        SELECT Id, NegocioId, Nombre, Direccion, Telefono, Activo, FechaCreacion
        FROM dbo.Sede";

    private static Sede Map(Microsoft.Data.SqlClient.SqlDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")),
        NegocioId = r.GetInt32(r.GetOrdinal("NegocioId")),
        Nombre = r.GetString(r.GetOrdinal("Nombre")),
        Direccion = r.GetNullableString("Direccion"),
        Telefono = r.GetNullableString("Telefono"),
        Activo = r.GetBoolean(r.GetOrdinal("Activo")),
        FechaCreacion = r.GetDateTime(r.GetOrdinal("FechaCreacion"))
    };

    public async Task<Sede?> ObtenerPorIdAsync(int id, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = BaseSelect + " WHERE Id = @Id";
        cmd.AddParam("@Id", id);
        return await cmd.ReadFirstOrDefaultAsync(Map, ct);
    }

    public async Task<List<Sede>> ListarPorNegocioAsync(int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = BaseSelect + " WHERE NegocioId = @NegocioId ORDER BY Activo DESC, Nombre";
        cmd.AddParam("@NegocioId", negocioId);
        return await cmd.ReadListAsync(Map, ct);
    }

    public async Task<int> CrearAsync(Sede s, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO dbo.Sede (NegocioId, Nombre, Direccion, Telefono, Activo)
            OUTPUT INSERTED.Id
            VALUES (@NegocioId, @Nombre, @Direccion, @Telefono, @Activo);";
        cmd.AddParam("@NegocioId", s.NegocioId);
        cmd.AddParam("@Nombre", s.Nombre);
        cmd.AddParam("@Direccion", s.Direccion);
        cmd.AddParam("@Telefono", s.Telefono);
        cmd.AddParam("@Activo", s.Activo);
        return await cmd.ReadScalarAsync<int>(ct);
    }

    public async Task ActualizarAsync(Sede s, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE dbo.Sede
               SET Nombre = @Nombre, Direccion = @Direccion, Telefono = @Telefono, Activo = @Activo
             WHERE Id = @Id AND NegocioId = @NegocioId";
        cmd.AddParam("@Id", s.Id);
        cmd.AddParam("@Nombre", s.Nombre);
        cmd.AddParam("@Direccion", s.Direccion);
        cmd.AddParam("@Telefono", s.Telefono);
        cmd.AddParam("@Activo", s.Activo);
        cmd.AddParam("@NegocioId", negocioId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task CambiarEstadoAsync(int id, bool activo, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE dbo.Sede SET Activo = @Activo WHERE Id = @Id AND NegocioId = @NegocioId";
        cmd.AddParam("@Id", id);
        cmd.AddParam("@Activo", activo);
        cmd.AddParam("@NegocioId", negocioId);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
