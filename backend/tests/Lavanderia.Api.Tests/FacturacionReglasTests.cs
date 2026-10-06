using Lavanderia.Api.Domain;

namespace Lavanderia.Api.Tests;

/// <summary>Reglas del ciclo de vida de comprobantes: anulación por nota de crédito, bajas y documento vigente.</summary>
public class FacturacionReglasTests
{
    // ---------- Qué notas anulan la operación ----------
    [Theory]
    [InlineData("01", true)]   // anulación de la operación
    [InlineData("02", true)]   // anulación por error en el RUC
    [InlineData("06", true)]   // devolución total
    [InlineData("03", false)]  // corrección de descripción
    [InlineData("04", false)]  // descuento global
    [InlineData("13", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void MotivoAnulaOperacion_SoloLosMotivosDeAnulacionTotal(string? motivo, bool esperado)
        => Assert.Equal(esperado, FacturacionReglas.MotivoAnulaOperacion(motivo));

    [Theory]
    [InlineData("NOTA_CREDITO", "01", "ACEPTADO", true)]
    [InlineData("NOTA_CREDITO", "01", "SIMULADO", true)]
    [InlineData("NOTA_CREDITO", "01", "PENDIENTE", false)]   // aún no aceptada por SUNAT
    [InlineData("NOTA_CREDITO", "01", "RECHAZADO", false)]
    [InlineData("NOTA_CREDITO", "03", "ACEPTADO", false)]    // motivo que no anula
    [InlineData("NOTA_DEBITO", "01", "ACEPTADO", false)]     // una nota de débito nunca anula
    public void NotaAnulaOriginal_SoloNotaDeCreditoAceptadaConMotivoDeAnulacion(string tipo, string motivo, string estado, bool esperado)
        => Assert.Equal(esperado, FacturacionReglas.NotaAnulaOriginal(tipo, motivo, estado));

    // ---------- Baja directa (voidBill) ----------
    [Fact]
    public void ValidarBaja_UnaBoletaNoSeDaDeBaja_SeAnulaConNotaDeCredito()
    {
        var error = FacturacionReglas.ValidarBaja("BOLETA", "ACEPTADO", false, null, null);
        Assert.NotNull(error);
        Assert.Contains("Nota de crédito", error);
    }

    [Theory]
    [InlineData("NOTA_CREDITO")]
    [InlineData("NOTA_DEBITO")]
    public void ValidarBaja_UnaNotaNoSeAnula(string tipo)
        => Assert.NotNull(FacturacionReglas.ValidarBaja(tipo, "ACEPTADO", false, null, null));

    [Fact]
    public void ValidarBaja_FacturaAceptadaSinNotaSePuedeDarDeBaja()
        => Assert.Null(FacturacionReglas.ValidarBaja("FACTURA", "ACEPTADO", false, null, null));

    [Fact]
    public void ValidarBaja_FacturaConNotaDeCreditoVigenteNoSeDaDeBaja()
    {
        var error = FacturacionReglas.ValidarBaja("FACTURA", "ACEPTADO", false, null, "FC01-3");
        Assert.NotNull(error);
        Assert.Contains("FC01-3", error);
    }

    [Theory]
    [InlineData("PENDIENTE")]
    [InlineData("ANULADO")]
    public void ValidarBaja_NoRepiteUnaAnulacionEnCursoOTerminada(string estadoAnulacion)
        => Assert.NotNull(FacturacionReglas.ValidarBaja("FACTURA", "ACEPTADO", false, estadoAnulacion, null));

    [Fact]
    public void ValidarBaja_NoAplicaADocumentosSimuladosNiNoAceptados()
    {
        Assert.NotNull(FacturacionReglas.ValidarBaja("FACTURA", "ACEPTADO", true, null, null));
        Assert.NotNull(FacturacionReglas.ValidarBaja("FACTURA", "PENDIENTE", false, null, null));
        Assert.NotNull(FacturacionReglas.ValidarBaja("FACTURA", "ANULADO", false, null, null));
    }

    // ---------- Emisión de notas ----------
    [Fact]
    public void ValidarNota_NotaDeCreditoSobreBoletaAceptadaEsValida()
        => Assert.Null(FacturacionReglas.ValidarNota("NOTA_CREDITO", "nota de crédito", "BOLETA", "ACEPTADO", null, 28m, 0m, 28m));

    [Fact]
    public void ValidarNota_NoSeAcreditaDosVecesLaMismaVenta()
    {
        // Ya existe una nota por S/ 28 sobre una boleta de S/ 28: otra nota excedería el total.
        var error = FacturacionReglas.ValidarNota("NOTA_CREDITO", "nota de crédito", "BOLETA", "ACEPTADO", null, 28m, 28m, 28m);
        Assert.NotNull(error);
        Assert.Contains("nota de crédito vigente", error);
    }

    [Fact]
    public void ValidarNota_UnaNotaRechazadaNoReservaElTotal_PermiteEmitirOtra()
        // Las notas rechazadas no cuentan en lo acreditado (el repositorio solo suma PENDIENTE/ACEPTADO/ERROR/SIMULADO).
        => Assert.Null(FacturacionReglas.ValidarNota("NOTA_CREDITO", "nota de crédito", "BOLETA", "ACEPTADO", null, 28m, 0m, 28m));

    [Fact]
    public void ValidarNota_NotaDeDebitoNoSeLimitaPorLoAcreditado()
        => Assert.Null(FacturacionReglas.ValidarNota("NOTA_DEBITO", "nota de débito", "FACTURA", "ACEPTADO", null, 100m, 100m, 100m));

    [Theory]
    [InlineData("ANULADO")]
    [InlineData("PENDIENTE")]
    public void ValidarNota_NoSobreUnDocumentoAnuladoOConAnulacionEnCurso(string estadoAnulacion)
        => Assert.NotNull(FacturacionReglas.ValidarNota("NOTA_CREDITO", "nota de crédito", "BOLETA", "ACEPTADO", estadoAnulacion, 28m, 0m, 28m));

    [Theory]
    [InlineData("PENDIENTE")]
    [InlineData("RECHAZADO")]
    [InlineData("ANULADO")]
    public void ValidarNota_SoloSobreComprobanteAceptado(string estado)
        => Assert.NotNull(FacturacionReglas.ValidarNota("NOTA_CREDITO", "nota de crédito", "BOLETA", estado, null, 28m, 0m, 28m));

    [Theory]
    [InlineData("NOTA_CREDITO")]
    [InlineData("NOTA_DEBITO")]
    [InlineData("GUIA_REMISION")]
    public void ValidarNota_SoloSobreBoletaOFactura(string tipoOriginal)
        => Assert.NotNull(FacturacionReglas.ValidarNota("NOTA_CREDITO", "nota de crédito", tipoOriginal, "ACEPTADO", null, 28m, 0m, 28m));

    // ---------- Documento vigente del pedido ----------
    [Theory]
    [InlineData("BOLETA", "ACEPTADO", null, true)]
    [InlineData("FACTURA", "PENDIENTE", null, true)]
    [InlineData("BOLETA", "ERROR", null, true)]
    [InlineData("BOLETA", "ANULADO", "ANULADO", false)]      // anulada por nota de crédito: el pedido queda libre
    [InlineData("BOLETA", "ACEPTADO", "ANULADO", false)]
    [InlineData("BOLETA", "RECHAZADO", null, false)]
    [InlineData("NOTA_CREDITO", "ACEPTADO", null, false)]     // una nota no bloquea volver a emitir
    [InlineData("NOTA_DEBITO", "ACEPTADO", null, false)]
    [InlineData("GUIA_REMISION", "ACEPTADO", null, false)]
    public void EsVigente_SoloElDocumentoDeVentaQueAunCuenta(string tipo, string estado, string? estadoAnulacion, bool esperado)
        => Assert.Equal(esperado, FacturacionReglas.EsVigente(tipo, estado, estadoAnulacion));
}
