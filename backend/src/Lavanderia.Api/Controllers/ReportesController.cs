using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

[Route("api/reportes")]
public class ReportesController : TenantAwareControllerBase
{
    private readonly IReporteRepository _repo;
    public ReportesController(IReporteRepository repo) => _repo = repo;

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
}
