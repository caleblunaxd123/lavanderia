using Lavanderia.Api.Domain;
using Lavanderia.Api.Infrastructure;
using Microsoft.Data.SqlClient;
using System.Data;

namespace Lavanderia.Api.Repositories;

public interface IPedidoRepository
{
    Task<int> CrearAsync(Pedido pedido, CancellationToken ct = default);
    Task<Pedido?> ObtenerPorIdAsync(int id, int sedeId, CancellationToken ct = default);
    Task<(List<Pedido> Items, int Total)> ListarPaginadoAsync(string? filtro, string? busqueda, DateTime? desde, DateTime? hasta, string? campoFecha, int pagina, int tamanoPagina, int sedeId, CancellationToken ct = default);
    Task<(List<Pedido> Items, int Total)> ListarPorClienteAsync(int clienteId, string? filtro, int pagina, int tamanoPagina, int sedeId, CancellationToken ct = default);
    Task<int> SiguienteNumeroAsync(int sedeId, CancellationToken ct = default);
    Task RegistrarHistorialAsync(PedidoHistorial h, SqlConnection conn, SqlTransaction tx, CancellationToken ct = default);
    Task AvanzarAreaAsync(int pedidoId, int? nuevaAreaId, string nuevoEstado, int usuarioId, string? nota, int sedeId, CancellationToken ct = default);
    Task<List<PedidoHistorial>> ObtenerHistorialAsync(int pedidoId, int sedeId, CancellationToken ct = default);
    Task<Dictionary<string, int>> ContadoresPorEstadoAsync(int sedeId, CancellationToken ct = default);
    Task<Dictionary<int, int>> ConteoPorAreaAsync(int sedeId, CancellationToken ct = default);
    Task<decimal> VentasDelDiaAsync(DateTime fecha, int sedeId, CancellationToken ct = default);
    Task<int> PedidosDelMesAsync(DateTime fecha, int sedeId, CancellationToken ct = default);
    Task RegistrarPagoAsync(int pedidoId, decimal monto, string metodo, int usuarioId, string? descripcion, int sedeId, CancellationToken ct = default);
    Task AgregarItemAsync(int pedidoId, PedidoItem item, int sedeId, CancellationToken ct = default);
    Task AnularAsync(int pedidoId, int usuarioId, string motivo, int sedeId, CancellationToken ct = default);
    Task ActualizarFechaEntregaAsync(int pedidoId, DateTime nuevaFecha, int usuarioId, string? motivo, int sedeId, CancellationToken ct = default);
    Task<List<PedidoAbandonado>> ListarListosAbandonadosAsync(int diasMinimo, int sedeId, CancellationToken ct = default);
    Task<bool> CambiarModalidadAsync(int pedidoId, string modalidad, int sedeId, CancellationToken ct = default);
}

public class PedidoRepository : IPedidoRepository
{
    private readonly ISqlConnectionFactory _factory;
    public PedidoRepository(ISqlConnectionFactory factory) => _factory = factory;

    public async Task<int> SiguienteNumeroAsync(int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT ISNULL(MAX(Numero), 0) + 1 FROM dbo.Pedido WHERE SedeId = @SedeId";
        cmd.AddParam("@SedeId", sedeId);
        return await cmd.ReadScalarAsync<int>(ct);
    }

    public async Task<int> CrearAsync(Pedido p, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);

        try
        {
            // Insert Pedido
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT INTO dbo.Pedido (
                    SedeId, Numero, ClienteId, UsuarioId, FechaIngreso, FechaEntregaEst, Modalidad,
                    Subtotal, Descuento, EsUrgente, RecargoUrgente, Redondeo, Total, MontoPagado, EstadoPago, EstadoProceso,
                    AreaActualId, Observaciones, CodigoAntiguo
                )
                OUTPUT INSERTED.Id
                VALUES (
                    @SedeId, @Numero, @ClienteId, @UsuarioId, @FechaIngreso, @FechaEntregaEst, @Modalidad,
                    @Subtotal, @Descuento, @EsUrgente, @RecargoUrgente, @Redondeo, @Total, @MontoPagado, @EstadoPago, @EstadoProceso,
                    @AreaActualId, @Observaciones, @CodigoAntiguo
                );";
            cmd.AddParam("@SedeId", p.SedeId);
            cmd.AddParam("@Numero", p.Numero);
            cmd.AddParam("@ClienteId", p.ClienteId);
            cmd.AddParam("@UsuarioId", p.UsuarioId);
            cmd.AddParam("@FechaIngreso", p.FechaIngreso);
            cmd.AddParam("@FechaEntregaEst", p.FechaEntregaEst);
            cmd.AddParam("@Modalidad", p.Modalidad);
            cmd.AddParam("@Subtotal", p.Subtotal);
            cmd.AddParam("@Descuento", p.Descuento);
            cmd.AddParam("@EsUrgente", p.EsUrgente);
            cmd.AddParam("@RecargoUrgente", p.RecargoUrgente);
            cmd.AddParam("@Redondeo", p.Redondeo);
            cmd.AddParam("@Total", p.Total);
            cmd.AddParam("@MontoPagado", p.MontoPagado);
            cmd.AddParam("@EstadoPago", p.EstadoPago);
            cmd.AddParam("@EstadoProceso", p.EstadoProceso);
            cmd.AddParam("@AreaActualId", p.AreaActualId);
            cmd.AddParam("@Observaciones", p.Observaciones);
            cmd.AddParam("@CodigoAntiguo", p.CodigoAntiguo);

            var pedidoId = await cmd.ReadScalarAsync<int>(ct);
            p.Id = pedidoId;

            // Insert items
            foreach (var it in p.Items)
            {
                await using var cmdItem = conn.CreateCommand();
                cmdItem.Transaction = tx;
                cmdItem.CommandText = @"
                    INSERT INTO dbo.PedidoItem (PedidoId, ServicioId, Cantidad, PrecioUnit, Total, Descripcion)
                    VALUES (@PedidoId, @ServicioId, @Cantidad, @PrecioUnit, @Total, @Descripcion);";
                cmdItem.AddParam("@PedidoId", pedidoId);
                cmdItem.AddParam("@ServicioId", it.ServicioId);
                cmdItem.AddParam("@Cantidad", it.Cantidad);
                cmdItem.AddParam("@PrecioUnit", it.PrecioUnit);
                cmdItem.AddParam("@Total", it.Total);
                cmdItem.AddParam("@Descripcion", it.Descripcion);
                await cmdItem.ExecuteNonQueryAsync(ct);
            }

            // Historial inicial
            await RegistrarHistorialAsync(new PedidoHistorial
            {
                PedidoId = pedidoId,
                AreaId = p.AreaActualId,
                EstadoProceso = p.EstadoProceso,
                UsuarioId = p.UsuarioId,
                Fecha = DateTime.Now,
                Nota = "Ingreso de pedido"
            }, conn, tx, ct);

            // Movimiento de caja por el pago inicial (si el cliente pagó algo al registrar el pedido)
            if (p.MontoPagado > 0)
            {
                await using var cmdMov = conn.CreateCommand();
                cmdMov.Transaction = tx;
                cmdMov.CommandText = @"
                    INSERT INTO dbo.MovimientoCaja
                           (SedeId, Fecha, Tipo, MetodoPago, Monto, Descripcion, PedidoId, UsuarioId)
                    VALUES (@SedeId, SYSDATETIME(), 'INGRESO', @Metodo, @Monto, @Descripcion, @PedidoId, @UsuarioId)";
                cmdMov.AddParam("@SedeId", p.SedeId);
                cmdMov.AddParam("@Metodo", p.MetodoPagoInicial);
                cmdMov.AddParam("@Monto", p.MontoPagado);
                cmdMov.AddParam("@Descripcion", $"Pago inicial de pedido #{p.Numero}");
                cmdMov.AddParam("@PedidoId", pedidoId);
                cmdMov.AddParam("@UsuarioId", p.UsuarioId);
                await cmdMov.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
            return pedidoId;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task RegistrarHistorialAsync(PedidoHistorial h, SqlConnection conn, SqlTransaction tx, CancellationToken ct = default)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO dbo.PedidoHistorial (PedidoId, AreaId, EstadoProceso, UsuarioId, Fecha, Nota, NotificadoWsp)
            VALUES (@PedidoId, @AreaId, @EstadoProceso, @UsuarioId, @Fecha, @Nota, @NotificadoWsp);";
        cmd.AddParam("@PedidoId", h.PedidoId);
        cmd.AddParam("@AreaId", h.AreaId);
        cmd.AddParam("@EstadoProceso", h.EstadoProceso);
        cmd.AddParam("@UsuarioId", h.UsuarioId);
        cmd.AddParam("@Fecha", h.Fecha);
        cmd.AddParam("@Nota", h.Nota);
        cmd.AddParam("@NotificadoWsp", h.NotificadoWsp);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<Pedido?> ObtenerPorIdAsync(int id, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT p.Id, p.SedeId, p.Numero, p.ClienteId, c.Nombre AS ClienteNombre, c.Celular AS ClienteCelular, c.Dni AS ClienteDni,
                   p.UsuarioId, u.NombreCompleto AS UsuarioNombre, p.FechaIngreso, p.FechaEntregaEst, p.Modalidad,
                   p.Subtotal, p.Descuento, p.EsUrgente, p.RecargoUrgente, p.Redondeo, p.Total, p.MontoPagado, p.EstadoPago, p.EstadoProceso,
                   p.AreaActualId, a.Nombre AS AreaActualNombre,
                   p.Observaciones, p.FechaEntregaReal, p.Anulado, p.MotivoAnulacion, p.CodigoAntiguo
            FROM dbo.Pedido p
            INNER JOIN dbo.Cliente c ON c.Id = p.ClienteId
            LEFT JOIN dbo.AreaLavado a ON a.Id = p.AreaActualId
            LEFT JOIN dbo.Usuario u ON u.Id = p.UsuarioId
            WHERE p.Id = @Id AND p.SedeId = @SedeId";
        cmd.AddParam("@Id", id);
        cmd.AddParam("@SedeId", sedeId);

        var pedido = await cmd.ReadFirstOrDefaultAsync(MapPedido, ct);
        if (pedido == null) return null;

        await using var cmdItems = conn.CreateCommand();
        cmdItems.CommandText = @"
            SELECT i.Id, i.PedidoId, i.ServicioId, s.Nombre AS ServicioNombre,
                   i.Cantidad, i.PrecioUnit, i.Total, i.Descripcion
            FROM dbo.PedidoItem i
            INNER JOIN dbo.Servicio s ON s.Id = i.ServicioId
            WHERE i.PedidoId = @PedidoId";
        cmdItems.AddParam("@PedidoId", id);
        pedido.Items = await cmdItems.ReadListAsync(MapItem, ct);

        return pedido;
    }

    public async Task<(List<Pedido> Items, int Total)> ListarPaginadoAsync(
        string? filtro, string? busqueda, DateTime? desde, DateTime? hasta, string? campoFecha, int pagina, int tamanoPagina, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();

        string where;
        if (!string.IsNullOrWhiteSpace(busqueda))
        {
            where = @" WHERE p.SedeId = @SedeId AND (
                CAST(p.Numero AS NVARCHAR(20)) = @Busqueda
                OR c.Celular LIKE @BusquedaLike
                OR c.Nombre LIKE @BusquedaLike
                OR c.Dni LIKE @BusquedaLike
                OR p.CodigoAntiguo LIKE @BusquedaLike
            ) ";
            cmd.AddParam("@Busqueda", busqueda.Trim());
            cmd.AddParam("@BusquedaLike", $"%{busqueda.Trim()}%");
        }
        else
        {
            where = filtro?.ToLowerInvariant() switch
            {
                "pendientes" => " WHERE p.SedeId = @SedeId AND p.EstadoProceso IN ('PENDIENTE','EN_PROCESO','LISTO') AND p.Anulado = 0 ",
                "listos"     => " WHERE p.SedeId = @SedeId AND p.EstadoProceso = 'LISTO' AND p.Anulado = 0 ",
                "entregados" => " WHERE p.SedeId = @SedeId AND p.EstadoProceso = 'ENTREGADO' ",
                // "Otros": entregados + anulados + donados (todo lo que no es un pedido activo)
                "otros"      => " WHERE p.SedeId = @SedeId AND (p.EstadoProceso = 'ENTREGADO' OR p.Anulado = 1) ",
                // "Últimos": los 500 más recientes sin filtro (limitado por paginación)
                "ultimos"    => " WHERE p.SedeId = @SedeId ",
                _            => " WHERE p.SedeId = @SedeId AND p.Anulado = 0 "
            };

            if (string.Equals(filtro, "fecha", StringComparison.OrdinalIgnoreCase))
            {
                var desdeFiltro = (desde ?? DateTime.Today.AddDays(-30)).Date;
                var hastaExclusivo = (hasta ?? DateTime.Today).Date.AddDays(1);
                var columnaFecha = campoFecha == "entrega" ? "p.FechaEntregaEst" : "p.FechaIngreso";
                where = $@" WHERE p.SedeId = @SedeId
                    AND p.Anulado = 0
                    AND {columnaFecha} IS NOT NULL
                    AND {columnaFecha} >= @DesdeFecha
                    AND {columnaFecha} < @HastaFecha ";
                cmd.AddParam("@DesdeFecha", desdeFiltro);
                cmd.AddParam("@HastaFecha", hastaExclusivo);
            }
        }
        cmd.AddParam("@SedeId", sedeId);

        cmd.CommandText = @$"
            SELECT
                p.Id, p.Numero, p.ClienteId, c.Nombre AS ClienteNombre, c.Celular AS ClienteCelular, c.Dni AS ClienteDni,
                p.UsuarioId, u.NombreCompleto AS UsuarioNombre, p.FechaIngreso, p.FechaEntregaEst, p.Modalidad,
                p.Subtotal, p.Descuento, p.EsUrgente, p.RecargoUrgente, p.Redondeo, p.Total, p.MontoPagado, p.EstadoPago, p.EstadoProceso,
                p.AreaActualId, a.Nombre AS AreaActualNombre,
                p.Observaciones, p.FechaEntregaReal, p.Anulado, p.MotivoAnulacion, p.CodigoAntiguo,
                COUNT(*) OVER() AS TotalRegistros
            FROM dbo.Pedido p
            INNER JOIN dbo.Cliente c ON c.Id = p.ClienteId
            LEFT JOIN dbo.AreaLavado a ON a.Id = p.AreaActualId
            LEFT JOIN dbo.Usuario u ON u.Id = p.UsuarioId
            {where}
            ORDER BY p.EsUrgente DESC, p.FechaIngreso DESC
            OFFSET @Salto ROWS FETCH NEXT @Tamano ROWS ONLY";
        cmd.AddParam("@Salto", (pagina - 1) * tamanoPagina);
        cmd.AddParam("@Tamano", tamanoPagina);

        var total = 0;
        var items = await cmd.ReadListAsync(r =>
        {
            total = r.GetInt32(r.GetOrdinal("TotalRegistros"));
            return MapPedido(r);
        }, ct);

        return (items, total);
    }

    public async Task<(List<Pedido> Items, int Total)> ListarPorClienteAsync(
        int clienteId, string? filtro, int pagina, int tamanoPagina, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();

        var where = filtro?.ToLowerInvariant() switch
        {
            "pendientes" => " WHERE p.ClienteId = @ClienteId AND p.SedeId = @SedeId AND p.EstadoProceso IN ('PENDIENTE','EN_PROCESO','LISTO') AND p.Anulado = 0 ",
            "en-proceso" => " WHERE p.ClienteId = @ClienteId AND p.SedeId = @SedeId AND p.EstadoProceso IN ('PENDIENTE','EN_PROCESO','LISTO') AND p.Anulado = 0 ",
            "con-deuda"  => " WHERE p.ClienteId = @ClienteId AND p.SedeId = @SedeId AND p.Anulado = 0 AND p.MontoPagado + 0.01 < p.Total ",
            "entregados" => " WHERE p.ClienteId = @ClienteId AND p.SedeId = @SedeId AND p.EstadoProceso = 'ENTREGADO' ",
            _            => " WHERE p.ClienteId = @ClienteId AND p.SedeId = @SedeId "
        };
        cmd.AddParam("@ClienteId", clienteId);
        cmd.AddParam("@SedeId", sedeId);

        cmd.CommandText = @$"
            SELECT
                p.Id, p.Numero, p.ClienteId, c.Nombre AS ClienteNombre, c.Celular AS ClienteCelular, c.Dni AS ClienteDni,
                p.UsuarioId, u.NombreCompleto AS UsuarioNombre, p.FechaIngreso, p.FechaEntregaEst, p.Modalidad,
                p.Subtotal, p.Descuento, p.EsUrgente, p.RecargoUrgente, p.Redondeo, p.Total, p.MontoPagado, p.EstadoPago, p.EstadoProceso,
                p.AreaActualId, a.Nombre AS AreaActualNombre,
                p.Observaciones, p.FechaEntregaReal, p.Anulado, p.MotivoAnulacion, p.CodigoAntiguo,
                COUNT(*) OVER() AS TotalRegistros
            FROM dbo.Pedido p
            INNER JOIN dbo.Cliente c ON c.Id = p.ClienteId
            LEFT JOIN dbo.AreaLavado a ON a.Id = p.AreaActualId
            LEFT JOIN dbo.Usuario u ON u.Id = p.UsuarioId
            {where}
            ORDER BY p.FechaIngreso DESC
            OFFSET @Salto ROWS FETCH NEXT @Tamano ROWS ONLY";
        cmd.AddParam("@Salto", (pagina - 1) * tamanoPagina);
        cmd.AddParam("@Tamano", tamanoPagina);

        var total = 0;
        var items = await cmd.ReadListAsync(r =>
        {
            total = r.GetInt32(r.GetOrdinal("TotalRegistros"));
            return MapPedido(r);
        }, ct);

        return (items, total);
    }

    public async Task<List<PedidoAbandonado>> ListarListosAbandonadosAsync(int diasMinimo, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            ;WITH UltimoListo AS (
                SELECT PedidoId, MAX(Fecha) AS FechaListo
                FROM dbo.PedidoHistorial
                WHERE EstadoProceso = 'LISTO'
                GROUP BY PedidoId
            )
            SELECT p.Id AS PedidoId, p.Numero, c.Nombre AS ClienteNombre, c.Celular AS ClienteCelular,
                   p.Total, p.MontoPagado, ul.FechaListo
            FROM dbo.Pedido p
            INNER JOIN dbo.Cliente c ON c.Id = p.ClienteId
            INNER JOIN UltimoListo ul ON ul.PedidoId = p.Id
            WHERE p.EstadoProceso = 'LISTO' AND p.Anulado = 0 AND p.SedeId = @SedeId
              AND ul.FechaListo <= DATEADD(day, -@Dias, SYSDATETIME())
            ORDER BY ul.FechaListo ASC";
        cmd.AddParam("@Dias", diasMinimo);
        cmd.AddParam("@SedeId", sedeId);

        return await cmd.ReadListAsync(r =>
        {
            var fechaListo = r.GetDateTime(r.GetOrdinal("FechaListo"));
            return new PedidoAbandonado
            {
                PedidoId = r.GetInt32(r.GetOrdinal("PedidoId")),
                Numero = r.GetInt32(r.GetOrdinal("Numero")),
                ClienteNombre = r.GetString(r.GetOrdinal("ClienteNombre")),
                ClienteCelular = r.GetNullableString("ClienteCelular"),
                Total = r.GetDecimal(r.GetOrdinal("Total")),
                MontoPagado = r.GetDecimal(r.GetOrdinal("MontoPagado")),
                FechaListo = fechaListo,
                DiasEsperando = (int)(DateTime.Now - fechaListo).TotalDays
            };
        }, ct);
    }

    public async Task AvanzarAreaAsync(int pedidoId, int? nuevaAreaId, string nuevoEstado, int usuarioId, string? nota, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            // 1. Leer estado actual para detectar no-op y prevenir duplicados en el historial
            await using var cmdActual = conn.CreateCommand();
            cmdActual.Transaction = tx;
            cmdActual.CommandText = "SELECT AreaActualId, EstadoProceso FROM dbo.Pedido WHERE Id = @Id AND SedeId = @SedeId";
            cmdActual.AddParam("@Id", pedidoId);
            cmdActual.AddParam("@SedeId", sedeId);

            int? areaActualDb = null;
            string? estadoActualDb = null;
            await using (var r = await cmdActual.ExecuteReaderAsync(ct))
            {
                if (await r.ReadAsync(ct))
                {
                    areaActualDb = r.IsDBNull(0) ? null : r.GetInt32(0);
                    estadoActualDb = r.GetString(1);
                }
            }

            if (estadoActualDb is null)
            {
                await tx.RollbackAsync(ct);
                throw new InvalidOperationException("Pedido no encontrado.");
            }

            // 2. Si no hay cambio real, no escribimos nada (evita historial duplicado)
            var mismaArea = areaActualDb == nuevaAreaId;
            var mismoEstado = estadoActualDb == nuevoEstado;
            if (mismaArea && mismoEstado)
            {
                await tx.RollbackAsync(ct);
                throw new InvalidOperationException("El pedido ya está en ese estado. No hay nada que actualizar.");
            }

            // 3. Actualizar
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                UPDATE dbo.Pedido
                   SET AreaActualId = @AreaId,
                       EstadoProceso = @Estado,
                       FechaEntregaReal = CASE WHEN @Estado = 'ENTREGADO' THEN SYSDATETIME() ELSE FechaEntregaReal END
                 WHERE Id = @Id AND SedeId = @SedeId";
            cmd.AddParam("@AreaId", nuevaAreaId);
            cmd.AddParam("@Estado", nuevoEstado);
            cmd.AddParam("@Id", pedidoId);
            cmd.AddParam("@SedeId", sedeId);
            await cmd.ExecuteNonQueryAsync(ct);

            await RegistrarHistorialAsync(new PedidoHistorial
            {
                PedidoId = pedidoId,
                AreaId = nuevaAreaId,
                EstadoProceso = nuevoEstado,
                UsuarioId = usuarioId,
                Fecha = DateTime.Now,
                Nota = nota
            }, conn, tx, ct);

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<List<PedidoHistorial>> ObtenerHistorialAsync(int pedidoId, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT h.Id, h.PedidoId, h.AreaId, a.Nombre AS AreaNombre,
                   h.EstadoProceso, h.UsuarioId, h.Fecha, h.Nota, h.NotificadoWsp
            FROM dbo.PedidoHistorial h
            INNER JOIN dbo.Pedido p ON p.Id = h.PedidoId
            LEFT JOIN dbo.AreaLavado a ON a.Id = h.AreaId
            WHERE h.PedidoId = @PedidoId AND p.SedeId = @SedeId
            ORDER BY h.Fecha ASC";
        cmd.AddParam("@PedidoId", pedidoId);
        cmd.AddParam("@SedeId", sedeId);
        return await cmd.ReadListAsync(r => new PedidoHistorial
        {
            Id = r.GetInt32(r.GetOrdinal("Id")),
            PedidoId = r.GetInt32(r.GetOrdinal("PedidoId")),
            AreaId = r.GetNullableInt("AreaId"),
            AreaNombre = r.GetNullableString("AreaNombre"),
            EstadoProceso = r.GetString(r.GetOrdinal("EstadoProceso")),
            UsuarioId = r.GetNullableInt("UsuarioId"),
            Fecha = r.GetDateTime(r.GetOrdinal("Fecha")),
            Nota = r.GetNullableString("Nota"),
            NotificadoWsp = r.GetBoolean(r.GetOrdinal("NotificadoWsp"))
        }, ct);
    }

    public async Task<Dictionary<string, int>> ContadoresPorEstadoAsync(int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT EstadoProceso, COUNT(1) AS Total
            FROM dbo.Pedido
            WHERE Anulado = 0 AND SedeId = @SedeId
            GROUP BY EstadoProceso";
        cmd.AddParam("@SedeId", sedeId);
        var dict = new Dictionary<string, int>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            dict[reader.GetString(0)] = reader.GetInt32(1);
        return dict;
    }

    public async Task<Dictionary<int, int>> ConteoPorAreaAsync(int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT AreaActualId, COUNT(1) AS Total
            FROM dbo.Pedido
            WHERE Anulado = 0
              AND EstadoProceso IN ('PENDIENTE','EN_PROCESO')
              AND AreaActualId IS NOT NULL
              AND SedeId = @SedeId
            GROUP BY AreaActualId";
        cmd.AddParam("@SedeId", sedeId);
        var dict = new Dictionary<int, int>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            dict[reader.GetInt32(0)] = reader.GetInt32(1);
        return dict;
    }

    public async Task<decimal> VentasDelDiaAsync(DateTime fecha, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT ISNULL(SUM(Total), 0)
            FROM dbo.Pedido
            WHERE Anulado = 0 AND CAST(FechaIngreso AS DATE) = CAST(@Fecha AS DATE) AND SedeId = @SedeId";
        cmd.AddParam("@Fecha", fecha.Date);
        cmd.AddParam("@SedeId", sedeId);
        return await cmd.ReadScalarAsync<decimal>(ct);
    }

    public async Task<int> PedidosDelMesAsync(DateTime fecha, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(*)
            FROM dbo.Pedido
            WHERE Anulado = 0
              AND YEAR(FechaIngreso) = YEAR(@Fecha)
              AND MONTH(FechaIngreso) = MONTH(@Fecha)
              AND SedeId = @SedeId";
        cmd.AddParam("@Fecha", fecha.Date);
        cmd.AddParam("@SedeId", sedeId);
        return await cmd.ReadScalarAsync<int>(ct);
    }

    public async Task RegistrarPagoAsync(int pedidoId, decimal monto, string metodo, int usuarioId, string? descripcion, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            // 1) Sumar monto pagado y recalcular estado
            await using var cmdPed = conn.CreateCommand();
            cmdPed.Transaction = tx;
            // El tope contra Total va en el propio WHERE (chequeo atomico): evita que dos pagos
            // concurrentes para el mismo pedido, cada uno validado por separado antes de llegar
            // aqui, sumen mas del total (TOCTOU si solo se valida afuera de la transaccion).
            cmdPed.CommandText = @"
                UPDATE dbo.Pedido
                   SET MontoPagado = MontoPagado + @Monto,
                       EstadoPago = CASE
                                      WHEN (MontoPagado + @Monto) >= Total THEN 'PAGADO'
                                      WHEN (MontoPagado + @Monto) > 0 THEN 'PARCIAL'
                                      ELSE 'PENDIENTE'
                                    END
                 WHERE Id = @PedidoId AND SedeId = @SedeId
                   AND MontoPagado + @Monto <= Total + 0.01";
            cmdPed.AddParam("@Monto", monto);
            cmdPed.AddParam("@PedidoId", pedidoId);
            cmdPed.AddParam("@SedeId", sedeId);
            var filas = await cmdPed.ExecuteNonQueryAsync(ct);
            if (filas == 0)
            {
                await using var cmdExiste = conn.CreateCommand();
                cmdExiste.Transaction = tx;
                cmdExiste.CommandText = "SELECT COUNT(1) FROM dbo.Pedido WHERE Id = @PedidoId AND SedeId = @SedeId";
                cmdExiste.AddParam("@PedidoId", pedidoId);
                cmdExiste.AddParam("@SedeId", sedeId);
                var existe = await cmdExiste.ReadScalarAsync<int>(ct) > 0;
                throw new InvalidOperationException(existe
                    ? "El monto excede el saldo pendiente del pedido."
                    : "Pedido no encontrado.");
            }

            // 2) Registrar en MovimientoCaja
            await using var cmdMov = conn.CreateCommand();
            cmdMov.Transaction = tx;
            cmdMov.CommandText = @"
                INSERT INTO dbo.MovimientoCaja
                       (SedeId, Fecha, Tipo, MetodoPago, Monto, Descripcion, PedidoId, UsuarioId)
                VALUES (@SedeId, SYSDATETIME(), 'INGRESO', @Metodo, @Monto, @Descripcion, @PedidoId, @UsuarioId)";
            cmdMov.AddParam("@SedeId", sedeId);
            cmdMov.AddParam("@Metodo", metodo);
            cmdMov.AddParam("@Monto", monto);
            cmdMov.AddParam("@Descripcion", descripcion ?? $"Pago de pedido");
            cmdMov.AddParam("@PedidoId", pedidoId);
            cmdMov.AddParam("@UsuarioId", usuarioId);
            await cmdMov.ExecuteNonQueryAsync(ct);

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task AgregarItemAsync(int pedidoId, PedidoItem item, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            await using var cmdCheck = conn.CreateCommand();
            cmdCheck.Transaction = tx;
            cmdCheck.CommandText = "SELECT COUNT(1) FROM dbo.Pedido WHERE Id = @PedidoId AND SedeId = @SedeId";
            cmdCheck.AddParam("@PedidoId", pedidoId);
            cmdCheck.AddParam("@SedeId", sedeId);
            if (await cmdCheck.ReadScalarAsync<int>(ct) == 0)
                throw new InvalidOperationException("Pedido no encontrado.");

            await using var cmdItem = conn.CreateCommand();
            cmdItem.Transaction = tx;
            cmdItem.CommandText = @"
                INSERT INTO dbo.PedidoItem (PedidoId, ServicioId, Cantidad, PrecioUnit, Total, Descripcion)
                VALUES (@PedidoId, @ServicioId, @Cantidad, @PrecioUnit, @Total, @Descripcion);";
            cmdItem.AddParam("@PedidoId", pedidoId);
            cmdItem.AddParam("@ServicioId", item.ServicioId);
            cmdItem.AddParam("@Cantidad", item.Cantidad);
            cmdItem.AddParam("@PrecioUnit", item.PrecioUnit);
            cmdItem.AddParam("@Total", item.Total);
            cmdItem.AddParam("@Descripcion", item.Descripcion);
            await cmdItem.ExecuteNonQueryAsync(ct);

            await using var cmdRecalc = conn.CreateCommand();
            cmdRecalc.Transaction = tx;
            // Preserva Descuento y RecargoUrgente (ya fijados al crear el pedido) y vuelve a
            // aplicar el redondeo a 10 centimos sobre el nuevo total, igual que PedidoService.CrearAsync.
            // La version anterior perdia el recargo urgente y el redondeo al recalcular (bug).
            cmdRecalc.CommandText = @"
                ;WITH Calc AS (
                    SELECT p.Id, t.SumaTotal,
                           (t.SumaTotal - p.Descuento + p.RecargoUrgente) AS TotalSinRedondear
                    FROM dbo.Pedido p
                    JOIN (SELECT PedidoId, SUM(Total) AS SumaTotal
                            FROM dbo.PedidoItem
                           WHERE PedidoId = @PedidoId
                          GROUP BY PedidoId) t ON t.PedidoId = p.Id
                    WHERE p.Id = @PedidoId AND p.SedeId = @SedeId
                )
                UPDATE p
                   SET Subtotal = c.SumaTotal,
                       Total = ROUND(c.TotalSinRedondear * 10, 0) / 10.0,
                       Redondeo = ROUND(c.TotalSinRedondear * 10, 0) / 10.0 - c.TotalSinRedondear,
                       EstadoPago = CASE
                                      WHEN p.MontoPagado >= ROUND(c.TotalSinRedondear * 10, 0) / 10.0 THEN 'PAGADO'
                                      WHEN p.MontoPagado > 0 THEN 'PARCIAL'
                                      ELSE 'PENDIENTE'
                                    END
                  FROM dbo.Pedido p
                  JOIN Calc c ON c.Id = p.Id";
            cmdRecalc.AddParam("@PedidoId", pedidoId);
            cmdRecalc.AddParam("@SedeId", sedeId);
            await cmdRecalc.ExecuteNonQueryAsync(ct);

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task ActualizarFechaEntregaAsync(int pedidoId, DateTime nuevaFecha, int usuarioId, string? motivo, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE dbo.Pedido SET FechaEntregaEst = @Fecha WHERE Id = @Id AND SedeId = @SedeId";
            cmd.AddParam("@Fecha", nuevaFecha);
            cmd.AddParam("@Id", pedidoId);
            cmd.AddParam("@SedeId", sedeId);
            var filas = await cmd.ExecuteNonQueryAsync(ct);
            if (filas == 0) throw new InvalidOperationException("Pedido no encontrado.");

            var nota = string.IsNullOrWhiteSpace(motivo)
                ? $"Fecha entrega actualizada a {nuevaFecha:dd/MM/yyyy HH:mm}"
                : $"Fecha entrega -> {nuevaFecha:dd/MM/yyyy HH:mm}. Motivo: {motivo}";

            // Registrar en historial (usamos el estado actual para no cambiar el flujo)
            await using var cmdEstado = conn.CreateCommand();
            cmdEstado.Transaction = tx;
            cmdEstado.CommandText = "SELECT EstadoProceso, AreaActualId FROM dbo.Pedido WHERE Id = @Id AND SedeId = @SedeId";
            cmdEstado.AddParam("@Id", pedidoId);
            cmdEstado.AddParam("@SedeId", sedeId);
            string estado = "PENDIENTE";
            int? areaId = null;
            await using (var r = await cmdEstado.ExecuteReaderAsync(ct))
            {
                if (await r.ReadAsync(ct))
                {
                    estado = r.GetString(0);
                    if (!r.IsDBNull(1)) areaId = r.GetInt32(1);
                }
            }

            await RegistrarHistorialAsync(new PedidoHistorial
            {
                PedidoId = pedidoId,
                AreaId = areaId,
                EstadoProceso = estado,
                UsuarioId = usuarioId,
                Fecha = DateTime.Now,
                Nota = nota
            }, conn, tx, ct);

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<bool> CambiarModalidadAsync(int pedidoId, string modalidad, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE dbo.Pedido SET Modalidad = @Modalidad WHERE Id = @Id AND SedeId = @SedeId";
        cmd.AddParam("@Modalidad", modalidad);
        cmd.AddParam("@Id", pedidoId);
        cmd.AddParam("@SedeId", sedeId);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task AnularAsync(int pedidoId, int usuarioId, string motivo, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                UPDATE dbo.Pedido
                   SET Anulado = 1,
                       EstadoProceso = 'ANULADO',
                       MotivoAnulacion = @Motivo
                 WHERE Id = @Id AND Anulado = 0 AND SedeId = @SedeId";
            cmd.AddParam("@Motivo", motivo);
            cmd.AddParam("@Id", pedidoId);
            cmd.AddParam("@SedeId", sedeId);
            var filas = await cmd.ExecuteNonQueryAsync(ct);
            if (filas == 0) throw new InvalidOperationException("Pedido no encontrado.");

            await RegistrarHistorialAsync(new PedidoHistorial
            {
                PedidoId = pedidoId,
                EstadoProceso = "ANULADO",
                UsuarioId = usuarioId,
                Fecha = DateTime.Now,
                Nota = $"Anulado: {motivo}"
            }, conn, tx, ct);

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    private static Pedido MapPedido(SqlDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")),
        Numero = r.GetInt32(r.GetOrdinal("Numero")),
        ClienteId = r.GetInt32(r.GetOrdinal("ClienteId")),
        ClienteNombre = r.GetNullableString("ClienteNombre"),
        ClienteCelular = r.GetNullableString("ClienteCelular"),
        ClienteDni = r.GetNullableString("ClienteDni"),
        UsuarioId = r.GetInt32(r.GetOrdinal("UsuarioId")),
        UsuarioNombre = r.GetNullableString("UsuarioNombre"),
        FechaIngreso = r.GetDateTime(r.GetOrdinal("FechaIngreso")),
        FechaEntregaEst = r.GetNullableDateTime("FechaEntregaEst"),
        Modalidad = r.GetString(r.GetOrdinal("Modalidad")),
        Subtotal = r.GetDecimal(r.GetOrdinal("Subtotal")),
        Descuento = r.GetDecimal(r.GetOrdinal("Descuento")),
        EsUrgente = r.GetBoolean(r.GetOrdinal("EsUrgente")),
        RecargoUrgente = r.GetDecimal(r.GetOrdinal("RecargoUrgente")),
        Redondeo = r.GetDecimal(r.GetOrdinal("Redondeo")),
        Total = r.GetDecimal(r.GetOrdinal("Total")),
        MontoPagado = r.GetDecimal(r.GetOrdinal("MontoPagado")),
        EstadoPago = r.GetString(r.GetOrdinal("EstadoPago")),
        EstadoProceso = r.GetString(r.GetOrdinal("EstadoProceso")),
        AreaActualId = r.GetNullableInt("AreaActualId"),
        AreaActualNombre = r.GetNullableString("AreaActualNombre"),
        Observaciones = r.GetNullableString("Observaciones"),
        FechaEntregaReal = r.GetNullableDateTime("FechaEntregaReal"),
        Anulado = r.GetBoolean(r.GetOrdinal("Anulado")),
        MotivoAnulacion = r.GetNullableString("MotivoAnulacion"),
        CodigoAntiguo = r.GetNullableString("CodigoAntiguo")
    };

    private static PedidoItem MapItem(SqlDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")),
        PedidoId = r.GetInt32(r.GetOrdinal("PedidoId")),
        ServicioId = r.GetInt32(r.GetOrdinal("ServicioId")),
        ServicioNombre = r.GetNullableString("ServicioNombre"),
        Cantidad = r.GetDecimal(r.GetOrdinal("Cantidad")),
        PrecioUnit = r.GetDecimal(r.GetOrdinal("PrecioUnit")),
        Total = r.GetDecimal(r.GetOrdinal("Total")),
        Descripcion = r.GetNullableString("Descripcion")
    };
}
