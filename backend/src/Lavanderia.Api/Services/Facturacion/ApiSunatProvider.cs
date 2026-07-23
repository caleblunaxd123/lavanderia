using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Microsoft.Extensions.Options;

namespace Lavanderia.Api.Services.Facturacion;

public class ApiSunatOptions
{
    public string BaseUrl { get; set; } = "https://back.apisunat.com";
    /// <summary>Id de la persona/empresa en APISUNAT (lo entrega su panel).</summary>
    public string PersonaId { get; set; } = "";
    /// <summary>Token de la API. Usar primero el de PRUEBAS (DEV) y luego el de producción.</summary>
    public string PersonaToken { get; set; } = "";
}

/// <summary>
/// Proveedor de emisión vía APISUNAT (PSE en la nube): en vez de firmar y presentar el XML a
/// SUNAT nosotros mismos, se envía el comprobante como JSON (formato xml-js "compact", el mismo
/// del botón "Generador JSON" de su panel) y APISUNAT lo firma y lo presenta.
///
/// Reutiliza <see cref="UblXmlBuilder"/> (ya validado contra SUNAT beta) y convierte ese XML a
/// JSON, así el contenido del comprobante es idéntico entre ambos proveedores.
///
/// Activación cuando Mekias entregue los datos:
///   1. appsettings → "FacturacionElectronica": { "Proveedor": "APISUNAT" }
///   2. appsettings → "ApiSunat": { "PersonaId": "...", "PersonaToken": "..." } (token DEV primero)
///   3. Contrastar el JSON generado con el ejemplo del "Generador JSON" y ajustar si difiere.
/// </summary>
public class ApiSunatProvider : IFacturacionElectronicaProvider
{
    private readonly HttpClient _http;
    private readonly ApiSunatOptions _opciones;

    public ApiSunatProvider(HttpClient http, IOptions<ApiSunatOptions> opciones)
    {
        _http = http;
        _opciones = opciones.Value;
    }

    public async Task<ResultadoEmision> EmitirAsync(SolicitudEmision solicitud, CancellationToken ct = default)
    {
        var (comprobante, items, credenciales, config) = solicitud;

        if (string.IsNullOrWhiteSpace(_opciones.PersonaId) || string.IsNullOrWhiteSpace(_opciones.PersonaToken))
            return new ResultadoEmision(false, "RECHAZADO", "CONFIG",
                "Faltan las credenciales de APISUNAT (ApiSunat:PersonaId / ApiSunat:PersonaToken).", null, null);

        // Mismo contenido UBL 2.1 que el envío directo; APISUNAT se encarga de la firma.
        var xmlDoc = UblXmlBuilder.Construir(comprobante, config, items);
        var documentBody = XmlJsCompact.Convertir(xmlDoc);

        var tipoDocCodigo = comprobante.Tipo == "FACTURA" ? "01" : "03";
        var fileName = $"{credenciales.RucEmisor}-{tipoDocCodigo}-{comprobante.Serie}-{comprobante.Correlativo}";

        var payload = new JsonObject
        {
            ["personaId"] = _opciones.PersonaId,
            ["personaToken"] = _opciones.PersonaToken,
            ["fileName"] = fileName,
            ["documentBody"] = documentBody
        };

        using var contenido = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        using var respuesta = await _http.PostAsync($"{_opciones.BaseUrl.TrimEnd('/')}/personas/v1/sendBill", contenido, ct);
        var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);

        if (!respuesta.IsSuccessStatusCode)
            return new ResultadoEmision(false, "RECHAZADO", ((int)respuesta.StatusCode).ToString(),
                $"APISUNAT respondió {(int)respuesta.StatusCode}: {Recortar(cuerpo)}", null, null);

        // APISUNAT procesa en cola: la respuesta inmediata trae documentId + estado inicial.
        string? documentId = null;
        string? status = null;
        try
        {
            var json = JsonNode.Parse(cuerpo);
            documentId = json?["documentId"]?.GetValue<string>();
            status = json?["status"]?.GetValue<string>();
        }
        catch { /* respuesta no-JSON: se conserva el cuerpo crudo en la descripción */ }

        var estado = string.Equals(status, "ACEPTADO", StringComparison.OrdinalIgnoreCase) ? "ACEPTADO" : "PENDIENTE";
        return new ResultadoEmision(true, estado, status,
            $"APISUNAT {status ?? "OK"} (documentId: {documentId ?? "?"})", null, null);
    }

    private static string Recortar(string s) => s.Length <= 300 ? s : s[..300] + "…";
}

/// <summary>
/// Convierte un XDocument al formato JSON "compact" de xml-js (el que APISUNAT usa como
/// documentBody): elementos como objetos, atributos en "_attributes", texto en "_text" y
/// elementos repetidos como arreglos.
/// </summary>
public static class XmlJsCompact
{
    public static JsonObject Convertir(XDocument doc)
    {
        var raiz = new JsonObject
        {
            ["_declaration"] = new JsonObject
            {
                ["_attributes"] = new JsonObject
                {
                    ["version"] = "1.0",
                    ["encoding"] = "UTF-8",
                    ["standalone"] = "no"
                }
            }
        };
        if (doc.Root is not null)
            raiz[Nombre(doc.Root)] = ConvertirElemento(doc.Root);
        return raiz;
    }

    private static string Nombre(XElement el)
    {
        var prefijo = el.GetPrefixOfNamespace(el.Name.Namespace);
        return string.IsNullOrEmpty(prefijo) ? el.Name.LocalName : $"{prefijo}:{el.Name.LocalName}";
    }

    private static JsonNode ConvertirElemento(XElement el)
    {
        var obj = new JsonObject();

        var atributos = new JsonObject();
        foreach (var atr in el.Attributes())
        {
            string nombre;
            if (atr.IsNamespaceDeclaration)
                nombre = atr.Name.LocalName == "xmlns" ? "xmlns" : $"xmlns:{atr.Name.LocalName}";
            else if (atr.Name.Namespace == XNamespace.None)
                nombre = atr.Name.LocalName;
            else
                nombre = $"{el.GetPrefixOfNamespace(atr.Name.Namespace)}:{atr.Name.LocalName}";
            atributos[nombre] = atr.Value;
        }
        if (atributos.Count > 0) obj["_attributes"] = atributos;

        var hijos = el.Elements().ToList();
        if (hijos.Count == 0)
        {
            if (el.Value.Length > 0) obj["_text"] = el.Value;
            return obj;
        }

        foreach (var grupo in hijos.GroupBy(Nombre))
        {
            var convertidos = grupo.Select(ConvertirElemento).ToList();
            if (convertidos.Count == 1)
            {
                obj[grupo.Key] = convertidos[0];
            }
            else
            {
                var arr = new JsonArray();
                foreach (var c in convertidos) arr.Add(c);
                obj[grupo.Key] = arr;
            }
        }
        return obj;
    }
}
