using Lavanderia.Api.Domain;
using Lavanderia.Api.Infrastructure;

namespace Lavanderia.Api.Repositories;

public interface IPromocionRepository
{
    Task<List<Promocion>> ListarTodasAsync(int negocioId, CancellationToken ct = default);
    Task<Promocion?> ObtenerPorIdAsync(int id, int negocioId, CancellationToken ct = default);
    Task<int> CrearAsync(Promocion p, CancellationToken ct = default);
    Task ActualizarAsync(Promocion p, int negocioId, CancellationToken ct = default);
    Task CambiarEstadoAsync(int id, bool activa, int negocioId, CancellationToken ct = default);
    Task EliminarAsync(int id, int negocioId, CancellationToken ct = default);
    Task<Promocion?> BuscarPorCodigoAsync(string codigo, int negocioId, CancellationToken ct = default);
}

public class PromocionRepository : IPromocionRepository
{
    private readonly ISqlConnectionFactory _factory;
    public PromocionRepository(ISqlConnectionFactory factory) => _factory = factory;

    private static Promocion Map(Microsoft.Data.SqlClient.SqlDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")),
        Tipo = r.GetString(r.GetOrdinal("Tipo")),
        Descripcion = r.GetString(r.GetOrdinal("Descripcion")),
        DescuentoPct = r.GetNullableDecimal("DescuentoPct"),
        DescuentoMonto = r.GetNullableDecimal("DescuentoMonto"),
        ServicioId = r.GetNullableInt("ServicioId"),
        ServicioNombre = r.GetNullableString("ServicioNombre"),
        CantidadMinima = r.GetDecimal(r.GetOrdinal("CantidadMinima")),
        FechaInicio = r.IsDBNull(r.GetOrdinal("FechaInicio")) ? null : DateOnly.FromDateTime(r.GetDateTime(r.GetOrdinal("FechaInicio"))),
        FechaFin = r.IsDBNull(r.GetOrdinal("FechaFin")) ? null : DateOnly.FromDateTime(r.GetDateTime(r.GetOrdinal("FechaFin"))),
        Activa = r.GetBoolean(r.GetOrdinal("Activa")),
        Codigo = r.GetNullableString("Codigo")
    };

    public async Task<List<Promocion>> ListarTodasAsync(int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT p.Id, p.Tipo, p.Descripcion, p.DescuentoPct, p.DescuentoMonto,
                   p.ServicioId, s.Nombre AS ServicioNombre, p.CantidadMinima,
                   p.FechaInicio, p.FechaFin, p.Activa, p.Codigo
            FROM dbo.Promocion p
            LEFT JOIN dbo.Servicio s ON s.Id = p.ServicioId
            WHERE p.NegocioId = @NegocioId
            ORDER BY p.Activa DESC, p.Id DESC";
        cmd.AddParam("@NegocioId", negocioId);
        return await cmd.ReadListAsync(Map, ct);
    }

    public async Task<Promocion?> ObtenerPorIdAsync(int id, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT p.Id, p.Tipo, p.Descripcion, p.DescuentoPct, p.DescuentoMonto,
                   p.ServicioId, s.Nombre AS ServicioNombre, p.CantidadMinima,
                   p.FechaInicio, p.FechaFin, p.Activa, p.Codigo
            FROM dbo.Promocion p
            LEFT JOIN dbo.Servicio s ON s.Id = p.ServicioId
            WHERE p.Id = @Id AND p.NegocioId = @NegocioId";
        cmd.AddParam("@Id", id);
        cmd.AddParam("@NegocioId", negocioId);
        return await cmd.ReadFirstOrDefaultAsync(Map, ct);
    }

    public async Task<int> CrearAsync(Promocion p, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO dbo.Promocion
                (NegocioId, Tipo, Descripcion, DescuentoPct, DescuentoMonto, ServicioId, CantidadMinima, FechaInicio, FechaFin, Activa, Codigo)
            OUTPUT INSERTED.Id
            VALUES (@NegocioId, @Tipo, @Descripcion, @DescuentoPct, @DescuentoMonto, @ServicioId, @CantidadMinima, @FechaInicio, @FechaFin, @Activa, @Codigo)";
        cmd.AddParam("@NegocioId", p.NegocioId);
        AddParams(cmd, p);
        return await cmd.ReadScalarAsync<int>(ct);
    }

    public async Task ActualizarAsync(Promocion p, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE dbo.Promocion
               SET Tipo = @Tipo, Descripcion = @Descripcion, DescuentoPct = @DescuentoPct,
                   DescuentoMonto = @DescuentoMonto, ServicioId = @ServicioId, CantidadMinima = @CantidadMinima,
                   FechaInicio = @FechaInicio, FechaFin = @FechaFin, Activa = @Activa, Codigo = @Codigo
             WHERE Id = @Id AND NegocioId = @NegocioId";
        cmd.AddParam("@Id", p.Id);
        cmd.AddParam("@NegocioId", negocioId);
        AddParams(cmd, p);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task CambiarEstadoAsync(int id, bool activa, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE dbo.Promocion SET Activa = @Activa WHERE Id = @Id AND NegocioId = @NegocioId";
        cmd.AddParam("@Id", id);
        cmd.AddParam("@Activa", activa);
        cmd.AddParam("@NegocioId", negocioId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // Antes borraba el registro (DELETE). Se cambia a soft-delete (mismo efecto visible: la
    // promocion deja de listarse/usarse) porque un borrado fisico pierde para siempre el
    // historico de que promociones existieron/se aplicaron. El nombre/contrato del metodo no
    // cambia para no tocar el controller ni el frontend.
    public async Task EliminarAsync(int id, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE dbo.Promocion SET Activa = 0 WHERE Id = @Id AND NegocioId = @NegocioId";
        cmd.AddParam("@Id", id);
        cmd.AddParam("@NegocioId", negocioId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static void AddParams(Microsoft.Data.SqlClient.SqlCommand cmd, Promocion p)
    {
        cmd.AddParam("@Tipo", p.Tipo);
        cmd.AddParam("@Descripcion", p.Descripcion);
        cmd.AddParam("@DescuentoPct", p.DescuentoPct);
        cmd.AddParam("@DescuentoMonto", p.DescuentoMonto);
        cmd.AddParam("@ServicioId", p.ServicioId);
        cmd.AddParam("@CantidadMinima", p.CantidadMinima);
        cmd.AddParam("@FechaInicio", p.FechaInicio.HasValue ? p.FechaInicio.Value.ToDateTime(TimeOnly.MinValue) : (object?)null);
        cmd.AddParam("@FechaFin", p.FechaFin.HasValue ? p.FechaFin.Value.ToDateTime(TimeOnly.MinValue) : (object?)null);
        cmd.AddParam("@Activa", p.Activa);
        cmd.AddParam("@Codigo", string.IsNullOrWhiteSpace(p.Codigo) ? null : p.Codigo.Trim().ToUpperInvariant());
    }

    public async Task<Promocion?> BuscarPorCodigoAsync(string codigo, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT p.Id, p.Tipo, p.Descripcion, p.DescuentoPct, p.DescuentoMonto,
                   p.ServicioId, s.Nombre AS ServicioNombre, p.CantidadMinima,
                   p.FechaInicio, p.FechaFin, p.Activa, p.Codigo
            FROM dbo.Promocion p
            LEFT JOIN dbo.Servicio s ON s.Id = p.ServicioId
            WHERE p.Codigo = @Codigo AND p.NegocioId = @NegocioId";
        cmd.AddParam("@Codigo", codigo.Trim().ToUpperInvariant());
        cmd.AddParam("@NegocioId", negocioId);
        return await cmd.ReadFirstOrDefaultAsync(Map, ct);
    }
}
