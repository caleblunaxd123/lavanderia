using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Lavanderia.Api.Services.Pagos;

public record ResultadoCargoCulqi(bool Exitoso, string? ChargeId, string Mensaje);

public class CulqiService
{
    private readonly HttpClient _http;

    public CulqiService(HttpClient http)
    {
        http.BaseAddress = new Uri("https://api.culqi.com/v2/");
        _http = http;
    }

    public async Task<ResultadoCargoCulqi> CobrarAsync(
        string secretKey,
        decimal montoSoles,
        string culqiTokenId,
        string email,
        string descripcion,
        string? nombreCliente,
        string? celularCliente,
        string? documentoCliente,
        CancellationToken ct = default)
    {
        var nombres = SepararNombre(nombreCliente);
        var body = new
        {
            amount = (int)Math.Round(montoSoles * 100m, MidpointRounding.AwayFromZero),
            currency_code = "PEN",
            email,
            source_id = culqiTokenId,
            description = descripcion,
            antifraud_details = new
            {
                first_name = nombres.firstName,
                last_name = nombres.lastName,
                phone = LimpiarTelefono(celularCliente),
                email
            }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "charges");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secretKey);
        req.Content = JsonContent.Create(body);

        using var resp = await _http.SendAsync(req, ct);
        JsonElement json;
        try
        {
            json = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        }
        catch (JsonException)
        {
            return new ResultadoCargoCulqi(false, null, "Respuesta inválida de la pasarela de pago.");
        }

        if (resp.IsSuccessStatusCode)
        {
            var chargeId = json.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
            return new ResultadoCargoCulqi(true, chargeId, "Pago aprobado.");
        }

        var mensaje = json.TryGetProperty("user_message", out var userMessage) ? userMessage.GetString() : null;
        return new ResultadoCargoCulqi(false, null, mensaje ?? "No se pudo procesar el pago. Intenta con otra tarjeta.");
    }

    private static (string? firstName, string? lastName) SepararNombre(string? nombreCompleto)
    {
        if (string.IsNullOrWhiteSpace(nombreCompleto)) return (null, null);
        var partes = nombreCompleto.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (partes.Length == 1) return (partes[0], null);
        return (partes[0], string.Join(' ', partes.Skip(1)));
    }

    private static string? LimpiarTelefono(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return null;
        var digitos = new string(valor.Where(char.IsDigit).ToArray());
        return string.IsNullOrWhiteSpace(digitos) ? null : digitos;
    }
}
