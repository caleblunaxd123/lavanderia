using Lavanderia.Api.Dtos;
using Lavanderia.Api.Infrastructure;

namespace Lavanderia.Api.Repositories;

/// <summary>
/// Analitica "gerencial": tablero de SLA/cuellos de botella (basado en PedidoHistorial) y la
/// vista unificada que junta caja + inventario + facturacion + cobranza en una sola pantalla.
/// Vive separado de IReporteRepository porque devuelve DTOs tipados para un dashboard visual,
/// no el formato generico de tabla (Columnas/Filas) que usan los reportes exportables.
/// </summary>
public interface IGerencialRepository
{
    Task<TableroSlaDto> ObtenerTableroSlaAsync(int sedeId, DateTime desde, DateTime hasta, CancellationToken ct = default);
    Task<VistaGerencialDto> ObtenerVistaGerencialAsync(int negocioId, int sedeId, CancellationToken ct = default);
    Task<List<ConsolidadoSedeDto>> ObtenerConsolidadoAsync(int negocioId, CancellationToken ct = default);
}

public class GerencialRepository : IGerencialRepository
{
    private readonly ISqlConnectionFactory _factory;
    public GerencialRepository(ISqlConnectionFactory factory) => _factory = factory;

    public async Task<TableroSlaDto> ObtenerTableroSlaAsync(int sedeId, DateTime desde, DateTime hasta, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);

        var resultado = new TableroSlaDto();

        // Tiempo promedio real por area: para cada fila de PedidoHistorial se mide contra la
        // fila SIGUIENTE del mismo pedido (LEAD) — esa diferencia de tiempo es lo que el pedido
        // realmente estuvo en esa area/estado. Se compara contra AreaLavado.TiempoEstMinutos.
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                ;WITH Transiciones AS (
                    SELECT h.AreaId,
                           DATEDIFF(MINUTE, h.Fecha, LEAD(h.Fecha) OVER (PARTITION BY h.PedidoId ORDER BY h.Fecha)) AS MinutosReal
                    FROM dbo.PedidoHistorial h
                    INNER JOIN dbo.Pedido p ON p.Id = h.PedidoId
                    WHERE p.SedeId = @SedeId AND p.FechaIngreso >= @Desde AND p.FechaIngreso < @Hasta AND p.Anulado = 0
                )
                SELECT a.Id AS AreaId, a.Nombre AS AreaNombre, a.Orden, a.TiempoEstMinutos,
                       AVG(CAST(t.MinutosReal AS FLOAT)) AS MinutosPromedioReal,
                       COUNT(*) AS PedidosProcesados
                FROM Transiciones t
                INNER JOIN dbo.AreaLavado a ON a.Id = t.AreaId
                WHERE t.MinutosReal IS NOT NULL
                GROUP BY a.Id, a.Nombre, a.Orden, a.TiempoEstMinutos
                ORDER BY a.Orden";
            cmd.AddParam("@SedeId", sedeId);
            cmd.AddParam("@Desde", desde.Date);
            cmd.AddParam("@Hasta", hasta.Date.AddDays(1));
            resultado.Areas = await cmd.ReadListAsync(r => new SlaAreaDto(
                r.GetInt32(r.GetOrdinal("AreaId")),
                r.GetString(r.GetOrdinal("AreaNombre")),
                r.GetInt32(r.GetOrdinal("Orden")),
                r.GetInt32(r.GetOrdinal("TiempoEstMinutos")),
                r.GetDouble(r.GetOrdinal("MinutosPromedioReal")),
                r.GetInt32(r.GetOrdinal("PedidosProcesados"))
            ), ct);
        }

        // Pedidos "estancados" ahora mismo: llevan mas de 1.5x el tiempo estimado de su area
        // actual sin haber avanzado. El factor 1.5 es deliberadamente generoso (una demora
        // corta es normal) para que la alerta solo suene ante un cuello de botella real.
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                ;WITH UltimoCambio AS (
                    SELECT PedidoId, MAX(Fecha) AS FechaUltimoCambio
                    FROM dbo.PedidoHistorial
                    GROUP BY PedidoId
                )
                SELECT p.Id AS PedidoId, p.Numero, c.Nombre AS ClienteNombre, a.Id AS AreaId, a.Nombre AS AreaNombre,
                       DATEDIFF(MINUTE, u.FechaUltimoCambio, SYSDATETIME()) AS MinutosEnArea,
                       a.TiempoEstMinutos
                FROM dbo.Pedido p
                INNER JOIN UltimoCambio u ON u.PedidoId = p.Id
                INNER JOIN dbo.AreaLavado a ON a.Id = p.AreaActualId
                INNER JOIN dbo.Cliente c ON c.Id = p.ClienteId
                WHERE p.SedeId = @SedeId AND p.EstadoProceso = 'EN_PROCESO' AND p.Anulado = 0
                  AND DATEDIFF(MINUTE, u.FechaUltimoCambio, SYSDATETIME()) > a.TiempoEstMinutos * 1.5
                ORDER BY MinutosEnArea DESC";
            cmd.AddParam("@SedeId", sedeId);
            resultado.Estancados = await cmd.ReadListAsync(r => new PedidoEstancadoDto(
                r.GetInt32(r.GetOrdinal("PedidoId")),
                r.GetInt32(r.GetOrdinal("Numero")),
                r.GetString(r.GetOrdinal("ClienteNombre")),
                r.GetInt32(r.GetOrdinal("AreaId")),
                r.GetString(r.GetOrdinal("AreaNombre")),
                r.GetInt32(r.GetOrdinal("MinutosEnArea")),
                r.GetInt32(r.GetOrdinal("TiempoEstMinutos"))
            ), ct);
        }

        return resultado;
    }

    public async Task<VistaGerencialDto> ObtenerVistaGerencialAsync(int negocioId, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        var hoy = DateTime.Today;
        var inicioSemana = hoy.AddDays(-(((int)hoy.DayOfWeek + 6) % 7));
        var inicioMes = new DateTime(hoy.Year, hoy.Month, 1);
        var dto = new VistaGerencialDto();

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT
                    ISNULL(SUM(CASE WHEN CAST(FechaIngreso AS DATE) = @Hoy THEN Total ELSE 0 END), 0) AS VentasHoy,
                    ISNULL(SUM(CASE WHEN FechaIngreso >= @InicioMes THEN Total ELSE 0 END), 0) AS VentasMes,
                    ISNULL(SUM(Total - MontoPagado), 0) AS SaldoPorCobrar,
                    COUNT(CASE WHEN EstadoProceso IN ('PENDIENTE', 'EN_PROCESO') THEN 1 END) AS PedidosActivos,
                    COUNT(CASE WHEN EstadoProceso = 'LISTO' THEN 1 END) AS PedidosListos
                FROM dbo.Pedido
                WHERE SedeId = @SedeId AND Anulado = 0";
            cmd.AddParam("@SedeId", sedeId);
            cmd.AddParam("@Hoy", hoy);
            cmd.AddParam("@InicioMes", inicioMes);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct))
            {
                dto.VentasHoy = r.GetDecimal(r.GetOrdinal("VentasHoy"));
                dto.VentasMes = r.GetDecimal(r.GetOrdinal("VentasMes"));
                dto.SaldoPorCobrar = r.GetDecimal(r.GetOrdinal("SaldoPorCobrar"));
                dto.PedidosActivos = r.GetInt32(r.GetOrdinal("PedidosActivos"));
                dto.PedidosListosSinRecoger = r.GetInt32(r.GetOrdinal("PedidosListos"));
            }
        }

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT
                    COUNT(CASE WHEN CAST(p.FechaEntregaReal AS DATE) = @Hoy THEN 1 END) AS PedidosHoy,
                    COUNT(CASE WHEN CAST(p.FechaEntregaReal AS DATE) = @Hoy AND p.Modalidad <> 'Delivery' THEN 1 END) AS PedidosTiendaHoy,
                    COUNT(CASE WHEN CAST(p.FechaEntregaReal AS DATE) = @Hoy AND p.Modalidad = 'Delivery' THEN 1 END) AS PedidosDomicilioHoy,
                    COUNT(CASE WHEN p.FechaEntregaReal >= @InicioSemana THEN 1 END) AS PedidosSemana,
                    COUNT(CASE WHEN p.FechaEntregaReal >= @InicioMes THEN 1 END) AS PedidosMes
                FROM dbo.Pedido p
                WHERE p.SedeId = @SedeId AND p.Anulado = 0 AND p.EstadoProceso = 'ENTREGADO'
                  AND p.FechaEntregaReal IS NOT NULL";
            cmd.AddParam("@SedeId", sedeId);
            cmd.AddParam("@Hoy", hoy);
            cmd.AddParam("@InicioSemana", inicioSemana);
            cmd.AddParam("@InicioMes", inicioMes);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct))
            {
                dto.PedidosEntregadosHoy = r.GetInt32(r.GetOrdinal("PedidosHoy"));
                dto.PedidosEntregadosTiendaHoy = r.GetInt32(r.GetOrdinal("PedidosTiendaHoy"));
                dto.PedidosEntregadosDomicilioHoy = r.GetInt32(r.GetOrdinal("PedidosDomicilioHoy"));
                dto.PedidosEntregadosSemana = r.GetInt32(r.GetOrdinal("PedidosSemana"));
                dto.PedidosEntregadosMes = r.GetInt32(r.GetOrdinal("PedidosMes"));
            }
        }

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT
                    ISNULL(SUM(CASE WHEN Tipo = 'GASTO' AND Fecha >= @InicioMes THEN Monto ELSE 0 END), 0) AS GastosMes,
                    ISNULL(SUM(CASE WHEN Tipo = 'INGRESO' AND CAST(Fecha AS DATE) = @Hoy THEN Monto ELSE 0 END), 0) AS CobradoHoy,
                    ISNULL(SUM(CASE WHEN Tipo = 'INGRESO' AND MetodoPago = 'EFECTIVO' AND CAST(Fecha AS DATE) = @Hoy THEN Monto ELSE 0 END), 0)
                        - ISNULL(SUM(CASE WHEN Tipo = 'GASTO' AND MetodoPago = 'EFECTIVO' AND CAST(Fecha AS DATE) = @Hoy THEN Monto ELSE 0 END), 0)
                        AS CajaEsperadaHoy
                FROM dbo.MovimientoCaja
                WHERE SedeId = @SedeId";
            cmd.AddParam("@SedeId", sedeId);
            cmd.AddParam("@Hoy", hoy);
            cmd.AddParam("@InicioMes", inicioMes);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct))
            {
                dto.GastosMes = r.GetDecimal(r.GetOrdinal("GastosMes"));
                dto.CobradoHoy = r.GetDecimal(r.GetOrdinal("CobradoHoy"));
                dto.CajaEsperadaHoy = r.GetDecimal(r.GetOrdinal("CajaEsperadaHoy"));
            }
        }
        dto.UtilidadMes = dto.VentasMes - dto.GastosMes;

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT
                    COUNT(CASE WHEN Estado = 'PENDIENTE' THEN 1 END) AS Pendientes,
                    COUNT(CASE WHEN Estado IN ('RECHAZADO', 'ERROR') THEN 1 END) AS Rechazados
                FROM dbo.ComprobanteElectronico
                WHERE SedeId = @SedeId";
            cmd.AddParam("@SedeId", sedeId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct))
            {
                dto.ComprobantesPendientes = r.GetInt32(r.GetOrdinal("Pendientes"));
                dto.ComprobantesRechazados = r.GetInt32(r.GetOrdinal("Rechazados"));
            }
        }

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(1) FROM dbo.Insumo WHERE SedeId = @SedeId AND Activo = 1 AND StockActual <= StockMinimo";
            cmd.AddParam("@SedeId", sedeId);
            dto.InsumosBajoStock = await cmd.ReadScalarAsync<int>(ct);
        }

        return dto;
    }

    /// <summary>KPIs por sede de todo el negocio, para que el dueño con varias sucursales vea el
    /// panorama junto (una fila por sede activa; el total lo arma el frontend).</summary>
    public async Task<List<ConsolidadoSedeDto>> ObtenerConsolidadoAsync(int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        var hoy = DateTime.Today;
        var inicioMes = new DateTime(hoy.Year, hoy.Month, 1);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT s.Id AS SedeId, s.Nombre AS SedeNombre,
                ISNULL(SUM(CASE WHEN p.Anulado = 0 AND CAST(p.FechaIngreso AS DATE) = @Hoy THEN p.Total ELSE 0 END), 0) AS VentasHoy,
                ISNULL(SUM(CASE WHEN p.Anulado = 0 AND p.FechaIngreso >= @InicioMes THEN p.Total ELSE 0 END), 0) AS VentasMes,
                ISNULL(SUM(CASE WHEN p.Anulado = 0 AND p.Total > p.MontoPagado THEN p.Total - p.MontoPagado ELSE 0 END), 0) AS SaldoPorCobrar,
                COUNT(CASE WHEN p.Anulado = 0 AND p.EstadoProceso IN ('PENDIENTE','EN_PROCESO') THEN 1 END) AS PedidosActivos,
                COUNT(CASE WHEN p.Anulado = 0 AND p.EstadoProceso = 'LISTO' THEN 1 END) AS PedidosListos
            FROM dbo.Sede s
            LEFT JOIN dbo.Pedido p ON p.SedeId = s.Id
            WHERE s.NegocioId = @NegocioId AND s.Activo = 1
            GROUP BY s.Id, s.Nombre
            ORDER BY s.Nombre";
        cmd.AddParam("@NegocioId", negocioId);
        cmd.AddParam("@Hoy", hoy);
        cmd.AddParam("@InicioMes", inicioMes);
        return await cmd.ReadListAsync(r => new ConsolidadoSedeDto(
            r.GetInt32(r.GetOrdinal("SedeId")),
            r.GetString(r.GetOrdinal("SedeNombre")),
            r.GetDecimal(r.GetOrdinal("VentasHoy")),
            r.GetDecimal(r.GetOrdinal("VentasMes")),
            r.GetDecimal(r.GetOrdinal("SaldoPorCobrar")),
            r.GetInt32(r.GetOrdinal("PedidosActivos")),
            r.GetInt32(r.GetOrdinal("PedidosListos"))
        ), ct);
    }
}
