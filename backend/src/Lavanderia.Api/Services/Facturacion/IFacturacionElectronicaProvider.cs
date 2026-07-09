using System.Text;
using System.Xml;
using Lavanderia.Api.Domain;

namespace Lavanderia.Api.Services.Facturacion;

public record CredencialesEmisor(
    string Ambiente, string RucEmisor, string RazonSocial,
    string SolUsuario, string SolClave, byte[] CertificadoPfx, string CertificadoPassword);

public record SolicitudEmision(ComprobanteElectronico Comprobante, List<PedidoItem> Items, CredencialesEmisor Credenciales, ConfiguracionFacturacion Config);

public record ResultadoEmision(bool Exitoso, string Estado, string? Codigo, string? Descripcion, byte[]? XmlFirmado, byte[]? CdrZip);

/// <summary>
/// Punto de extensión para el envío del comprobante a SUNAT. Hoy solo existe
/// <see cref="SunatDirectoProvider"/> (SEE - Sistema del Contribuyente, sin OSE);
/// un proveedor tipo Nubefact se agregaría implementando esta misma interfaz.
/// </summary>
public interface IFacturacionElectronicaProvider
{
    Task<ResultadoEmision> EmitirAsync(SolicitudEmision solicitud, CancellationToken ct = default);
}

public class SunatDirectoProvider : IFacturacionElectronicaProvider
{
    private readonly SunatSoapClient _soap;
    public SunatDirectoProvider(SunatSoapClient soap) => _soap = soap;

    public async Task<ResultadoEmision> EmitirAsync(SolicitudEmision solicitud, CancellationToken ct = default)
    {
        var (comprobante, items, credenciales, config) = solicitud;

        var xmlDoc = UblXmlBuilder.Construir(comprobante, config, items);
        var xmlBytes = SerializarUtf8(xmlDoc);
        var xmlFirmado = XmlSigner.Firmar(xmlBytes, credenciales.CertificadoPfx, credenciales.CertificadoPassword);

        var tipoDocCodigo = comprobante.Tipo == "FACTURA" ? "01" : "03";
        var nombreArchivo = $"{credenciales.RucEmisor}-{tipoDocCodigo}-{comprobante.Serie}-{comprobante.Correlativo}";

        var resultado = await _soap.EnviarAsync(
            credenciales.Ambiente, credenciales.RucEmisor, credenciales.SolUsuario, credenciales.SolClave,
            nombreArchivo, xmlFirmado, ct);

        var estado = resultado.Exitoso ? "ACEPTADO" : "RECHAZADO";
        return new ResultadoEmision(resultado.Exitoso, estado, resultado.Codigo, resultado.Descripcion, xmlFirmado, resultado.CdrZip);
    }

    private static byte[] SerializarUtf8(System.Xml.Linq.XDocument doc)
    {
        using var ms = new MemoryStream();
        using (var writer = XmlWriter.Create(ms, new XmlWriterSettings { Encoding = new UTF8Encoding(false), OmitXmlDeclaration = false }))
            doc.Save(writer);
        return ms.ToArray();
    }
}
