using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

[Route("api")]
public class CatalogosController : TenantAwareControllerBase
{
    private readonly IServicioRepository _servicios;
    private readonly IAreaLavadoRepository _areas;
    private readonly IPlantillaWhatsappRepository _plantillas;

    public CatalogosController(IServicioRepository servicios, IAreaLavadoRepository areas, IPlantillaWhatsappRepository plantillas)
    {
        _servicios = servicios;
        _areas = areas;
        _plantillas = plantillas;
    }

    [HttpGet("servicios")]
    public async Task<ActionResult<List<ServicioDto>>> Servicios(CancellationToken ct)
        => Ok((await _servicios.ListarActivosAsync(NegocioId, ct))
                .Select(s => new ServicioDto(s.Id, s.Nombre, s.Precio, s.Unidad, s.CategoriaId))
                .ToList());

    [HttpGet("areas-lavado")]
    public async Task<ActionResult<List<AreaLavadoDto>>> Areas(CancellationToken ct)
        => Ok((await _areas.ListarActivasAsync(SedeId!.Value, ct))
                .Select(a => new AreaLavadoDto(a.Id, a.Nombre, a.Orden, a.TiempoEstMinutos))
                .ToList());

    [HttpGet("plantillas-whatsapp")]
    public async Task<ActionResult<List<PlantillaWhatsappActivaDto>>> PlantillasWhatsapp(CancellationToken ct)
        => Ok((await _plantillas.ListarActivasAsync(NegocioId, ct))
                .Select(p => new PlantillaWhatsappActivaDto(p.Evento, p.Mensaje))
                .ToList());

}
