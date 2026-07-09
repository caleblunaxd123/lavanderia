using Lavanderia.Api.Domain;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Lavanderia.Api.Services.Facturacion;

/// <summary>Genera la representación impresa (PDF) de una boleta/factura electrónica.</summary>
public class ComprobantePdfGenerator
{
    public byte[] Generar(ComprobanteElectronico comp, ConfiguracionNegocio negocio, List<PedidoItem> items)
    {
        var qrBytes = GenerarQr(comp, negocio);
        var tituloDoc = comp.Tipo == "FACTURA" ? "FACTURA ELECTRÓNICA" : "BOLETA DE VENTA ELECTRÓNICA";

        var documento = Document.Create(contenedor =>
        {
            contenedor.Page(pagina =>
            {
                pagina.Size(PageSizes.A4);
                pagina.Margin(30);
                pagina.DefaultTextStyle(x => x.FontSize(10));

                pagina.Header().Element(c => ComponerEncabezado(c, comp, negocio, tituloDoc));
                pagina.Content().Element(c => ComponerContenido(c, comp, items, qrBytes));
                pagina.Footer().AlignCenter().Text(t =>
                {
                    t.Span("Representación impresa de comprobante electrónico. ").FontSize(8);
                    t.Span(comp.Estado == "ACEPTADO" ? "Aceptado por SUNAT." : $"Estado: {comp.Estado}.").FontSize(8).SemiBold();
                });
            });
        });

        return documento.GeneratePdf();
    }

    private static void ComponerEncabezado(IContainer contenedor, ComprobanteElectronico comp, ConfiguracionNegocio negocio, string tituloDoc)
    {
        contenedor.Row(fila =>
        {
            fila.RelativeItem().Column(col =>
            {
                col.Item().Text(negocio.NombreNegocio).FontSize(14).Bold();
                if (!string.IsNullOrWhiteSpace(negocio.Ruc)) col.Item().Text($"RUC: {negocio.Ruc}");
                if (!string.IsNullOrWhiteSpace(negocio.Direccion)) col.Item().Text(negocio.Direccion!);
                if (!string.IsNullOrWhiteSpace(negocio.Telefono)) col.Item().Text($"Tel: {negocio.Telefono}");
            });

            fila.ConstantItem(200).Border(1).BorderColor(Colors.Grey.Medium).Padding(10).Column(col =>
            {
                col.Item().AlignCenter().Text(tituloDoc).Bold().FontSize(11);
                col.Item().AlignCenter().Text($"{comp.Serie}-{comp.Correlativo}").FontSize(13).Bold();
                col.Item().AlignCenter().Text($"RUC: {negocio.Ruc}").FontSize(9);
            });
        });
    }

    private static void ComponerContenido(IContainer contenedor, ComprobanteElectronico comp, List<PedidoItem> items, byte[] qrBytes)
    {
        contenedor.PaddingTop(15).Column(col =>
        {
            col.Item().Row(fila =>
            {
                fila.RelativeItem().Text(t =>
                {
                    t.Span("Cliente: ").SemiBold();
                    t.Span(comp.ClienteNombre);
                });
                fila.RelativeItem().AlignRight().Text(t =>
                {
                    t.Span($"{DocLabel(comp.ClienteTipoDoc)}: ").SemiBold();
                    t.Span(comp.ClienteNumDoc ?? "-");
                });
            });
            col.Item().PaddingTop(3).Text($"Fecha de emisión: {comp.FechaEmision:dd/MM/yyyy}");

            col.Item().PaddingTop(15).Table(tabla =>
            {
                tabla.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(1);
                    c.RelativeColumn(4);
                    c.RelativeColumn(2);
                    c.RelativeColumn(2);
                });

                tabla.Header(header =>
                {
                    header.Cell().Element(EstiloEncabezadoCelda).Text("Cant.");
                    header.Cell().Element(EstiloEncabezadoCelda).Text("Descripción");
                    header.Cell().Element(EstiloEncabezadoCelda).AlignRight().Text("P. Unit.");
                    header.Cell().Element(EstiloEncabezadoCelda).AlignRight().Text("Importe");
                });

                var lista = items.Count > 0 ? items : [new PedidoItem { ServicioNombre = "Servicio de lavandería", Cantidad = 1, PrecioUnit = comp.Total, Total = comp.Total }];
                foreach (var item in lista)
                {
                    tabla.Cell().Element(EstiloCelda).Text(item.Cantidad.ToString("0.##"));
                    tabla.Cell().Element(EstiloCelda).Text(item.ServicioNombre ?? "Servicio");
                    tabla.Cell().Element(EstiloCelda).AlignRight().Text($"S/ {item.PrecioUnit:0.00}");
                    tabla.Cell().Element(EstiloCelda).AlignRight().Text($"S/ {item.Total:0.00}");
                }
            });

            col.Item().PaddingTop(15).Row(fila =>
            {
                fila.RelativeItem().Height(90).Image(qrBytes);
                fila.RelativeItem().AlignRight().Column(totales =>
                {
                    totales.Item().Row(r => { r.RelativeItem().Text("Op. Gravada:"); r.ConstantItem(90).AlignRight().Text($"S/ {comp.OpGravada:0.00}"); });
                    totales.Item().Row(r => { r.RelativeItem().Text("IGV (18%):"); r.ConstantItem(90).AlignRight().Text($"S/ {comp.Igv:0.00}"); });
                    totales.Item().PaddingTop(3).BorderTop(1).BorderColor(Colors.Grey.Medium).Row(r =>
                    {
                        r.RelativeItem().Text("TOTAL:").Bold();
                        r.ConstantItem(90).AlignRight().Text($"S/ {comp.Total:0.00}").Bold();
                    });
                });
            });
        });
    }

    private static IContainer EstiloEncabezadoCelda(IContainer c) =>
        c.DefaultTextStyle(x => x.SemiBold()).PaddingVertical(5).BorderBottom(1).BorderColor(Colors.Grey.Darken1);

    private static IContainer EstiloCelda(IContainer c) =>
        c.PaddingVertical(4).BorderBottom(1).BorderColor(Colors.Grey.Lighten2);

    private static string DocLabel(string tipoDoc) => tipoDoc switch { "RUC" => "RUC", "DNI" => "DNI", _ => "Doc." };

    private static byte[] GenerarQr(ComprobanteElectronico comp, ConfiguracionNegocio negocio)
    {
        var tipoDocCodigo = comp.Tipo == "FACTURA" ? "01" : "03";
        var tipoDocReceptorCodigo = comp.ClienteTipoDoc switch { "RUC" => "6", "DNI" => "1", _ => "-" };
        var contenido = string.Join("|",
            negocio.Ruc, tipoDocCodigo, comp.Serie, comp.Correlativo,
            comp.Igv.ToString("0.00"), comp.Total.ToString("0.00"),
            comp.FechaEmision.ToString("yyyy-MM-dd"),
            tipoDocReceptorCodigo, comp.ClienteNumDoc ?? "");

        using var generador = new QRCodeGenerator();
        using var datosQr = generador.CreateQrCode(contenido, QRCodeGenerator.ECCLevel.M);
        var pngQr = new PngByteQRCode(datosQr);
        return pngQr.GetGraphic(10);
    }
}
