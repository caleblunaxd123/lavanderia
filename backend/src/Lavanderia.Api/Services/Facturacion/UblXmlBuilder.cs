using System.Globalization;
using System.Xml.Linq;
using Lavanderia.Api.Domain;

namespace Lavanderia.Api.Services.Facturacion;

/// <summary>
/// Arma el XML UBL 2.1 (Invoice) que exige SUNAT para Boleta (tipo 03) y Factura (tipo 01).
/// El nodo ext:UBLExtensions queda vacío: XmlSigner inserta ahí la firma digital después.
/// </summary>
public static class UblXmlBuilder
{
    private static readonly XNamespace Inv = "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2";
    private static readonly XNamespace Cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    private static readonly XNamespace Cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";
    private static readonly XNamespace Ext = "urn:oasis:names:specification:ubl:schema:xsd:CommonExtensionComponents-2";
    private static readonly XNamespace Ccts = "urn:un:unece:uncefact:documentation:2";
    private static readonly XNamespace Qdt = "urn:oasis:names:specification:ubl:schema:xsd:QualifiedDatatypes-2";
    private static readonly XNamespace Udt = "urn:un:unece:uncefact:data:specification:UnqualifiedDataTypesSchemaModule:2";

    public static XDocument Construir(ComprobanteElectronico c, ConfiguracionFacturacion config, List<PedidoItem> items)
    {
        var tipoDocCodigo = c.Tipo == "FACTURA" ? "01" : "03";
        var ahora = c.FechaEmision == default ? DateTime.Now : c.FechaEmision;
        var lineas = AsignarLineas(c, items);

        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(Inv + "Invoice",
                new XAttribute(XNamespace.Xmlns + "cac", Cac),
                new XAttribute(XNamespace.Xmlns + "cbc", Cbc),
                new XAttribute(XNamespace.Xmlns + "ext", Ext),
                new XAttribute(XNamespace.Xmlns + "ccts", Ccts),
                new XAttribute(XNamespace.Xmlns + "qdt", Qdt),
                new XAttribute(XNamespace.Xmlns + "udt", Udt),

                new XElement(Ext + "UBLExtensions",
                    new XElement(Ext + "UBLExtension",
                        new XElement(Ext + "ExtensionContent"))),

                new XElement(Cbc + "UBLVersionID", "2.1"),
                new XElement(Cbc + "CustomizationID", "2.0"),
                new XElement(Cbc + "ID", $"{c.Serie}-{c.Correlativo}"),
                new XElement(Cbc + "IssueDate", ahora.ToString("yyyy-MM-dd")),
                new XElement(Cbc + "IssueTime", ahora.ToString("HH:mm:ss")),
                new XElement(Cbc + "InvoiceTypeCode",
                    new XAttribute("listID", "0101"),
                    new XAttribute("listAgencyName", "PE:SUNAT"),
                    new XAttribute("listName", "Tipo de Documento"),
                    new XAttribute("listSchemeURI", "urn:pe:gob:sunat:cpe:see:gem:catalogos:catalogo01"),
                    tipoDocCodigo),
                new XElement(Cbc + "Note",
                    new XAttribute("languageLocaleID", "1000"),
                    MontoEnLetras.Convertir(c.Total)),
                new XElement(Cbc + "DocumentCurrencyCode", "PEN"),

                // Firma (referencia); la firma real la agrega XmlSigner sobre ext:ExtensionContent
                new XElement(Cac + "Signature",
                    new XElement(Cbc + "ID", "SignatureSP"),
                    new XElement(Cac + "SignatoryParty",
                        new XElement(Cac + "PartyIdentification",
                            new XElement(Cbc + "ID", config.RucEmisor)),
                        new XElement(Cac + "PartyName",
                            new XElement(Cbc + "Name", config.RazonSocial))),
                    new XElement(Cac + "DigitalSignatureAttachment",
                        new XElement(Cac + "ExternalReference",
                            new XElement(Cbc + "URI", "#SignatureSP")))),

                // Emisor
                new XElement(Cac + "AccountingSupplierParty",
                    new XElement(Cac + "Party",
                        new XElement(Cac + "PartyIdentification",
                            new XElement(Cbc + "ID",
                                new XAttribute("schemeID", "6"),
                                new XAttribute("schemeName", "Documento de Identidad"),
                                new XAttribute("schemeAgencyName", "PE:SUNAT"),
                                new XAttribute("schemeURI", "urn:pe:gob:sunat:cpe:see:gem:catalogos:catalogo06"),
                                config.RucEmisor)),
                        new XElement(Cac + "PartyLegalEntity",
                            new XElement(Cbc + "RegistrationName", config.RazonSocial),
                            new XElement(Cac + "RegistrationAddress",
                                new XElement(Cbc + "ID", "0000"),
                                new XElement(Cbc + "AddressTypeCode", "0000"))))),

                // Receptor (cliente)
                new XElement(Cac + "AccountingCustomerParty",
                    new XElement(Cac + "Party",
                        new XElement(Cac + "PartyIdentification",
                            new XElement(Cbc + "ID",
                                new XAttribute("schemeID", SchemeIdDocumento(c.ClienteTipoDoc)),
                                new XAttribute("schemeName", "Documento de Identidad"),
                                new XAttribute("schemeAgencyName", "PE:SUNAT"),
                                new XAttribute("schemeURI", "urn:pe:gob:sunat:cpe:see:gem:catalogos:catalogo06"),
                                c.ClienteNumDoc ?? "0")),
                        new XElement(Cac + "PartyLegalEntity",
                            new XElement(Cbc + "RegistrationName", c.ClienteNombre)))),

                // Forma de pago: el comprobante solo se emite cuando el pedido ya esta 100% pagado (Contado).
                new XElement(Cac + "PaymentTerms",
                    new XElement(Cbc + "ID", "FormaPago"),
                    new XElement(Cbc + "PaymentMeansID", "Contado")),

                // IGV total
                new XElement(Cac + "TaxTotal",
                    new XElement(Cbc + "TaxAmount", new XAttribute("currencyID", "PEN"), Dec(c.Igv)),
                    new XElement(Cac + "TaxSubtotal",
                        new XElement(Cbc + "TaxableAmount", new XAttribute("currencyID", "PEN"), Dec(c.OpGravada)),
                        new XElement(Cbc + "TaxAmount", new XAttribute("currencyID", "PEN"), Dec(c.Igv)),
                        new XElement(Cac + "TaxCategory",
                            new XElement(Cac + "TaxScheme",
                                new XElement(Cbc + "ID", "1000"),
                                new XElement(Cbc + "Name", "IGV"),
                                new XElement(Cbc + "TaxTypeCode", "VAT"))))),

                // Totales
                new XElement(Cac + "LegalMonetaryTotal",
                    new XElement(Cbc + "LineExtensionAmount", new XAttribute("currencyID", "PEN"), Dec(c.OpGravada)),
                    new XElement(Cbc + "TaxInclusiveAmount", new XAttribute("currencyID", "PEN"), Dec(c.Total)),
                    new XElement(Cbc + "PayableAmount", new XAttribute("currencyID", "PEN"), Dec(c.Total))),

                lineas.Select((l, i) => ConstruirLinea(i + 1, l))));

        return doc;
    }

    private static string SchemeIdDocumento(string tipoDoc) => tipoDoc switch
    {
        "RUC" => "6",
        "DNI" => "1",
        _ => "0"
    };

    private static string Dec(decimal d) => d.ToString("F2", CultureInfo.InvariantCulture);

    private record LineaAsignada(string Descripcion, decimal Cantidad, decimal ValorVentaSinIgv, decimal IgvLinea, decimal TotalConIgv);

    private static List<LineaAsignada> AsignarLineas(ComprobanteElectronico c, List<PedidoItem> items)
    {
        if (items.Count == 0)
        {
            return [new LineaAsignada("Servicio de lavandería", 1, c.OpGravada, c.Igv, c.Total)];
        }

        var rawTotal = items.Sum(i => i.Total);
        var escala = rawTotal == 0 ? 1m : c.Total / rawTotal;

        var resultado = new List<LineaAsignada>();
        decimal sumaTotal = 0, sumaSinIgv = 0, sumaIgv = 0;

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            decimal totalLinea, sinIgvLinea, igvLinea;
            if (i == items.Count - 1)
            {
                // La ultima linea absorbe el remanente para que la suma cuadre exacto con el header.
                totalLinea = c.Total - sumaTotal;
                sinIgvLinea = c.OpGravada - sumaSinIgv;
                igvLinea = c.Igv - sumaIgv;
            }
            else
            {
                totalLinea = Math.Round(item.Total * escala, 2, MidpointRounding.AwayFromZero);
                sinIgvLinea = Math.Round(totalLinea / 1.18m, 2, MidpointRounding.AwayFromZero);
                igvLinea = totalLinea - sinIgvLinea;
            }
            sumaTotal += totalLinea;
            sumaSinIgv += sinIgvLinea;
            sumaIgv += igvLinea;
            resultado.Add(new LineaAsignada(item.ServicioNombre ?? "Servicio", item.Cantidad, sinIgvLinea, igvLinea, totalLinea));
        }
        return resultado;
    }

    private static XElement ConstruirLinea(int numero, LineaAsignada l)
    {
        var precioUnitSinIgv = l.Cantidad == 0 ? 0 : Math.Round(l.ValorVentaSinIgv / l.Cantidad, 2, MidpointRounding.AwayFromZero);
        return new XElement(Cac + "InvoiceLine",
            new XElement(Cbc + "ID", numero),
            new XElement(Cbc + "InvoicedQuantity", new XAttribute("unitCode", "ZZ"), Dec(l.Cantidad)),
            new XElement(Cbc + "LineExtensionAmount", new XAttribute("currencyID", "PEN"), Dec(l.ValorVentaSinIgv)),
            new XElement(Cac + "PricingReference",
                new XElement(Cac + "AlternativeConditionPrice",
                    new XElement(Cbc + "PriceAmount", new XAttribute("currencyID", "PEN"), Dec(l.TotalConIgv)),
                    new XElement(Cbc + "PriceTypeCode", "01"))),
            new XElement(Cac + "TaxTotal",
                new XElement(Cbc + "TaxAmount", new XAttribute("currencyID", "PEN"), Dec(l.IgvLinea)),
                new XElement(Cac + "TaxSubtotal",
                    new XElement(Cbc + "TaxableAmount", new XAttribute("currencyID", "PEN"), Dec(l.ValorVentaSinIgv)),
                    new XElement(Cbc + "TaxAmount", new XAttribute("currencyID", "PEN"), Dec(l.IgvLinea)),
                    new XElement(Cac + "TaxCategory",
                        new XElement(Cbc + "Percent", "18"),
                        new XElement(Cbc + "TaxExemptionReasonCode", "10"),
                        new XElement(Cac + "TaxScheme",
                            new XElement(Cbc + "ID", "1000"),
                            new XElement(Cbc + "Name", "IGV"),
                            new XElement(Cbc + "TaxTypeCode", "VAT"))))),
            new XElement(Cac + "Item",
                new XElement(Cbc + "Description", l.Descripcion)),
            new XElement(Cac + "Price",
                new XElement(Cbc + "PriceAmount", new XAttribute("currencyID", "PEN"), Dec(precioUnitSinIgv))));
    }
}
