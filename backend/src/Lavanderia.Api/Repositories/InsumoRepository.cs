using Lavanderia.Api.Domain;
using Lavanderia.Api.Infrastructure;
using Microsoft.Data.SqlClient;

namespace Lavanderia.Api.Repositories;

public interface IInsumoRepository
{
    Task<List<Insumo>> ListarTodosAsync(int sedeId, CancellationToken ct = default);
    Task<List<Insumo>> ListarBajoStockAsync(int sedeId, CancellationToken ct = default);
    Task<Insumo?> ObtenerPorIdAsync(int id, int sedeId, CancellationToken ct = default);
    Task<int> CrearAsync(Insumo i, CancellationToken ct = default);
    Task ActualizarAsync(Insumo i, int sedeId, CancellationToken ct = default);
    Task CambiarEstadoAsync(int id, bool activo, int sedeId, CancellationToken ct = default);
    Task<int> RegistrarMovimientoAsync(MovimientoInsumo m, string? metodoPagoParaGasto, int? tipoGastoIdParaGasto, CancellationToken ct = default);
    Task<List<MovimientoInsumo>> ListarMovimientosAsync(int? insumoId, DateTime desde, DateTime hasta, int sedeId, CancellationToken ct = default);
}

public class InsumoRepository : IInsumoRepository
{
    private readonly ISqlConnectionFactory _factory;
    public InsumoRepository(ISqlConnectionFactory factory) => _factory = factory;

    private static Insumo Map(SqlDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")),
        Nombre = r.GetString(r.GetOrdinal("Nombre")),
        UnidadMedida = r.GetString(r.GetOrdinal("UnidadMedida")),
        StockActual = r.GetDecimal(r.GetOrdinal("StockActual")),
        StockMinimo = r.GetDecimal(r.GetOrdinal("StockMinimo")),
        Activo = r.GetBoolean(r.GetOrdinal("Activo"))
    };

    private const string Select = "SELECT Id, Nombre, UnidadMedida, StockActual, StockMinimo, Activo FROM dbo.Insumo";

    public async Task<List<Insumo>> ListarTodosAsync(int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = Select + " WHERE SedeId = @SedeId ORDER BY Activo DESC, Nombre";
        cmd.AddParam("@SedeId", sedeId);
        return await cmd.ReadListAsync(Map, ct);
    }

    public async Task<List<Insumo>> ListarBajoStockAsync(int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = Select + " WHERE Activo = 1 AND StockActual <= StockMinimo AND SedeId = @SedeId ORDER BY Nombre";
        cmd.AddParam("@SedeId", sedeId);
        return await cmd.ReadListAsync(Map, ct);
    }

    public async Task<Insumo?> ObtenerPorIdAsync(int id, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = Select + " WHERE Id = @Id AND SedeId = @SedeId";
        cmd.AddParam("@Id", id);
        cmd.AddParam("@SedeId", sedeId);
        return await cmd.ReadFirstOrDefaultAsync(Map, ct);
    }

    public async Task<int> CrearAsync(Insumo i, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO dbo.Insumo (SedeId, Nombre, UnidadMedida, StockActual, StockMinimo, Activo)
            OUTPUT INSERTED.Id
            VALUES (@SedeId, @Nombre, @UnidadMedida, @StockActual, @StockMinimo, @Activo)";
        cmd.AddParam("@SedeId", i.SedeId);
        cmd.AddParam("@Nombre", i.Nombre);
        cmd.AddParam("@UnidadMedida", i.UnidadMedida);
        cmd.AddParam("@StockActual", i.StockActual);
        cmd.AddParam("@StockMinimo", i.StockMinimo);
        cmd.AddParam("@Activo", i.Activo);
        return await cmd.ReadScalarAsync<int>(ct);
    }

    public async Task ActualizarAsync(Insumo i, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE dbo.Insumo
            SET Nombre = @Nombre, UnidadMedida = @UnidadMedida, StockMinimo = @StockMinimo, Activo = @Activo
            WHERE Id = @Id AND SedeId = @SedeId";
        cmd.AddParam("@Id", i.Id);
        cmd.AddParam("@Nombre", i.Nombre);
        cmd.AddParam("@UnidadMedida", i.UnidadMedida);
        cmd.AddParam("@StockMinimo", i.StockMinimo);
        cmd.AddParam("@Activo", i.Activo);
        cmd.AddParam("@SedeId", sedeId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task CambiarEstadoAsync(int id, bool activo, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE dbo.Insumo SET Activo = @Activo WHERE Id = @Id AND SedeId = @SedeId";
        cmd.AddParam("@Id", id);
        cmd.AddParam("@Activo", activo);
        cmd.AddParam("@SedeId", sedeId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> RegistrarMovimientoAsync(MovimientoInsumo m, string? metodoPagoParaGasto, int? tipoGastoIdParaGasto, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            int? movimientoCajaId = null;

            // Si es una compra con costo y metodo de pago, tambien se registra como gasto de caja
            if (m.Tipo == "COMPRA" && m.CostoTotal is > 0 && metodoPagoParaGasto is not null)
            {
                await using var cmdGasto = conn.CreateCommand();
                cmdGasto.Transaction = tx;
                cmdGasto.CommandText = @"
                    INSERT INTO dbo.MovimientoCaja (SedeId, Fecha, Tipo, MetodoPago, Monto, Descripcion, TipoGastoId, UsuarioId)
                    OUTPUT INSERTED.Id
                    VALUES (@SedeId, @Fecha, 'GASTO', @MetodoPago, @Monto, @Descripcion, @TipoGastoId, @UsuarioId);";
                cmdGasto.AddParam("@SedeId", m.SedeId);
                cmdGasto.AddParam("@Fecha", m.Fecha);
                cmdGasto.AddParam("@MetodoPago", metodoPagoParaGasto);
                cmdGasto.AddParam("@Monto", m.CostoTotal.Value);
                cmdGasto.AddParam("@Descripcion", m.Descripcion ?? $"Compra de {m.InsumoNombre}");
                cmdGasto.AddParam("@TipoGastoId", tipoGastoIdParaGasto);
                cmdGasto.AddParam("@UsuarioId", m.UsuarioId);
                movimientoCajaId = await cmdGasto.ReadScalarAsync<int>(ct);
            }

            await using var cmdMov = conn.CreateCommand();
            cmdMov.Transaction = tx;
            cmdMov.CommandText = @"
                INSERT INTO dbo.MovimientoInsumo (SedeId, InsumoId, Tipo, Cantidad, CostoTotal, Fecha, UsuarioId, Descripcion, MovimientoCajaId)
                OUTPUT INSERTED.Id
                VALUES (@SedeId, @InsumoId, @Tipo, @Cantidad, @CostoTotal, @Fecha, @UsuarioId, @Descripcion, @MovimientoCajaId);";
            cmdMov.AddParam("@SedeId", m.SedeId);
            cmdMov.AddParam("@InsumoId", m.InsumoId);
            cmdMov.AddParam("@Tipo", m.Tipo);
            cmdMov.AddParam("@Cantidad", m.Cantidad);
            cmdMov.AddParam("@CostoTotal", m.CostoTotal);
            cmdMov.AddParam("@Fecha", m.Fecha);
            cmdMov.AddParam("@UsuarioId", m.UsuarioId);
            cmdMov.AddParam("@Descripcion", m.Descripcion);
            cmdMov.AddParam("@MovimientoCajaId", movimientoCajaId);
            var id = await cmdMov.ReadScalarAsync<int>(ct);

            var delta = m.Tipo switch
            {
                "COMPRA" => m.Cantidad,
                "CONSUMO" => -m.Cantidad,
                _ => m.Cantidad // AJUSTE: la cantidad ya viene con el signo deseado
            };

            // El tope de stock >= 0 va en el propio WHERE (chequeo atomico): evita que dos
            // consumos concurrentes, cada uno validado por separado antes de llegar aqui, dejen
            // el stock negativo (TOCTOU si solo se valida afuera de la transaccion).
            await using var cmdStock = conn.CreateCommand();
            cmdStock.Transaction = tx;
            cmdStock.CommandText = @"
                UPDATE dbo.Insumo
                   SET StockActual = StockActual + @Delta
                 WHERE Id = @InsumoId AND SedeId = @SedeId AND StockActual + @Delta >= 0";
            cmdStock.AddParam("@Delta", delta);
            cmdStock.AddParam("@InsumoId", m.InsumoId);
            cmdStock.AddParam("@SedeId", m.SedeId);
            var stockRows = await cmdStock.ExecuteNonQueryAsync(ct);
            if (stockRows == 0)
            {
                await using var cmdExiste = conn.CreateCommand();
                cmdExiste.Transaction = tx;
                cmdExiste.CommandText = "SELECT COUNT(1) FROM dbo.Insumo WHERE Id = @InsumoId AND SedeId = @SedeId";
                cmdExiste.AddParam("@InsumoId", m.InsumoId);
                cmdExiste.AddParam("@SedeId", m.SedeId);
                var existe = await cmdExiste.ReadScalarAsync<int>(ct) > 0;
                throw new InvalidOperationException(existe
                    ? "Stock insuficiente para este movimiento."
                    : "Insumo no encontrado en esta sede.");
            }

            await tx.CommitAsync(ct);
            return id;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<List<MovimientoInsumo>> ListarMovimientosAsync(int? insumoId, DateTime desde, DateTime hasta, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        var where = " WHERE m.Fecha >= @Desde AND m.Fecha < @Hasta AND m.SedeId = @SedeId ";
        if (insumoId is int id) where += " AND m.InsumoId = @InsumoId ";
        cmd.CommandText = @$"
            SELECT m.Id, m.InsumoId, i.Nombre AS InsumoNombre, m.Tipo, m.Cantidad, m.CostoTotal,
                   m.Fecha, u.NombreCompleto AS UsuarioNombre, m.Descripcion
            FROM dbo.MovimientoInsumo m
            INNER JOIN dbo.Insumo i ON i.Id = m.InsumoId
            INNER JOIN dbo.Usuario u ON u.Id = m.UsuarioId
            {where}
            ORDER BY m.Fecha DESC";
        cmd.AddParam("@Desde", desde);
        cmd.AddParam("@Hasta", hasta);
        cmd.AddParam("@SedeId", sedeId);
        if (insumoId is int idVal) cmd.AddParam("@InsumoId", idVal);
        return await cmd.ReadListAsync(r => new MovimientoInsumo
        {
            Id = r.GetInt32(r.GetOrdinal("Id")),
            InsumoId = r.GetInt32(r.GetOrdinal("InsumoId")),
            InsumoNombre = r.GetNullableString("InsumoNombre"),
            Tipo = r.GetString(r.GetOrdinal("Tipo")),
            Cantidad = r.GetDecimal(r.GetOrdinal("Cantidad")),
            CostoTotal = r.GetNullableDecimal("CostoTotal"),
            Fecha = r.GetDateTime(r.GetOrdinal("Fecha")),
            UsuarioNombre = r.GetNullableString("UsuarioNombre"),
            Descripcion = r.GetNullableString("Descripcion")
        }, ct);
    }
}
