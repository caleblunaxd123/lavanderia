using Lavanderia.Api.Domain;
using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;
using Lavanderia.Api.Services;
using Lavanderia.Api.Services.Facturacion;
using Lavanderia.Api.Services.Pagos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

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
    private readonly IRutaRepartoRepository _rutas;
    private readonly CulqiService _culqi;
    private readonly SecretProtector _secretos;
    private readonly ILogger<PagoPublicoController> _log;

    public PagoPublicoController(
        IPagosRepository pagos,
        IPedidoRepository pedidos,
        IPedidoService pedidoService,
        IConfiguracionNegocioRepository configNegocio,
        IRutaRepartoRepository rutas,
        CulqiService culqi,
        SecretProtector secretos,
        ILogger<PagoPublicoController> log)
    {
        _pagos = pagos;
        _pedidos = pedidos;
        _pedidoService = pedidoService;
        _configNegocio = configNegocio;
        _rutas = rutas;
        _culqi = culqi;
        _secretos = secretos;
        _log = log;
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

        // Seguimiento en vivo del reparto: posición del repartidor, estado de ruta y ETA.
        var ruta = await _rutas.ObtenerPorPedidoAsync(pedido.Id, solicitud.SedeId, ct);
        var distancia = ruta is null ? null : SeguimientoRutaCalculo.DistanciaAlDestino(ruta);
        var estadoRuta = ruta is null ? "SIN_RUTA" : SeguimientoRutaCalculo.DeterminarEstado(ruta, distancia);

        return Ok(new SeguimientoPedidoDto
        {
            NombreNegocio = config?.NombreNegocio ?? "Lavandería",
            LogoUrl = config?.LogoUrl,
            ColorPrimario = string.IsNullOrWhiteSpace(config?.ColorPrimario) ? "#0b57d0" : config!.ColorPrimario,
            TelefonoNegocio = config?.Telefono,
            DireccionNegocio = config?.Direccion,
            NumeroPedido = pedido.Numero,
            Modalidad = pedido.Modalidad,
            DireccionEntrega = pedido.DireccionEntrega,
            DistritoEntrega = pedido.DistritoEntrega,
            ReferenciaEntrega = pedido.ReferenciaEntrega,
            LatitudEntrega = pedido.LatitudEntrega,
            LongitudEntrega = pedido.LongitudEntrega,
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
            PublicKeyCulqi = puedePagar ? pagosConfig!.PublicKey : null,
            MotorizadoNombre = pedido.MotorizadoNombre,
            MotorizadoCelular = pedido.MotorizadoCelular,
            PuedeReprogramar = !pedido.Anulado && pedido.EstadoProceso != "ENTREGADO",
            EstadoRuta = estadoRuta,
            RutaIniciadaEn = ruta?.RutaIniciadaEn,
            MotorizadoLat = ruta?.MotorizadoLat,
            MotorizadoLng = ruta?.MotorizadoLng,
            MotorizadoUbicadoEn = ruta?.MotorizadoUbicadoEn,
            DistanciaMetros = distancia,
            EtaMinutos = SeguimientoRutaCalculo.EtaMinutos(distancia)
        });
    }

    /// <summary>Permite al cliente, desde el link público, pedir una nueva fecha/hora de
    /// entrega o recojo sin tener que llamar a la lavandería.</summary>
    [HttpPost("{token:guid}/reprogramar")]
    public async Task<IActionResult> Reprogramar(Guid token, [FromBody] ReprogramarPedidoPublicoRequest req, CancellationToken ct)
    {
        var solicitud = await _pagos.ObtenerPorTokenAsync(token, ct);
        if (solicitud is null) return NotFound(new { mensaje = "Link no encontrado o inválido." });

        var pedido = await _pedidos.ObtenerPorIdAsync(solicitud.PedidoId, solicitud.SedeId, ct);
        if (pedido is null) return NotFound(new { mensaje = "El pedido ya no existe." });

        // El personal puede poner cualquier fecha desde el panel, pero desde el link publico
        // (sin autenticacion) se acota a una ventana razonable para evitar reprogramaciones absurdas.
        if (req.NuevaFecha > DateTime.Now.AddDays(60))
            return BadRequest(new { mensaje = "La fecha elegida es demasiado lejana. Elige una fecha dentro de los próximos 60 días." });

        try
        {
            await _pedidoService.CambiarFechaEntregaAsync(
                pedido.Id,
                new CambiarFechaEntregaRequest { Fecha = req.NuevaFecha, Motivo = "Reprogramado por el cliente desde el portal público" },
                pedido.UsuarioId,
                solicitud.SedeId,
                ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    [HttpPost("{token:guid}/cobrar")]
    [EnableRateLimiting("pago-publico")]
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

        if (!await _pagos.IntentarIniciarCobroAsync(solicitud.Id, ct))
            return Conflict(new { mensaje = "El pago ya está siendo procesado o este link dejó de estar vigente." });

        ResultadoCargoCulqi resultado;
        try
        {
            resultado = await _culqi.CobrarAsync(
                secretKey,
                saldo,
                req.CulqiTokenId,
                req.Email,
                $"Pedido #{pedido.Numero}",
                pedido.ClienteNombre,
                pedido.ClienteCelular,
                pedido.ClienteDni,
                ct);
        }
        catch (Exception ex)
        {
            await _pagos.RestaurarPendienteAsync(solicitud.Id, CancellationToken.None);
            _log.LogError(ex, "Fallo la comunicacion con Culqi para SolicitudPago {SolicitudId}.", solicitud.Id);
            return StatusCode(502, new { mensaje = "No se pudo comunicar con la pasarela de pago. No se realizó ningún cargo; intenta nuevamente." });
        }

        if (!resultado.Exitoso)
        {
            await _pagos.RestaurarPendienteAsync(solicitud.Id, ct);
            return Ok(new CobrarSolicitudPagoResultDto { Exito = false, Mensaje = resultado.Mensaje, SaldoPendiente = saldo });
        }

        var metodoPago = req.CulqiTokenId.StartsWith("ype_", StringComparison.OrdinalIgnoreCase)
            ? "YAPE"
            : "TARJETA";

        // Culqi ya confirmo el cargo. Primero conciliamos pedido/caja y despues cerramos la
        // solicitud. Ante una falla se bloquea cualquier reintento para impedir un doble cargo.
        try
        {
            await _pedidoService.RegistrarPagoAsync(
                pedido.Id,
                new RegistrarPagoRequest { Monto = saldo, Metodo = metodoPago, Descripcion = $"Pago en línea ({metodoPago} / Culqi)" },
                pedido.UsuarioId,
                solicitud.SedeId,
                ct);
        }
        catch (Exception ex)
        {
            await _pagos.MarcarRequiereConciliacionAsync(solicitud.Id, resultado.ChargeId ?? "", CancellationToken.None);
            _log.LogCritical(ex,
                "Culqi cobro OK (ChargeId={ChargeId}, Monto={Monto}) para SolicitudPago {SolicitudId} / Pedido {PedidoId}, " +
                "pero RegistrarPagoAsync fallo. El cliente SI fue cobrado; reconciliar el pedido manualmente en caja.",
                resultado.ChargeId, saldo, solicitud.Id, pedido.Id);
            return Ok(new CobrarSolicitudPagoResultDto
            {
                Exito = true,
                Mensaje = "El pago fue recibido y está pendiente de confirmación en el pedido. No vuelvas a pagarlo; contacta a la lavandería si el estado no cambia.",
                SaldoPendiente = 0
            });
        }

        var marcadoAhora = await _pagos.MarcarPagadoAsync(solicitud.Id, resultado.ChargeId ?? "", ct);
        if (!marcadoAhora)
            _log.LogCritical("Pago conciliado en pedido {PedidoId}, pero no se pudo cerrar SolicitudPago {SolicitudId}.", pedido.Id, solicitud.Id);

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
