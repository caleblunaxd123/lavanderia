using Lavanderia.Api.Domain;
using Lavanderia.Api.Infrastructure;
using Microsoft.Data.SqlClient;

namespace Lavanderia.Api.Repositories;

public interface IPagosRepository
{
    Task<ConfiguracionPagos?> ObtenerConfigAsync(int negocioId, CancellationToken ct = default);
    Task GuardarConfigAsync(ConfiguracionPagos c, CancellationToken ct = default);

    Task<SolicitudPago> CrearSolicitudAsync(int negocioId, int sedeId, int pedidoId, decimal monto, CancellationToken ct = default);
    Task<SolicitudPago?> ObtenerVigentePorPedidoAsync(int pedidoId, CancellationToken ct = default);
    Task<SolicitudPago?> ObtenerPorTokenAsync(Guid token, CancellationToken ct = default);
    /// <summary>Marca la solicitud como pagada solo si seguia PENDIENTE (idempotente ante
    /// reintentos del cliente o dobles llamadas). Devuelve false si ya estaba resuelta.</summary>
    Task<bool> MarcarPagadoAsync(int id, string culqiChargeId, CancellationToken ct = default);
}

public class PagosRepository : IPagosRepository
{
    private readonly ISqlConnectionFactory _factory;
    public PagosRepository(ISqlConnectionFactory factory) => _factory = factory;

    private const string ConfigSelect = @"
        SELECT Id, NegocioId, Proveedor, PublicKey, SecretKeyCifrada, Activo
        FROM dbo.ConfiguracionPagos";

    private static ConfiguracionPagos MapConfig(SqlDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")),
        NegocioId = r.GetInt32(r.GetOrdinal("NegocioId")),
        Proveedor = r.GetString(r.GetOrdinal("Proveedor")),
        PublicKey = r.GetNullableString("PublicKey"),
        SecretKeyCifrada = r.GetNullableString("SecretKeyCifrada"),
        Activo = r.GetBoolean(r.GetOrdinal("Activo"))
    };

    public async Task<ConfiguracionPagos?> ObtenerConfigAsync(int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = ConfigSelect + " WHERE NegocioId = @NegocioId";
        cmd.AddParam("@NegocioId", negocioId);
        return await cmd.ReadFirstOrDefaultAsync(MapConfig, ct);
    }

    public async Task GuardarConfigAsync(ConfiguracionPagos c, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        // Upsert: el negocio puede no tener fila propia todavia (primera vez que configura).
        cmd.CommandText = @"
            MERGE dbo.ConfiguracionPagos AS target
            USING (SELECT @NegocioId AS NegocioId) AS src
                ON target.NegocioId = src.NegocioId
            WHEN MATCHED THEN UPDATE SET
                Proveedor = @Proveedor,
                PublicKey = @PublicKey,
                SecretKeyCifrada = COALESCE(@SecretKeyCifrada, target.SecretKeyCifrada),
                Activo = @Activo,
                FechaActualizacion = SYSDATETIME()
            WHEN NOT MATCHED THEN INSERT
                (NegocioId, Proveedor, PublicKey, SecretKeyCifrada, Activo)
                VALUES
                (@NegocioId, @Proveedor, @PublicKey, @SecretKeyCifrada, @Activo);";
        cmd.AddParam("@NegocioId", c.NegocioId);
        cmd.AddParam("@Proveedor", c.Proveedor);
        cmd.AddParam("@PublicKey", c.PublicKey);
        cmd.AddParam("@SecretKeyCifrada", c.SecretKeyCifrada);
        cmd.AddParam("@Activo", c.Activo);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private const string SolicitudSelect = @"
        SELECT Id, NegocioId, SedeId, PedidoId, Token, Monto, Estado, CulqiChargeId,
               FechaCreacion, FechaExpiracion, FechaPago
        FROM dbo.SolicitudPago";

    private static SolicitudPago MapSolicitud(SqlDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")),
        NegocioId = r.GetInt32(r.GetOrdinal("NegocioId")),
        SedeId = r.GetInt32(r.GetOrdinal("SedeId")),
        PedidoId = r.GetInt32(r.GetOrdinal("PedidoId")),
        Token = r.GetGuid(r.GetOrdinal("Token")),
        Monto = r.GetDecimal(r.GetOrdinal("Monto")),
        Estado = r.GetString(r.GetOrdinal("Estado")),
        CulqiChargeId = r.GetNullableString("CulqiChargeId"),
        FechaCreacion = r.GetDateTime(r.GetOrdinal("FechaCreacion")),
        FechaExpiracion = r.GetDateTime(r.GetOrdinal("FechaExpiracion")),
        FechaPago = r.GetNullableDateTime("FechaPago")
    };

    public async Task<SolicitudPago> CrearSolicitudAsync(int negocioId, int sedeId, int pedidoId, decimal monto, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO dbo.SolicitudPago (NegocioId, SedeId, PedidoId, Token, Monto, Estado, FechaExpiracion)
            OUTPUT INSERTED.Id, INSERTED.Token, INSERTED.FechaCreacion, INSERTED.FechaExpiracion
            VALUES (@NegocioId, @SedeId, @PedidoId, NEWID(), @Monto, 'PENDIENTE', DATEADD(DAY, 30, SYSDATETIME()));";
        cmd.AddParam("@NegocioId", negocioId);
        cmd.AddParam("@SedeId", sedeId);
        cmd.AddParam("@PedidoId", pedidoId);
        cmd.AddParam("@Monto", monto);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return new SolicitudPago
        {
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            NegocioId = negocioId,
            SedeId = sedeId,
            PedidoId = pedidoId,
            Token = reader.GetGuid(reader.GetOrdinal("Token")),
            Monto = monto,
            Estado = "PENDIENTE",
            FechaCreacion = reader.GetDateTime(reader.GetOrdinal("FechaCreacion")),
            FechaExpiracion = reader.GetDateTime(reader.GetOrdinal("FechaExpiracion"))
        };
    }

    public async Task<SolicitudPago?> ObtenerVigentePorPedidoAsync(int pedidoId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = SolicitudSelect + @"
            WHERE PedidoId = @PedidoId AND Estado = 'PENDIENTE' AND FechaExpiracion > SYSDATETIME()
            ORDER BY FechaCreacion DESC";
        cmd.AddParam("@PedidoId", pedidoId);
        return await cmd.ReadFirstOrDefaultAsync(MapSolicitud, ct);
    }

    public async Task<SolicitudPago?> ObtenerPorTokenAsync(Guid token, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = SolicitudSelect + " WHERE Token = @Token";
        cmd.AddParam("@Token", token);
        return await cmd.ReadFirstOrDefaultAsync(MapSolicitud, ct);
    }

    public async Task<bool> MarcarPagadoAsync(int id, string culqiChargeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE dbo.SolicitudPago
               SET Estado = 'PAGADO', CulqiChargeId = @ChargeId, FechaPago = SYSDATETIME()
             WHERE Id = @Id AND Estado = 'PENDIENTE'";
        cmd.AddParam("@Id", id);
        cmd.AddParam("@ChargeId", culqiChargeId);
        var filas = await cmd.ExecuteNonQueryAsync(ct);
        return filas > 0;
    }
}
