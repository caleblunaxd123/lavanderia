namespace Lavanderia.Api.Domain;

/// <summary>
/// Reglas puras del ciclo de vida de los comprobantes electrónicos (testeables sin base de datos).
///
/// Modelo de estados:
///  - Estado: PENDIENTE → ACEPTADO | RECHAZADO | ERROR (respuesta de SUNAT/proveedor) y ANULADO cuando el
///    documento quedó sin efecto. SIMULADO para documentos de prueba.
///  - Un documento de venta (BOLETA/FACTURA) se anula de dos maneras:
///      · Nota de crédito aceptada con un motivo que anula la operación (01, 02 o 06). Es la ÚNICA vía para
///        boletas (SUNAT/APISUNAT no aceptan la baja directa de una boleta).
///      · Baja (voidBill) — solo facturas.
///  - Una nota (crédito/débito) o una guía NUNCA se anula: dejaría de nuevo vigente la venta que corrigió.
///  - "Vigente" = documento de venta que aún cuenta para el pedido (no anulado ni rechazado). Las notas y
///    guías no cuentan: de lo contrario bloquearían volver a emitir tras una anulación.
/// </summary>
public static class FacturacionReglas
{
    /// <summary>Motivos del catálogo 09 de SUNAT que dejan sin efecto toda la operación original.</summary>
    public static readonly string[] MotivosNotaCreditoQueAnulan = { "01", "02", "06" };

    /// <summary>Estados de un documento de venta que aún cuenta como "vigente" para su pedido.</summary>
    public static readonly string[] EstadosVigentes = { "PENDIENTE", "ACEPTADO", "ERROR" };

    /// <summary>Estados de una nota de crédito que ya "reserva" el total del documento original.</summary>
    public static readonly string[] EstadosNotaActiva = { "PENDIENTE", "ACEPTADO", "ERROR", "SIMULADO" };

    public static bool EsDocumentoDeVenta(string? tipo) => tipo is "BOLETA" or "FACTURA";

    public static bool EsNota(string? tipo) => tipo is "NOTA_CREDITO" or "NOTA_DEBITO";

    /// <summary>¿Este motivo de nota de crédito deja sin efecto la operación completa?</summary>
    public static bool MotivoAnulaOperacion(string? motivoCodigo)
        => !string.IsNullOrWhiteSpace(motivoCodigo) && MotivosNotaCreditoQueAnulan.Contains(motivoCodigo.Trim());

    /// <summary>
    /// ¿Esta nota, en este estado, deja sin efecto su documento original? Solo una nota de crédito ya
    /// aceptada (o simulada) con motivo de anulación total.
    /// </summary>
    public static bool NotaAnulaOriginal(string tipoNota, string? motivoCodigo, string estadoNota)
        => tipoNota == "NOTA_CREDITO"
           && estadoNota is "ACEPTADO" or "SIMULADO"
           && MotivoAnulaOperacion(motivoCodigo);

    /// <summary>
    /// ¿Se puede dar de baja (anular directamente) este comprobante? Devuelve el mensaje de error o null.
    /// </summary>
    public static string? ValidarBaja(string tipo, string estado, bool esSimulado, string? estadoAnulacion, string? notaCreditoVigente)
    {
        if (esSimulado || estado != "ACEPTADO") return "Solo se puede anular un comprobante real aceptado.";
        if (estadoAnulacion is "PENDIENTE" or "ANULADO") return "El comprobante ya tiene una anulacion en curso o aceptada.";
        if (tipo == "BOLETA")
            return "Una boleta no se anula directamente en SUNAT. Emite una Nota de crédito (motivo: Anulación de la operación).";
        if (EsNota(tipo))
            return "No se anula una nota de crédito/débito: dejaría de nuevo vigente la venta que corrigió.";
        if (tipo != "FACTURA") return "Este tipo de comprobante no se anula.";
        if (!string.IsNullOrEmpty(notaCreditoVigente))
            return $"El comprobante ya tiene la nota de crédito {notaCreditoVigente}; no se anula también por baja.";
        return null;
    }

    /// <summary>
    /// ¿Se puede emitir una nota (crédito/débito) sobre este documento? Devuelve el mensaje de error o null.
    /// <paramref name="totalNotasCreditoActivas"/> es lo ya acreditado por notas de crédito vigentes.
    /// </summary>
    public static string? ValidarNota(string tipoNota, string etiqueta, string tipoOriginal, string estadoOriginal,
        string? estadoAnulacionOriginal, decimal totalOriginal, decimal totalNotasCreditoActivas, decimal totalNota)
    {
        if (!EsDocumentoDeVenta(tipoOriginal)) return $"Solo se emite una {etiqueta} sobre una boleta o factura.";
        if (estadoOriginal != "ACEPTADO") return $"Solo se emite {etiqueta} sobre un comprobante aceptado por SUNAT.";
        if (estadoAnulacionOriginal is "PENDIENTE" or "ANULADO") return "El comprobante ya está anulado o con una anulación en curso.";
        // Una nota de crédito no puede acreditar más que el total del documento: evita acreditar dos veces lo mismo.
        if (tipoNota == "NOTA_CREDITO" && totalNotasCreditoActivas + totalNota > totalOriginal + 0.01m)
            return "Este comprobante ya tiene una nota de crédito vigente (aceptada, pendiente o con error). "
                 + "No se puede acreditar más que su total.";
        return null;
    }

    /// <summary>¿Cuenta como documento de venta vigente para el pedido? (impide emitir otro y editar el pedido)</summary>
    public static bool EsVigente(string tipo, string estado, string? estadoAnulacion)
        => EsDocumentoDeVenta(tipo) && EstadosVigentes.Contains(estado) && estadoAnulacion != "ANULADO";
}
