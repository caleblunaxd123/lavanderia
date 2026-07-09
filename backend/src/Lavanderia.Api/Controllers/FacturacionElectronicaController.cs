using Lavanderia.Api.Domain;
using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;
using Lavanderia.Api.Services.Facturacion;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

[Route("api")]
public class FacturacionElectronicaController : TenantAwareControllerBase
{
    private readonly IFacturacionRepository _facturacion;
    private readonly IConfiguracionNegocioRepository _configNegocio;
    private readonly IPedidoRepository _pedidos;
    private readonly IClienteRepository _clientes;
    private readonly IFacturacionElectronicaProvider _provider;
    private readonly ComprobantePdfGenerator _pdf;
    private readonly SecretProtector _secretos;

    public FacturacionElectronicaController(
        IFacturacionRepository facturacion, IConfiguracionNegocioRepository configNegocio,
        IPedidoRepository pedidos, IClienteRepository clientes,
        IFacturacionElectronicaProvider provider, ComprobantePdfGenerator pdf, SecretProtector secretos)
    {
        _facturacion = facturacion;
        _configNegocio = configNegocio;
        _pedidos = pedidos;
        _clientes = clientes;
        _provider = provider;
        _pdf = pdf;
        _secretos = secretos;
    }

    [HttpGet("facturacion/configuracion")]
    [Authorize(Roles = "ADMIN")]
    public async Task<ActionResult<ConfiguracionFacturacionDto>> ObtenerConfiguracion(CancellationToken ct)
    {
        var c = await _facturacion.ObtenerConfigAsync(NegocioId, ct);
        return Ok(new ConfiguracionFacturacionDto
        {
            RazonSocial = c?.RazonSocial,
            RucEmisor = c?.RucEmisor,
            Ambiente = c?.Ambiente ?? "BETA",
            SolUsuario = c?.SolUsuario,
            SerieBoleta = c?.SerieBoleta ?? "B001",
            SerieFactura = c?.SerieFactura ?? "F001",
            Activo = c?.Activo ?? false,
            TieneCertificado = c?.CertificadoPfx is { Length: > 0 },
            TieneCredencialesSol = !string.IsNullOrEmpty(c?.SolClaveCifrada)
        });
    }

    [HttpPut("facturacion/configuracion")]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> GuardarConfiguracion([FromBody] ConfiguracionFacturacionDto dto, CancellationToken ct)
    {
        var existente = await _facturacion.ObtenerConfigAsync(NegocioId, ct) ?? new ConfiguracionFacturacion { NegocioId = NegocioId };
        existente.RazonSocial = dto.RazonSocial;
        existente.RucEmisor = dto.RucEmisor;
        existente.Ambiente = dto.Ambiente == "PRODUCCION" ? "PRODUCCION" : "BETA";
        existente.SolUsuario = dto.SolUsuario;
        existente.SerieBoleta = string.IsNullOrWhiteSpace(dto.SerieBoleta) ? "B001" : dto.SerieBoleta;
        existente.SerieFactura = string.IsNullOrWhiteSpace(dto.SerieFactura) ? "F001" : dto.SerieFactura;
        existente.Activo = dto.Activo;

        if (!string.IsNullOrWhiteSpace(dto.SolClaveNueva))
            existente.SolClaveCifrada = _secretos.Proteger(dto.SolClaveNueva);
        if (!string.IsNullOrWhiteSpace(dto.CertificadoPfxBase64))
            existente.CertificadoPfx = Convert.FromBase64String(dto.CertificadoPfxBase64);
        if (!string.IsNullOrWhiteSpace(dto.CertificadoPasswordNueva))
            existente.CertificadoPasswordCifrada = _secretos.Proteger(dto.CertificadoPasswordNueva);

        await _facturacion.GuardarConfigAsync(existente, ct);
        return NoContent();
    }

    [HttpPost("pedidos/{pedidoId:int}/comprobante")]
    public async Task<ActionResult<ComprobanteDto>> EmitirComprobante(int pedidoId, [FromBody] EmitirComprobanteRequest req, CancellationToken ct)
    {
        var pedido = await _pedidos.ObtenerPorIdAsync(pedidoId, SedeId!.Value, ct);
        if (pedido is null) return NotFound();
        if (pedido.EstadoPago != "PAGADO")
            return BadRequest(new { mensaje = "El pedido debe estar pagado por completo para emitir el comprobante." });

        var config = await _facturacion.ObtenerConfigAsync(NegocioId, ct);
        if (config is null || !config.Activo || config.CertificadoPfx is null
            || string.IsNullOrEmpty(config.SolClaveCifrada) || string.IsNullOrEmpty(config.CertificadoPasswordCifrada)
            || string.IsNullOrEmpty(config.RucEmisor))
            return BadRequest(new { mensaje = "Configura la facturación electrónica en Ajustes antes de emitir comprobantes." });

        var cliente = await _clientes.ObtenerPorIdAsync(pedido.ClienteId, NegocioId, ct);
        var tipo = string.Equals(req.Tipo, "FACTURA", StringComparison.OrdinalIgnoreCase) ? "FACTURA" : "BOLETA";

        if (tipo == "FACTURA" && string.IsNullOrWhiteSpace(cliente?.DocumentoFiscal))
            return BadRequest(new { mensaje = "El cliente no tiene RUC registrado; no se puede emitir factura." });

        string tipoDocCliente;
        string? numDocCliente;
        string clienteNombre;
        if (tipo == "FACTURA")
        {
            tipoDocCliente = "RUC";
            numDocCliente = cliente!.DocumentoFiscal;
            clienteNombre = cliente.Nombre;
        }
        else if (!string.IsNullOrWhiteSpace(cliente?.Dni))
        {
            tipoDocCliente = "DNI";
            numDocCliente = cliente.Dni;
            clienteNombre = pedido.ClienteNombre ?? cliente.Nombre;
        }
        else
        {
            tipoDocCliente = "SIN_DOC";
            numDocCliente = null;
            clienteNombre = pedido.ClienteNombre ?? cliente?.Nombre ?? "Cliente";
        }

        var opGravada = Math.Round(pedido.Total / 1.18m, 2, MidpointRounding.AwayFromZero);
        var igv = pedido.Total - opGravada;
        var correlativo = await _facturacion.SiguienteCorrelativoAsync(NegocioId, tipo, ct);
        var serie = tipo == "FACTURA" ? config.SerieFactura : config.SerieBoleta;

        var comprobante = new ComprobanteElectronico
        {
            NegocioId = NegocioId,
            SedeId = SedeId!.Value,
            PedidoId = pedido.Id,
            Tipo = tipo,
            Serie = serie,
            Correlativo = correlativo,
            ClienteNombre = clienteNombre,
            ClienteTipoDoc = tipoDocCliente,
            ClienteNumDoc = numDocCliente,
            OpGravada = opGravada,
            Igv = igv,
            Total = pedido.Total,
            Estado = "PENDIENTE",
            FechaEmision = DateTime.Now,
            UsuarioId = UsuarioId
        };
        var id = await _facturacion.CrearComprobanteAsync(comprobante, ct);
        comprobante.Id = id;

        var credenciales = new CredencialesEmisor(
            config.Ambiente, config.RucEmisor!, config.RazonSocial ?? "",
            config.SolUsuario ?? "", _secretos.Desproteger(config.SolClaveCifrada!),
            config.CertificadoPfx!, _secretos.Desproteger(config.CertificadoPasswordCifrada!));

        ResultadoEmision resultado;
        try
        {
            resultado = await _provider.EmitirAsync(new SolicitudEmision(comprobante, pedido.Items, credenciales, config), ct);
        }
        catch (Exception ex)
        {
            resultado = new ResultadoEmision(false, "ERROR", null, ex.Message, null, null);
        }

        await _facturacion.ActualizarResultadoAsync(
            id, resultado.Estado, resultado.Codigo, resultado.Descripcion,
            resultado.XmlFirmado, resultado.CdrZip, null, DateTime.Now, ct);

        var final = await _facturacion.ObtenerPorIdAsync(id, SedeId!.Value, ct);
        return Ok(Map(final!));
    }

    [HttpGet("facturacion/comprobantes")]
    public async Task<ActionResult<PagedResultDto<ComprobanteDto>>> Listar(
        [FromQuery] int pagina = 1, [FromQuery] int tamanoPagina = 15, CancellationToken ct = default)
    {
        pagina = Math.Max(1, pagina);
        tamanoPagina = Math.Clamp(tamanoPagina, 1, 200);
        var (items, total) = await _facturacion.ListarPaginadoAsync(SedeId!.Value, pagina, tamanoPagina, ct);
        return Ok(new PagedResultDto<ComprobanteDto>
        {
            Items = items.Select(Map).ToList(),
            Total = total,
            Pagina = pagina,
            TamanoPagina = tamanoPagina
        });
    }

    [HttpGet("facturacion/comprobantes/{id:int}")]
    public async Task<ActionResult<ComprobanteDto>> Obtener(int id, CancellationToken ct)
    {
        var c = await _facturacion.ObtenerPorIdAsync(id, SedeId!.Value, ct);
        if (c is null) return NotFound();
        return Ok(Map(c));
    }

    [HttpGet("facturacion/comprobantes/{id:int}/pdf")]
    public async Task<IActionResult> Pdf(int id, CancellationToken ct)
    {
        var c = await _facturacion.ObtenerPorIdAsync(id, SedeId!.Value, ct);
        if (c is null) return NotFound();
        var pedido = await _pedidos.ObtenerPorIdAsync(c.PedidoId, SedeId!.Value, ct);
        var negocio = await _configNegocio.ObtenerAsync(NegocioId, ct);
        if (negocio is null) return NotFound(new { mensaje = "Configura primero los datos del negocio en Ajustes." });

        var bytes = _pdf.Generar(c, negocio, pedido?.Items ?? new List<PedidoItem>());
        return File(bytes, "application/pdf", $"{c.Tipo}-{c.Serie}-{c.Correlativo}.pdf");
    }

    [HttpPost("facturacion/comprobantes/{id:int}/anular")]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> Anular(int id, CancellationToken ct)
    {
        var c = await _facturacion.ObtenerPorIdAsync(id, SedeId!.Value, ct);
        if (c is null) return NotFound();
        await _facturacion.ActualizarResultadoAsync(
            id, "ANULADO", c.CodigoRespuestaSunat, "Anulado localmente.",
            c.XmlFirmado, c.CdrZip, c.HashCpe, c.FechaEnvio, ct);
        return Ok(new
        {
            mensaje = "Comprobante marcado como anulado. Nota: esto no envía todavía la Comunicación de Baja " +
                      "(boletas) ni la Nota de Crédito (facturas) a SUNAT — eso queda pendiente como mejora futura."
        });
    }

    private static ComprobanteDto Map(ComprobanteElectronico c) => new()
    {
        Id = c.Id,
        PedidoId = c.PedidoId,
        PedidoNumero = c.PedidoNumero,
        Tipo = c.Tipo,
        Serie = c.Serie,
        Correlativo = c.Correlativo,
        ClienteNombre = c.ClienteNombre,
        ClienteTipoDoc = c.ClienteTipoDoc,
        ClienteNumDoc = c.ClienteNumDoc,
        OpGravada = c.OpGravada,
        Igv = c.Igv,
        Total = c.Total,
        Estado = c.Estado,
        DescripcionRespuestaSunat = c.DescripcionRespuestaSunat,
        FechaEmision = c.FechaEmision
    };
}
