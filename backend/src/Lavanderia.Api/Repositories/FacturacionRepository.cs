using Lavanderia.Api.Domain;
using Lavanderia.Api.Infrastructure;
using Microsoft.Data.SqlClient;

namespace Lavanderia.Api.Repositories;

public interface IFacturacionRepository
{
    Task<ConfiguracionFacturacion?> ObtenerConfigAsync(int negocioId, CancellationToken ct = default);
    Task GuardarConfigAsync(ConfiguracionFacturacion c, CancellationToken ct = default);
    Task<int> SiguienteCorrelativoAsync(int negocioId, string tipo, CancellationToken ct = default);
    Task<int> CrearComprobanteAsync(ComprobanteElectronico c, CancellationToken ct = default);
    Task ActualizarResultadoAsync(
        int id, string estado, string? codigoRespuesta, string? descripcionRespuesta,
        byte[]? xmlFirmado, byte[]? cdrZip, string? hashCpe, DateTime? fechaEnvio, CancellationToken ct = default);
    Task<ComprobanteElectronico?> ObtenerPorIdAsync(int id, int sedeId, CancellationToken ct = default);
    Task<(List<ComprobanteElectronico> Items, int Total)> ListarPaginadoAsync(int sedeId, int pagina, int tamanoPagina, CancellationToken ct = default);
}

public class FacturacionRepository : IFacturacionRepository
{
    private readonly ISqlConnectionFactory _factory;
    public FacturacionRepository(ISqlConnectionFactory factory) => _factory = factory;

    private const string ConfigSelect = @"
        SELECT Id, NegocioId, RazonSocial, RucEmisor, Ambiente, SolUsuario, SolClaveCifrada,
               CertificadoPfx, CertificadoPasswordCifrada, SerieBoleta, SerieFactura,
               CorrelativoBoleta, CorrelativoFactura, Activo
        FROM dbo.ConfiguracionFacturacion";

    private static ConfiguracionFacturacion MapConfig(SqlDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")),
        NegocioId = r.GetInt32(r.GetOrdinal("NegocioId")),
        RazonSocial = r.GetNullableString("RazonSocial"),
        RucEmisor = r.GetNullableString("RucEmisor"),
        Ambiente = r.GetString(r.GetOrdinal("Ambiente")),
        SolUsuario = r.GetNullableString("SolUsuario"),
        SolClaveCifrada = r.GetNullableString("SolClaveCifrada"),
        CertificadoPfx = r.IsDBNull(r.GetOrdinal("CertificadoPfx")) ? null : (byte[])r["CertificadoPfx"],
        CertificadoPasswordCifrada = r.GetNullableString("CertificadoPasswordCifrada"),
        SerieBoleta = r.GetString(r.GetOrdinal("SerieBoleta")),
        SerieFactura = r.GetString(r.GetOrdinal("SerieFactura")),
        CorrelativoBoleta = r.GetInt32(r.GetOrdinal("CorrelativoBoleta")),
        CorrelativoFactura = r.GetInt32(r.GetOrdinal("CorrelativoFactura")),
        Activo = r.GetBoolean(r.GetOrdinal("Activo"))
    };

    public async Task<ConfiguracionFacturacion?> ObtenerConfigAsync(int negocioId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = ConfigSelect + " WHERE NegocioId = @NegocioId";
        cmd.AddParam("@NegocioId", negocioId);
        return await cmd.ReadFirstOrDefaultAsync(MapConfig, ct);
    }

    public async Task GuardarConfigAsync(ConfiguracionFacturacion c, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        // Upsert: el negocio puede no tener fila propia todavia (primera vez que configura).
        cmd.CommandText = @"
            MERGE dbo.ConfiguracionFacturacion AS target
            USING (SELECT @NegocioId AS NegocioId) AS src
                ON target.NegocioId = src.NegocioId
            WHEN MATCHED THEN UPDATE SET
                RazonSocial = @RazonSocial,
                RucEmisor = @RucEmisor,
                Ambiente = @Ambiente,
                SolUsuario = @SolUsuario,
                SolClaveCifrada = COALESCE(@SolClaveCifrada, target.SolClaveCifrada),
                CertificadoPfx = COALESCE(@CertificadoPfx, target.CertificadoPfx),
                CertificadoPasswordCifrada = COALESCE(@CertificadoPasswordCifrada, target.CertificadoPasswordCifrada),
                SerieBoleta = @SerieBoleta,
                SerieFactura = @SerieFactura,
                Activo = @Activo,
                FechaActualizacion = SYSDATETIME()
            WHEN NOT MATCHED THEN INSERT
                (NegocioId, RazonSocial, RucEmisor, Ambiente, SolUsuario, SolClaveCifrada,
                 CertificadoPfx, CertificadoPasswordCifrada, SerieBoleta, SerieFactura, Activo)
                VALUES
                (@NegocioId, @RazonSocial, @RucEmisor, @Ambiente, @SolUsuario, @SolClaveCifrada,
                 @CertificadoPfx, @CertificadoPasswordCifrada, @SerieBoleta, @SerieFactura, @Activo);";
        cmd.AddParam("@NegocioId", c.NegocioId);
        cmd.AddParam("@RazonSocial", c.RazonSocial);
        cmd.AddParam("@RucEmisor", c.RucEmisor);
        cmd.AddParam("@Ambiente", c.Ambiente);
        cmd.AddParam("@SolUsuario", c.SolUsuario);
        cmd.AddParam("@SolClaveCifrada", c.SolClaveCifrada);
        cmd.AddParam("@CertificadoPfx", c.CertificadoPfx);
        cmd.AddParam("@CertificadoPasswordCifrada", c.CertificadoPasswordCifrada);
        cmd.AddParam("@SerieBoleta", c.SerieBoleta);
        cmd.AddParam("@SerieFactura", c.SerieFactura);
        cmd.AddParam("@Activo", c.Activo);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> SiguienteCorrelativoAsync(int negocioId, string tipo, CancellationToken ct = default)
    {
        var columna = tipo == "FACTURA" ? "CorrelativoFactura" : "CorrelativoBoleta";
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        // UPDATE ... OUTPUT es atomico: seguro ante emisiones concurrentes del mismo negocio.
        cmd.CommandText = $@"
            UPDATE dbo.ConfiguracionFacturacion
               SET {columna} = {columna} + 1
            OUTPUT INSERTED.{columna}
             WHERE NegocioId = @NegocioId";
        cmd.AddParam("@NegocioId", negocioId);
        return await cmd.ReadScalarAsync<int>(ct);
    }

    public async Task<int> CrearComprobanteAsync(ComprobanteElectronico c, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO dbo.ComprobanteElectronico
                (NegocioId, SedeId, PedidoId, Tipo, Serie, Correlativo, ClienteNombre, ClienteTipoDoc,
                 ClienteNumDoc, OpGravada, Igv, Total, Estado, UsuarioId)
            OUTPUT INSERTED.Id
            VALUES
                (@NegocioId, @SedeId, @PedidoId, @Tipo, @Serie, @Correlativo, @ClienteNombre, @ClienteTipoDoc,
                 @ClienteNumDoc, @OpGravada, @Igv, @Total, @Estado, @UsuarioId);";
        cmd.AddParam("@NegocioId", c.NegocioId);
        cmd.AddParam("@SedeId", c.SedeId);
        cmd.AddParam("@PedidoId", c.PedidoId);
        cmd.AddParam("@Tipo", c.Tipo);
        cmd.AddParam("@Serie", c.Serie);
        cmd.AddParam("@Correlativo", c.Correlativo);
        cmd.AddParam("@ClienteNombre", c.ClienteNombre);
        cmd.AddParam("@ClienteTipoDoc", c.ClienteTipoDoc);
        cmd.AddParam("@ClienteNumDoc", c.ClienteNumDoc);
        cmd.AddParam("@OpGravada", c.OpGravada);
        cmd.AddParam("@Igv", c.Igv);
        cmd.AddParam("@Total", c.Total);
        cmd.AddParam("@Estado", c.Estado);
        cmd.AddParam("@UsuarioId", c.UsuarioId);
        return await cmd.ReadScalarAsync<int>(ct);
    }

    public async Task ActualizarResultadoAsync(
        int id, string estado, string? codigoRespuesta, string? descripcionRespuesta,
        byte[]? xmlFirmado, byte[]? cdrZip, string? hashCpe, DateTime? fechaEnvio, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE dbo.ComprobanteElectronico
               SET Estado = @Estado, CodigoRespuestaSunat = @Codigo, DescripcionRespuestaSunat = @Descripcion,
                   XmlFirmado = @Xml, CdrZip = @Cdr, HashCpe = @Hash, FechaEnvio = @FechaEnvio
             WHERE Id = @Id";
        cmd.AddParam("@Id", id);
        cmd.AddParam("@Estado", estado);
        cmd.AddParam("@Codigo", codigoRespuesta);
        cmd.AddParam("@Descripcion", descripcionRespuesta);
        // AddWithValue no puede inferir el tipo SQL de un byte[] nulo (lo trata como nvarchar,
        // lo que choca contra la columna VARBINARY(MAX)); se tipa explicito para ese caso.
        cmd.Parameters.Add("@Xml", System.Data.SqlDbType.VarBinary).Value = (object?)xmlFirmado ?? DBNull.Value;
        cmd.Parameters.Add("@Cdr", System.Data.SqlDbType.VarBinary).Value = (object?)cdrZip ?? DBNull.Value;
        cmd.AddParam("@Hash", hashCpe);
        cmd.AddParam("@FechaEnvio", fechaEnvio);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private const string ComprobanteSelect = @"
        SELECT c.Id, c.NegocioId, c.SedeId, c.PedidoId, p.Numero AS PedidoNumero, c.Tipo, c.Serie, c.Correlativo,
               c.ClienteNombre, c.ClienteTipoDoc, c.ClienteNumDoc, c.OpGravada, c.Igv, c.Total, c.Estado,
               c.CodigoRespuestaSunat, c.DescripcionRespuestaSunat, c.XmlFirmado, c.CdrZip, c.HashCpe,
               c.FechaEmision, c.FechaEnvio, c.UsuarioId
        FROM dbo.ComprobanteElectronico c
        INNER JOIN dbo.Pedido p ON p.Id = c.PedidoId";

    private static ComprobanteElectronico MapComprobante(SqlDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")),
        NegocioId = r.GetInt32(r.GetOrdinal("NegocioId")),
        SedeId = r.GetInt32(r.GetOrdinal("SedeId")),
        PedidoId = r.GetInt32(r.GetOrdinal("PedidoId")),
        PedidoNumero = r.GetNullableInt("PedidoNumero"),
        Tipo = r.GetString(r.GetOrdinal("Tipo")),
        Serie = r.GetString(r.GetOrdinal("Serie")),
        Correlativo = r.GetInt32(r.GetOrdinal("Correlativo")),
        ClienteNombre = r.GetString(r.GetOrdinal("ClienteNombre")),
        ClienteTipoDoc = r.GetString(r.GetOrdinal("ClienteTipoDoc")),
        ClienteNumDoc = r.GetNullableString("ClienteNumDoc"),
        OpGravada = r.GetDecimal(r.GetOrdinal("OpGravada")),
        Igv = r.GetDecimal(r.GetOrdinal("Igv")),
        Total = r.GetDecimal(r.GetOrdinal("Total")),
        Estado = r.GetString(r.GetOrdinal("Estado")),
        CodigoRespuestaSunat = r.GetNullableString("CodigoRespuestaSunat"),
        DescripcionRespuestaSunat = r.GetNullableString("DescripcionRespuestaSunat"),
        XmlFirmado = r.IsDBNull(r.GetOrdinal("XmlFirmado")) ? null : (byte[])r["XmlFirmado"],
        CdrZip = r.IsDBNull(r.GetOrdinal("CdrZip")) ? null : (byte[])r["CdrZip"],
        HashCpe = r.GetNullableString("HashCpe"),
        FechaEmision = r.GetDateTime(r.GetOrdinal("FechaEmision")),
        FechaEnvio = r.IsDBNull(r.GetOrdinal("FechaEnvio")) ? null : r.GetDateTime(r.GetOrdinal("FechaEnvio")),
        UsuarioId = r.GetInt32(r.GetOrdinal("UsuarioId"))
    };

    public async Task<ComprobanteElectronico?> ObtenerPorIdAsync(int id, int sedeId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = ComprobanteSelect + " WHERE c.Id = @Id AND c.SedeId = @SedeId";
        cmd.AddParam("@Id", id);
        cmd.AddParam("@SedeId", sedeId);
        return await cmd.ReadFirstOrDefaultAsync(MapComprobante, ct);
    }

    public async Task<(List<ComprobanteElectronico> Items, int Total)> ListarPaginadoAsync(
        int sedeId, int pagina, int tamanoPagina, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = ComprobanteSelect + @"
            WHERE c.SedeId = @SedeId
            ORDER BY c.FechaEmision DESC
            OFFSET @Salto ROWS FETCH NEXT @Tamano ROWS ONLY";
        cmd.AddParam("@SedeId", sedeId);
        cmd.AddParam("@Salto", (pagina - 1) * tamanoPagina);
        cmd.AddParam("@Tamano", tamanoPagina);
        var items = await cmd.ReadListAsync(MapComprobante, ct);

        await using var cmdCount = conn.CreateCommand();
        cmdCount.CommandText = "SELECT COUNT(1) FROM dbo.ComprobanteElectronico WHERE SedeId = @SedeId";
        cmdCount.AddParam("@SedeId", sedeId);
        var total = await cmdCount.ReadScalarAsync<int>(ct);

        return (items, total);
    }
}
