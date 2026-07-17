using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

[Route("api/reportes")]
[Microsoft.AspNetCore.Authorization.Authorize(Policy = "Modulo:REPORTES")]
public class ReportesController : TenantAwareControllerBase
{
    private readonly IReporteRepository _repo;
    private readonly IGerencialRepository _gerencial;
    public ReportesController(IReporteRepository repo, IGerencialRepository gerencial)
    {
        _repo = repo;
        _gerencial = gerencial;
    }

    [HttpGet("sla")]
    public async Task<ActionResult<TableroSlaDto>> Sla([FromQuery] DateTime? desde, [FromQuery] DateTime? hasta, CancellationToken ct)
    {
        var (d, h) = Rango(desde, hasta);
        return Ok(await _gerencial.ObtenerTableroSlaAsync(SedeId!.Value, d, h, ct));
    }

    [HttpGet("vista-gerencial")]
    public async Task<ActionResult<VistaGerencialDto>> VistaGerencial(CancellationToken ct)
        => Ok(await _gerencial.ObtenerVistaGerencialAsync(NegocioId, SedeId!.Value, ct));

    [HttpGet("consolidado")]
    public async Task<ActionResult<List<ConsolidadoSedeDto>>> Consolidado(CancellationToken ct)
        => Ok(await _gerencial.ObtenerConsolidadoAsync(NegocioId, ct));

    private static (DateTime desde, DateTime hasta) Rango(DateTime? desde, DateTime? hasta)
    {
        var h = hasta ?? DateTime.Today;
        var d = desde ?? h.AddDays(-30);
        return (d.Date, h.Date);
    }

    [HttpGet("ordenes-pendientes")]
    public async Task<ActionResult<ReporteResultDto>> OrdenesPendientes(CancellationToken ct)
        => Ok(await _repo.OrdenesPendientesAsync(SedeId!.Value, ct));

    [HttpGet("gastos")]
    public async Task<ActionResult<ReporteResultDto>> Gastos([FromQuery] DateTime? desde, [FromQuery] DateTime? hasta, CancellationToken ct)
    {
        var (d, h) = Rango(desde, hasta);
        return Ok(await _repo.GastosAsync(d, h, SedeId!.Value, ct));
    }

    [HttpGet("general")]
    public async Task<ActionResult<ReporteResultDto>> General([FromQuery] DateTime? desde, [FromQuery] DateTime? hasta, CancellationToken ct)
    {
        var (d, h) = Rango(desde, hasta);
        return Ok(await _repo.GeneralAsync(d, h, SedeId!.Value, ct));
    }

    [HttpGet("servicios")]
    public async Task<ActionResult<ReporteResultDto>> Servicios([FromQuery] DateTime? desde, [FromQuery] DateTime? hasta, CancellationToken ct)
    {
        var (d, h) = Rango(desde, hasta);
        return Ok(await _repo.ServiciosAsync(d, h, SedeId!.Value, ct));
    }

    [HttpGet("cuadres-caja")]
    public async Task<ActionResult<ReporteResultDto>> CuadresCaja([FromQuery] DateTime? desde, [FromQuery] DateTime? hasta, CancellationToken ct)
    {
        var (d, h) = Rango(desde, hasta);
        return Ok(await _repo.CuadresCajaAsync(d, h, SedeId!.Value, ct));
    }

    /// <summary>Reporte mensual dedicado de cuadres diarios (pantalla propia con vista
    /// colapsable, corte/digital/tarjeta y días sin información / montos no cuadrados).</summary>
    [HttpGet("cuadres-diarios")]
    public async Task<ActionResult<CuadresDiariosReporteDto>> CuadresDiarios([FromQuery] int? anio, [FromQuery] int? mes, CancellationToken ct)
    {
        var hoy = DateTime.Today;
        var a = anio ?? hoy.Year;
        var m = mes ?? hoy.Month;
        if (m < 1 || m > 12) return BadRequest(new { mensaje = "Mes inválido." });
        return Ok(await _repo.CuadresDiariosAsync(a, m, SedeId!.Value, ct));
    }

    [HttpGet("ordenes-mensual")]
    public async Task<ActionResult<ReporteResultDto>> OrdenesMensual([FromQuery] DateTime? desde, [FromQuery] DateTime? hasta, CancellationToken ct)
    {
        var (d, h) = Rango(desde, hasta);
        return Ok(await _repo.OrdenesMensualAsync(d, h, SedeId!.Value, ct));
    }

    [HttpGet("almacen")]
    public async Task<ActionResult<ReporteResultDto>> Almacen(CancellationToken ct)
        => Ok(await _repo.AlmacenAsync(SedeId!.Value, ct));

    [HttpGet("anulados")]
    public async Task<ActionResult<ReporteResultDto>> Anulados([FromQuery] DateTime? desde, [FromQuery] DateTime? hasta, CancellationToken ct)
    {
        var (d, h) = Rango(desde, hasta);
        return Ok(await _repo.AnuladosAsync(d, h, SedeId!.Value, ct));
    }

    [HttpGet("registro-entregas")]
    public async Task<ActionResult<ReporteResultDto>> RegistroEntregas([FromQuery] DateTime? desde, [FromQuery] DateTime? hasta, CancellationToken ct)
    {
        var (d, h) = Rango(desde, hasta);
        return Ok(await _repo.RegistroEntregasAsync(d, h, SedeId!.Value, ct));
    }

    [HttpGet("pagos")]
    public async Task<ActionResult<ReporteResultDto>> Pagos([FromQuery] DateTime? desde, [FromQuery] DateTime? hasta, CancellationToken ct)
    {
        var (d, h) = Rango(desde, hasta);
        return Ok(await _repo.PagosAsync(d, h, SedeId!.Value, ct));
    }

    [HttpGet("descuento-directo")]
    public async Task<ActionResult<ReporteResultDto>> DescuentoDirecto([FromQuery] DateTime? desde, [FromQuery] DateTime? hasta, CancellationToken ct)
    {
        var (d, h) = Rango(desde, hasta);
        return Ok(await _repo.DescuentoDirectoAsync(d, h, SedeId!.Value, ct));
    }

    private async Task<ReporteResultDto?> ObtenerPorKeyAsync(string key, DateTime d, DateTime h, CancellationToken ct) => key switch
    {
        "ordenes-pendientes" => await _repo.OrdenesPendientesAsync(SedeId!.Value, ct),
        "gastos" => await _repo.GastosAsync(d, h, SedeId!.Value, ct),
        "general" => await _repo.GeneralAsync(d, h, SedeId!.Value, ct),
        "servicios" => await _repo.ServiciosAsync(d, h, SedeId!.Value, ct),
        "cuadres-caja" => await _repo.CuadresCajaAsync(d, h, SedeId!.Value, ct),
        "ordenes-mensual" => await _repo.OrdenesMensualAsync(d, h, SedeId!.Value, ct),
        "almacen" => await _repo.AlmacenAsync(SedeId!.Value, ct),
        "anulados" => await _repo.AnuladosAsync(d, h, SedeId!.Value, ct),
        "registro-entregas" => await _repo.RegistroEntregasAsync(d, h, SedeId!.Value, ct),
        "pagos" => await _repo.PagosAsync(d, h, SedeId!.Value, ct),
        "descuento-directo" => await _repo.DescuentoDirectoAsync(d, h, SedeId!.Value, ct),
        _ => null
    };

    /// <summary>Exporta cualquier reporte a un archivo Excel (.xlsx) real con encabezados.</summary>
    [HttpGet("export/{key}")]
    public async Task<IActionResult> Exportar(string key, [FromQuery] DateTime? desde, [FromQuery] DateTime? hasta, CancellationToken ct)
    {
        var (d, h) = Rango(desde, hasta);
        var rep = await ObtenerPorKeyAsync(key, d, h, ct);
        if (rep is null) return NotFound(new { mensaje = "Reporte desconocido." });

        using var wb = new ClosedXML.Excel.XLWorkbook();
        var ws = wb.Worksheets.Add("Reporte");
        // Encabezados (omitimos la columna interna _id).
        var cols = rep.Columnas;
        for (int c = 0; c < cols.Count; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = cols[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#1e40af");
            cell.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
        }
        for (int f = 0; f < rep.Filas.Count; f++)
            for (int c = 0; c < cols.Count; c++)
                ws.Cell(f + 2, c + 1).Value = rep.Filas[f].TryGetValue(cols[c], out var v) ? v : "";
        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        var nombre = $"reporte-{key}-{d:yyyyMMdd}-{h:yyyyMMdd}.xlsx";
        return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", nombre);
    }
}
