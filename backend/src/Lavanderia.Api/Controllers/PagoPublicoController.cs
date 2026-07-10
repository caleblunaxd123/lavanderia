using Lavanderia.Api.Domain;
using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;
using Lavanderia.Api.Services;
using Lavanderia.Api.Services.Facturacion;
using Lavanderia.Api.Services.Pagos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

[ApiController]
[Route("api/pago-publico")]
[AllowAnonymous]
public class PagoPublicoController : ControllerBase
{
    private readonly IPagosRepository _pagos;
    private readonly IPedidoRepository _pedidos;
    private readonly IPedidoService _pedidoService;
    private readonly IConfiguracionNegocioRepository _configNegocio;
    private readonly CulqiService _culqi;
    private readonly SecretProtector _secretos;

    public PagoPublicoController(
        IPagosRepository pagos,
        IPedidoRepository pedidos,
        IPedidoService pedidoService,
        IConfiguracionNegocioRepository configNegocio,
        CulqiService culqi,
        SecretProtector secretos)
    {
        _pagos = pagos;
        _pedidos = pedidos;
        _pedidoService = pedidoService;
        _configNegocio = configNegocio;
        _culqi = culqi;
        _secretos = secretos;
    }

    [HttpGet("{token:guid}")]
    public async Task<ActionResult<SeguimientoPedidoDto>> Obtener(Guid token, CancellationToken ct)
    {
        var solicitud = await _pagos.ObtenerPorTokenAsync(token, ct);
        if (solicitud is null) return NotFound(new { mensaje = "Link no encontrado o inválido." });

        var pedido = await _pedidos.ObtenerPorIdAsync(solicitud.PedidoId, solicitud.SedeId, ct);
        if (pedido is null) return NotFound(new { mensaje = "El pedido ya no existe." });

        var config = await _configNegocio.ObtenerAsync(solicitud.NegocioId, ct);
        var pagosConfig = await _pagos.ObtenerConfigAsync(solicitud.NegocioId, ct);

        var saldo = Math.Max(0m, pedido.Total - pedido.MontoPagado);
        var puedePagar = saldo > 0.01m
            && !pedido.Anulado
            && solicitud.Estado == "PENDIENTE"
            && solicitud.FechaExpiracion > DateTime.Now
            && (pagosConfig?.Activo ?? false)
            && !string.IsNullOrWhiteSpace(pagosConfig?.PublicKey);

        return Ok(new SeguimientoPedidoDto
        {
            NombreNegocio = config?.NombreNegocio ?? "Lavandería",
            LogoUrl = config?.LogoUrl,
            ColorPrimario = string.IsNullOrWhiteSpace(config?.ColorPrimario) ? "#0b57d0" : config!.ColorPrimario,
            TelefonoNegocio = config?.Telefono,
            DireccionNegocio = config?.Direccion,
            NumeroPedido = pedido.Numero,
            Modalidad = pedido.Modalidad,
            ResumenEstado = ConstruirResumenEstado(pedido),
            FechaCompromiso = pedido.FechaEntregaEst,
            EtiquetaFechaCompromiso = EtiquetaFechaCompromiso(pedido.Modalidad),
            Pasos = ConstruirPasos(pedido),
            Items = pedido.Items
                .Select(i => new SeguimientoPedidoItemDto(i.ServicioNombre ?? "Servicio", i.Cantidad))
                .ToList(),
            Anulado = pedido.Anulado,
            Total = pedido.Total,
            MontoPagado = pedido.MontoPagado,
            Saldo = saldo,
            RequierePago = puedePagar,
            PublicKeyCulqi = puedePagar ? pagosConfig!.PublicKey : null
        });
    }

    [HttpPost("{token:guid}/cobrar")]
    public async Task<ActionResult<CobrarSolicitudPagoResultDto>> Cobrar(Guid token, [FromBody] CobrarSolicitudPagoRequest req, CancellationToken ct)
    {
        var solicitud = await _pagos.ObtenerPorTokenAsync(token, ct);
        if (solicitud is null) return NotFound(new { mensaje = "Link no encontrado o inválido." });
        if (solicitud.Estado != "PENDIENTE" || solicitud.FechaExpiracion <= DateTime.Now)
            return BadRequest(new { mensaje = "Este link de pago ya no está vigente." });

        var pedido = await _pedidos.ObtenerPorIdAsync(solicitud.PedidoId, solicitud.SedeId, ct);
        if (pedido is null) return NotFound(new { mensaje = "El pedido ya no existe." });
        if (pedido.Anulado)
            return BadRequest(new { mensaje = "Este pedido fue anulado." });

        var saldo = Math.Max(0m, pedido.Total - pedido.MontoPagado);
        if (saldo <= 0.01m)
            return Ok(new CobrarSolicitudPagoResultDto { Exito = true, Mensaje = "Este pedido ya está pagado.", SaldoPendiente = 0 });

        var pagosConfig = await _pagos.ObtenerConfigAsync(solicitud.NegocioId, ct);
        if (pagosConfig is null || !pagosConfig.Activo || string.IsNullOrEmpty(pagosConfig.SecretKeyCifrada))
            return BadRequest(new { mensaje = "Los pagos en línea no están habilitados para este negocio." });

        string secretKey;
        try
        {
            secretKey = _secretos.Desproteger(pagosConfig.SecretKeyCifrada);
        }
        catch
        {
            return StatusCode(500, new { mensaje = "No se pudo procesar el pago. Contacta al negocio." });
        }

        var resultado = await _culqi.CobrarAsync(
            secretKey,
            saldo,
            req.CulqiTokenId,
            req.Email,
            $"Pedido #{pedido.Numero}",
            pedido.ClienteNombre,
            pedido.ClienteCelular,
            pedido.ClienteDni,
            ct);

        if (!resultado.Exitoso)
            return Ok(new CobrarSolicitudPagoResultDto { Exito = false, Mensaje = resultado.Mensaje, SaldoPendiente = saldo });

        var marcadoAhora = await _pagos.MarcarPagadoAsync(solicitud.Id, resultado.ChargeId ?? "", ct);
        if (marcadoAhora)
        {
            await _pedidoService.RegistrarPagoAsync(
                pedido.Id,
                new RegistrarPagoRequest { Monto = saldo, Metodo = "TARJETA", Descripcion = "Pago en línea (Culqi)" },
                pedido.UsuarioId,
                solicitud.SedeId,
                ct);
        }

        return Ok(new CobrarSolicitudPagoResultDto { Exito = true, Mensaje = "Pago confirmado. Gracias.", SaldoPendiente = 0 });
    }

    private static List<PasoSeguimientoDto> ConstruirPasos(Pedido pedido)
    {
        var codigos = new[] { "PENDIENTE", "EN_PROCESO", "LISTO", "ENTREGADO" };
        var nombres = new Dictionary<string, string>
        {
            ["PENDIENTE"] = pedido.Modalidad switch
            {
                "Recojo" => "Recojo programado",
                "Delivery" => "Recojo programado",
                _ => "Pedido recibido"
            },
            ["EN_PROCESO"] = pedido.Modalidad switch
            {
                "Recojo" or "Delivery" => "En lavandería",
                _ => "En proceso"
            },
            ["LISTO"] = pedido.Modalidad switch
            {
                "Delivery" => "En ruta",
                _ => "Listo para recoger"
            },
            ["ENTREGADO"] = pedido.Modalidad switch
            {
                "Delivery" => "Entregado",
                _ => "Entregado"
            }
        };

        var idxActual = Array.IndexOf(codigos, pedido.EstadoProceso);
        return codigos.Select((codigo, index) => new PasoSeguimientoDto(
            codigo,
            nombres[codigo],
            Alcanzado: idxActual >= 0 && index <= idxActual,
            Actual: index == idxActual
        )).ToList();
    }

    private static string ConstruirResumenEstado(Pedido pedido)
    {
        if (pedido.Anulado) return "Este pedido fue anulado.";

        return pedido.EstadoProceso switch
        {
            "PENDIENTE" => pedido.Modalidad switch
            {
                "Recojo" or "Delivery" => "Ya registramos tu pedido y lo dejamos listo para el servicio a domicilio.",
                _ => "Tu pedido fue recibido correctamente."
            },
            "EN_PROCESO" => "Estamos trabajando tu pedido.",
            "LISTO" => pedido.Modalidad switch
            {
                "Delivery" => "Tu pedido está listo y saldrá a ruta.",
                _ => "Tu pedido ya está listo para recoger."
            },
            "ENTREGADO" => pedido.Modalidad == "Delivery"
                ? "El pedido ya fue entregado."
                : "El pedido ya fue entregado o retirado.",
            _ => "Puedes seguir el avance de tu pedido desde esta página."
        };
    }

    private static string EtiquetaFechaCompromiso(string modalidad)
    {
        return modalidad == "Delivery"
            ? "Entrega estimada"
            : "Fecha estimada";
    }
}
