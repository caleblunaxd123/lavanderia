using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

/// <summary>
/// Genera plantillas .xlsx con estilo (encabezados, instrucciones y filas de ejemplo) para las
/// cargas masivas. Un solo lugar define el formato de cada tipo; el frontend solo descarga.
/// </summary>
[ApiController]
[Route("api/plantillas")]
[Authorize]
public class PlantillasController : ControllerBase
{
    private record Columna(string Titulo, string Ejemplo1, string Ejemplo2, string Nota, double Ancho);
    private record Definicion(string Hoja, string Titulo, string Instruccion, Columna[] Columnas);

    private static readonly Dictionary<string, Definicion> Plantillas = new(StringComparer.OrdinalIgnoreCase)
    {
        ["servicios"] = new(
            "Servicios", "Plantilla de servicios",
            "Completa una fila por servicio. No borres la fila de títulos. Precio en soles (ej. 6.50). Unidad: kg, prenda, pieza o und. La categoría es opcional (se crea sola si no existe).",
            new[]
            {
                new Columna("Nombre", "Lavado por kilo", "Planchado camisa", "Obligatorio", 34),
                new Columna("Precio", "6.50", "3.00", "Obligatorio · en soles", 14),
                new Columna("Unidad", "kg", "prenda", "kg / prenda / pieza / und", 16),
                new Columna("Categoria", "Ropa por kilo", "Adicionales", "Opcional", 26),
            }),
        ["clientes"] = new(
            "Clientes", "Plantilla de clientes",
            "Completa una fila por cliente. No borres la fila de títulos. El celular debe tener 9 dígitos y empezar con 9. El DNI (8 dígitos) y la dirección son opcionales.",
            new[]
            {
                new Columna("Nombre", "Juan Pérez", "María Gómez", "Obligatorio", 32),
                new Columna("Celular", "987654321", "998877665", "9 dígitos", 18),
                new Columna("DNI", "12345678", "", "Opcional · 8 dígitos", 16),
                new Columna("Direccion", "Av. Larco 123, Miraflores", "Jr. Unión 456", "Opcional", 38),
            }),
    };

    [HttpGet("{tipo}")]
    public IActionResult Descargar(string tipo)
    {
        if (!Plantillas.TryGetValue(tipo, out var def))
            return NotFound(new { mensaje = "Plantilla no disponible." });

        var azul = XLColor.FromHtml("#0B57D0");
        var azulClaro = XLColor.FromHtml("#EAF1FF");
        var grisNota = XLColor.FromHtml("#64748B");

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet(def.Hoja);
        var nCols = def.Columnas.Length;

        // Fila 1: título de marca.
        var titulo = ws.Range(1, 1, 1, nCols).Merge();
        titulo.Value = def.Titulo;
        titulo.Style.Font.Bold = true;
        titulo.Style.Font.FontSize = 15;
        titulo.Style.Font.FontColor = XLColor.White;
        titulo.Style.Fill.BackgroundColor = azul;
        titulo.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        titulo.Style.Alignment.Indent = 1;
        ws.Row(1).Height = 26;

        // Fila 2: instrucciones.
        var instr = ws.Range(2, 1, 2, nCols).Merge();
        instr.Value = def.Instruccion;
        instr.Style.Font.FontSize = 10;
        instr.Style.Font.FontColor = grisNota;
        instr.Style.Alignment.WrapText = true;
        instr.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        instr.Style.Alignment.Indent = 1;
        ws.Row(2).Height = 44;

        // Fila 4: encabezados de columna.
        const int filaHeader = 4;
        for (var i = 0; i < nCols; i++)
        {
            var c = ws.Cell(filaHeader, i + 1);
            c.Value = def.Columnas[i].Titulo;
            c.Style.Font.Bold = true;
            c.Style.Font.FontColor = XLColor.White;
            c.Style.Fill.BackgroundColor = azul;
            c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            c.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            c.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            c.Style.Border.OutsideBorderColor = XLColor.White;
            ws.Column(i + 1).Width = def.Columnas[i].Ancho;
        }
        ws.Row(filaHeader).Height = 20;

        // Fila 5: notas de cada columna (qué va en cada una).
        for (var i = 0; i < nCols; i++)
        {
            var c = ws.Cell(filaHeader + 1, i + 1);
            c.Value = def.Columnas[i].Nota;
            c.Style.Font.FontSize = 9;
            c.Style.Font.Italic = true;
            c.Style.Font.FontColor = grisNota;
            c.Style.Fill.BackgroundColor = azulClaro;
            c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        // Filas 6-7: ejemplos (se pueden borrar).
        for (var i = 0; i < nCols; i++)
        {
            ws.Cell(filaHeader + 2, i + 1).Value = def.Columnas[i].Ejemplo1;
            ws.Cell(filaHeader + 3, i + 1).Value = def.Columnas[i].Ejemplo2;
        }
        ws.Range(filaHeader + 2, 1, filaHeader + 3, nCols).Style.Font.FontColor = grisNota;

        var aviso = ws.Cell(filaHeader + 5, 1);
        aviso.Value = "↑ Las dos filas de ejemplo son solo referencia: puedes borrarlas y escribir tus datos debajo del encabezado.";
        aviso.Style.Font.FontSize = 9;
        aviso.Style.Font.Italic = true;
        aviso.Style.Font.FontColor = grisNota;

        ws.SheetView.FreezeRows(filaHeader);
        ws.Range(filaHeader, 1, filaHeader, nCols).SetAutoFilter();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"plantilla-{tipo.ToLowerInvariant()}.xlsx");
    }
}
