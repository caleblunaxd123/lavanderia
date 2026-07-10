using Lavanderia.Api.Domain;
using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

[Route("api/configuracion")]
[Microsoft.AspNetCore.Authorization.Authorize(Policy = "Modulo:AJUSTES")]
public class ConfiguracionController : TenantAwareControllerBase
{
    private readonly IConfiguracionNegocioRepository _repo;
    private readonly INegocioRepository _negocios;
    private readonly IServicioRepository _servicios;

    public ConfiguracionController(IConfiguracionNegocioRepository repo, INegocioRepository negocios, IServicioRepository servicios)
    {
        _repo = repo;
        _negocios = negocios;
        _servicios = servicios;
    }

    /// <summary>
    /// Si hay sesion valida, devuelve la configuracion del negocio del usuario autenticado.
    /// Sin sesion, se usa el primer negocio de esta instancia (fallback generico para accesos
    /// sin slug de empresa en la URL — ver GET /publico/{slug} para el caso normal de login).
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<ConfiguracionNegocioDto>> Obtener(CancellationToken ct)
    {
        var negocioId = User.Identity?.IsAuthenticated == true ? NegocioId : (int?)null;
        var c = await _repo.ObtenerAsync(negocioId, ct);
        if (c is null) return NotFound();
        var dto = Map(c);
        if (negocioId.HasValue)
            dto.ServicioDeliveryId = (await _servicios.ObtenerCargoDeliveryAsync(negocioId.Value, ct))?.Id;
        return Ok(dto);
    }

    /// <summary>
    /// Marca del negocio identificado por el slug de su URL (ej. /lavixa/login), para pintarla
    /// antes de que el usuario inicie sesion. Publico: no expone nada que no exponga ya el
    /// endpoint anonimo de arriba (RUC/IGV son datos fiscales de por si publicos en Peru).
    /// </summary>
    [HttpGet("publico/{slug}")]
    [AllowAnonymous]
    public async Task<ActionResult<ConfiguracionNegocioDto>> ObtenerPorSlug(string slug, CancellationToken ct)
    {
        var negocio = await _negocios.ObtenerPorSlugAsync(slug, ct);
        if (negocio is null) return NotFound();
        var c = await _repo.ObtenerAsync(negocio.Id, ct);
        if (c is null) return NotFound();
        var dto = Map(c);
        dto.ServicioDeliveryId = (await _servicios.ObtenerCargoDeliveryAsync(negocio.Id, ct))?.Id;
        return Ok(dto);
    }

    [HttpPut]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> Actualizar([FromBody] ConfiguracionNegocioDto dto, CancellationToken ct)
    {
        // Upsert: un negocio recien creado aun no tiene fila propia en ConfiguracionNegocio.
        var existente = await _repo.ObtenerAsync(NegocioId, ct) ?? new ConfiguracionNegocio();

        existente.NombreNegocio = dto.NombreNegocio;
        existente.LogoUrl = dto.LogoUrl;
        existente.ColorPrimario = dto.ColorPrimario;
        existente.ColorSecundario = dto.ColorSecundario;
        existente.ColorAcento = dto.ColorAcento;
        existente.Direccion = dto.Direccion;
        existente.Telefono = dto.Telefono;
        existente.Ruc = dto.Ruc;
        existente.HorarioAtencion = dto.HorarioAtencion;
        existente.Igv = dto.Igv;
        existente.MetaMensual = dto.MetaMensual;
        existente.SolesPorPunto = dto.SolesPorPunto;
        existente.AnchoTicketMm = dto.AnchoTicketMm;
        existente.MensajePieTicket = dto.MensajePieTicket;
        existente.CondicionesServicio = dto.CondicionesServicio;
        existente.NotasProduccion = dto.NotasProduccion;
        existente.CostoDelivery = dto.CostoDelivery;

        await _repo.ActualizarAsync(existente, NegocioId, ct);

        // El servidor jamas confia en el precio que manda el cliente al crear un pedido: siempre
        // recalcula Total = Servicio.Precio * Cantidad (ver PedidoService.CrearAsync). Por eso el
        // Precio del servicio de sistema debe reflejar el CostoDelivery configurado aqui, o el
        // cargo de delivery se aplicaria como S/ 0 en el pedido real sin importar lo que Registrar
        // muestre en pantalla.
        var servicioDelivery = await _servicios.ObtenerCargoDeliveryAsync(NegocioId, ct);
        if (servicioDelivery is not null && servicioDelivery.Precio != dto.CostoDelivery)
        {
            servicioDelivery.Precio = dto.CostoDelivery;
            await _servicios.ActualizarAsync(servicioDelivery, NegocioId, ct);
        }

        return NoContent();
    }

    private static ConfiguracionNegocioDto Map(ConfiguracionNegocio c) => new()
    {
        Id = c.Id,
        NombreNegocio = c.NombreNegocio,
        LogoUrl = c.LogoUrl,
        ColorPrimario = c.ColorPrimario,
        ColorSecundario = c.ColorSecundario,
        ColorAcento = c.ColorAcento,
        Direccion = c.Direccion,
        Telefono = c.Telefono,
        Ruc = c.Ruc,
        HorarioAtencion = c.HorarioAtencion,
        Igv = c.Igv,
        MetaMensual = c.MetaMensual,
        SolesPorPunto = c.SolesPorPunto,
        AnchoTicketMm = c.AnchoTicketMm,
        MensajePieTicket = c.MensajePieTicket,
        CondicionesServicio = c.CondicionesServicio,
        NotasProduccion = c.NotasProduccion,
        CostoDelivery = c.CostoDelivery
    };
}
