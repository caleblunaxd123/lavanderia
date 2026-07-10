using Lavanderia.Api.Domain;
using Lavanderia.Api.Dtos;
using Lavanderia.Api.Infrastructure;

namespace Lavanderia.Api.Repositories;

public interface IClienteRepository
{
    Task<List<Cliente>> BuscarAsync(string? texto, string? campo, int limite, int negocioId, CancellationToken ct = default);
    Task<Cliente?> ObtenerPorIdAsync(int id, int negocioId, CancellationToken ct = default);
    Task<Cliente?> BuscarPorCelularOrDniAsync(string valor, int negocioId, CancellationToken ct = default);
    Task<int> CrearAsync(Cliente c, CancellationToken ct = default);
    Task ActualizarAsync(Cliente c, int negocioId, CancellationToken ct = default);
    Task DesactivarAsync(int id, int negocioId, CancellationToken ct = default);
    Task<int> ContarPedidosAsync(int clienteId, int negocioId, CancellationToken ct = default);
    Task<List<ClienteFrecuenteDto>> ListarFrecuentesAsync(DateTime desde, DateTime hasta, int limite, int negocioId, CancellationToken ct = default);
    Task FusionarAsync(int origenId, int destinoId, int negocioId, CancellationToken ct = default);
    Task<List<MovimientoPuntos>> ListarMovimientosPuntosAsync(int clienteId, int negocioId, CancellationToken ct = default);
    Task AgregarMovimientoPuntosAsync(MovimientoPuntos m, int negocioId, CancellationToken ct = default);
}

public class ClienteRepository : IClienteRepository
{
    private readonly ISqlConnectionFactory _factory;
    public ClienteRepository(ISqlConnectionFactory factory) => _factory = factory;

    private const string BaseSelect = @"
        SELECT Id, NegocioId, Nombre, Celular, Dni, DocumentoFiscal, Direccion, Puntos, Activo, FechaCreacion
        FROM dbo.Cliente";

    private static Cliente Map(Microsoft.Data.SqlClient.SqlDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")),
        Nombre = r.GetString(r.GetOrdinal("Nombre")),
        Celular = r.GetNullableString("Celular"),
        Dni = r.GetNullableString("Dni"),
        DocumentoFiscal = r.GetNullableString("DocumentoFiscal"),
        Direccion = r.GetNullableString("Direccion"),
        Puntos = r.GetInt32(r.GetOrdinal("Puntos")),
        Activo = r.GetBoolean(r.GetOrdinal("Activo")),
        FechaCreacion = r.GetDateTime(r.GetOrdinal("FechaCreacion"))
    };

    public async Task<List<Cliente>> BuscarAsync(string? texto, string? campo, int limite, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();

        var whereExtra = "";
        if (!string.IsNullOrWhiteSpace(texto))
        {
            whereExtra = campo?.ToLowerInvariant() switch
            {
                "celular" => " AND Celular LIKE @Texto",
                "dni"     => " AND Dni LIKE @Texto",
                _         => " AND Nombre LIKE @Texto"
            };
            cmd.AddParam("@Texto", $"%{texto}%");
        }
        cmd.CommandText = $"SELECT TOP (@Limite) * FROM ({BaseSelect}) t WHERE Activo = 1 AND NegocioId = @NegocioId {whereExtra} ORDER BY Nombre";
        cmd.AddParam("@Limite", limite);
        cmd.AddParam("@NegocioId", negocioId);
        return await cmd.ReadListAsync(Map, ct);
    }

    public async Task<Cliente?> ObtenerPorIdAsync(int id, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = BaseSelect + " WHERE Id = @Id AND NegocioId = @NegocioId";
        cmd.AddParam("@Id", id);
        cmd.AddParam("@NegocioId", negocioId);
        return await cmd.ReadFirstOrDefaultAsync(Map, ct);
    }

    public async Task<Cliente?> BuscarPorCelularOrDniAsync(string valor, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = BaseSelect + " WHERE (Celular = @Valor OR Dni = @Valor) AND NegocioId = @NegocioId";
        cmd.AddParam("@Valor", valor);
        cmd.AddParam("@NegocioId", negocioId);
        return await cmd.ReadFirstOrDefaultAsync(Map, ct);
    }

    public async Task<int> CrearAsync(Cliente c, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO dbo.Cliente (NegocioId, Nombre, Celular, Dni, DocumentoFiscal, Direccion, Puntos)
            OUTPUT INSERTED.Id
            VALUES (@NegocioId, @Nombre, @Celular, @Dni, @DocumentoFiscal, @Direccion, @Puntos);";
        cmd.AddParam("@NegocioId", c.NegocioId);
        cmd.AddParam("@Nombre", c.Nombre);
        cmd.AddParam("@Celular", c.Celular);
        cmd.AddParam("@Dni", c.Dni);
        cmd.AddParam("@DocumentoFiscal", c.DocumentoFiscal);
        cmd.AddParam("@Direccion", c.Direccion);
        cmd.AddParam("@Puntos", c.Puntos);
        return await cmd.ReadScalarAsync<int>(ct);
    }

    public async Task ActualizarAsync(Cliente c, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE dbo.Cliente
               SET Nombre = @Nombre,
                   Celular = @Celular,
                   Dni = @Dni,
                   DocumentoFiscal = @DocumentoFiscal,
                   Direccion = @Direccion,
                   Puntos = @Puntos
             WHERE Id = @Id AND NegocioId = @NegocioId";
        cmd.AddParam("@Id", c.Id);
        cmd.AddParam("@Nombre", c.Nombre);
        cmd.AddParam("@Celular", c.Celular);
        cmd.AddParam("@Dni", c.Dni);
        cmd.AddParam("@DocumentoFiscal", c.DocumentoFiscal);
        cmd.AddParam("@Direccion", c.Direccion);
        cmd.AddParam("@Puntos", c.Puntos);
        cmd.AddParam("@NegocioId", negocioId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DesactivarAsync(int id, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE dbo.Cliente SET Activo = 0 WHERE Id = @Id AND NegocioId = @NegocioId";
        cmd.AddParam("@Id", id);
        cmd.AddParam("@NegocioId", negocioId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> ContarPedidosAsync(int clienteId, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(1)
            FROM dbo.Pedido p
            INNER JOIN dbo.Cliente c ON c.Id = p.ClienteId
            WHERE p.ClienteId = @Id AND c.NegocioId = @NegocioId";
        cmd.AddParam("@Id", clienteId);
        cmd.AddParam("@NegocioId", negocioId);
        return await cmd.ReadScalarAsync<int>(ct);
    }

    public async Task<List<ClienteFrecuenteDto>> ListarFrecuentesAsync(DateTime desde, DateTime hasta, int limite, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT TOP (@Limite) c.Id AS ClienteId, c.Nombre, c.Celular, COUNT(p.Id) AS Visitas
            FROM dbo.Pedido p
            INNER JOIN dbo.Cliente c ON c.Id = p.ClienteId
            WHERE p.FechaIngreso >= @Desde AND p.FechaIngreso < @Hasta AND p.Anulado = 0 AND c.NegocioId = @NegocioId
            GROUP BY c.Id, c.Nombre, c.Celular
            ORDER BY COUNT(p.Id) DESC, c.Nombre";
        cmd.AddParam("@Desde", desde);
        cmd.AddParam("@Hasta", hasta);
        cmd.AddParam("@Limite", limite);
        cmd.AddParam("@NegocioId", negocioId);
        return await cmd.ReadListAsync(r => new ClienteFrecuenteDto(
            r.GetInt32(r.GetOrdinal("ClienteId")),
            r.GetString(r.GetOrdinal("Nombre")),
            r.GetNullableString("Celular"),
            r.GetInt32(r.GetOrdinal("Visitas"))
        ), ct);
    }

    public async Task FusionarAsync(int origenId, int destinoId, int negocioId, CancellationToken ct = default)
    {
        if (origenId == destinoId) throw new InvalidOperationException("Elige dos clientes distintos para fusionar.");

        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var tx = (Microsoft.Data.SqlClient.SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            await using (var cmdPuntos = conn.CreateCommand())
            {
                cmdPuntos.Transaction = tx;
                cmdPuntos.CommandText = @"
                    UPDATE dbo.Cliente
                    SET Puntos = Puntos + (
                        SELECT Puntos FROM dbo.Cliente WHERE Id = @Origen AND NegocioId = @NegocioId
                    )
                    WHERE Id = @Destino AND NegocioId = @NegocioId";
                cmdPuntos.AddParam("@Origen", origenId);
                cmdPuntos.AddParam("@Destino", destinoId);
                cmdPuntos.AddParam("@NegocioId", negocioId);
                await cmdPuntos.ExecuteNonQueryAsync(ct);
            }

            await using (var cmdPedidos = conn.CreateCommand())
            {
                cmdPedidos.Transaction = tx;
                cmdPedidos.CommandText = @"
                    UPDATE p
                    SET ClienteId = @Destino
                    FROM dbo.Pedido p
                    INNER JOIN dbo.Cliente c ON c.Id = p.ClienteId
                    WHERE p.ClienteId = @Origen AND c.NegocioId = @NegocioId";
                cmdPedidos.AddParam("@Destino", destinoId);
                cmdPedidos.AddParam("@Origen", origenId);
                cmdPedidos.AddParam("@NegocioId", negocioId);
                await cmdPedidos.ExecuteNonQueryAsync(ct);
            }

            await using (var cmdMovPuntos = conn.CreateCommand())
            {
                cmdMovPuntos.Transaction = tx;
                cmdMovPuntos.CommandText = @"
                    UPDATE dbo.MovimientoPuntos
                    SET ClienteId = @Destino
                    WHERE ClienteId = @Origen AND NegocioId = @NegocioId";
                cmdMovPuntos.AddParam("@Destino", destinoId);
                cmdMovPuntos.AddParam("@Origen", origenId);
                cmdMovPuntos.AddParam("@NegocioId", negocioId);
                await cmdMovPuntos.ExecuteNonQueryAsync(ct);
            }

            await using (var cmdDesactivar = conn.CreateCommand())
            {
                cmdDesactivar.Transaction = tx;
                cmdDesactivar.CommandText = "UPDATE dbo.Cliente SET Activo = 0, Puntos = 0 WHERE Id = @Origen AND NegocioId = @NegocioId";
                cmdDesactivar.AddParam("@Origen", origenId);
                cmdDesactivar.AddParam("@NegocioId", negocioId);
                await cmdDesactivar.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<List<MovimientoPuntos>> ListarMovimientosPuntosAsync(int clienteId, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT m.Id, m.ClienteId, m.Fecha, m.Motivo, m.Puntos, m.Tipo, m.UsuarioId, u.NombreCompleto AS UsuarioNombre
            FROM dbo.MovimientoPuntos m
            INNER JOIN dbo.Cliente c ON c.Id = m.ClienteId
            LEFT JOIN dbo.Usuario u ON u.Id = m.UsuarioId
            WHERE m.ClienteId = @ClienteId AND c.NegocioId = @NegocioId
            ORDER BY m.Fecha DESC";
        cmd.AddParam("@ClienteId", clienteId);
        cmd.AddParam("@NegocioId", negocioId);
        return await cmd.ReadListAsync(r => new MovimientoPuntos
        {
            Id = r.GetInt32(r.GetOrdinal("Id")),
            ClienteId = r.GetInt32(r.GetOrdinal("ClienteId")),
            Fecha = r.GetDateTime(r.GetOrdinal("Fecha")),
            Motivo = r.GetString(r.GetOrdinal("Motivo")),
            Puntos = r.GetInt32(r.GetOrdinal("Puntos")),
            Tipo = r.GetString(r.GetOrdinal("Tipo")),
            UsuarioId = r.IsDBNull(r.GetOrdinal("UsuarioId")) ? null : r.GetInt32(r.GetOrdinal("UsuarioId")),
            UsuarioNombre = r.GetNullableString("UsuarioNombre")
        }, ct);
    }

    public async Task AgregarMovimientoPuntosAsync(MovimientoPuntos m, int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var tx = (Microsoft.Data.SqlClient.SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            await using (var cmdIns = conn.CreateCommand())
            {
                cmdIns.Transaction = tx;
                cmdIns.CommandText = @"
                    INSERT INTO dbo.MovimientoPuntos (NegocioId, ClienteId, Fecha, Motivo, Puntos, Tipo, UsuarioId)
                    SELECT @NegocioId, @ClienteId, SYSDATETIME(), @Motivo, @Puntos, @Tipo, @UsuarioId
                    WHERE EXISTS (
                        SELECT 1 FROM dbo.Cliente WHERE Id = @ClienteId AND NegocioId = @NegocioId
                    )";
                cmdIns.AddParam("@NegocioId", negocioId);
                cmdIns.AddParam("@ClienteId", m.ClienteId);
                cmdIns.AddParam("@Motivo", m.Motivo);
                cmdIns.AddParam("@Puntos", m.Puntos);
                cmdIns.AddParam("@Tipo", m.Tipo);
                cmdIns.AddParam("@UsuarioId", m.UsuarioId);
                var filas = await cmdIns.ExecuteNonQueryAsync(ct);
                if (filas == 0) throw new InvalidOperationException("El cliente no pertenece a este negocio.");
            }

            await using (var cmdUpd = conn.CreateCommand())
            {
                cmdUpd.Transaction = tx;
                var signo = m.Tipo == "RESTA" ? -1 : 1;
                cmdUpd.CommandText = "UPDATE dbo.Cliente SET Puntos = Puntos + @Delta WHERE Id = @ClienteId AND NegocioId = @NegocioId";
                cmdUpd.AddParam("@Delta", signo * m.Puntos);
                cmdUpd.AddParam("@ClienteId", m.ClienteId);
                cmdUpd.AddParam("@NegocioId", negocioId);
                await cmdUpd.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
