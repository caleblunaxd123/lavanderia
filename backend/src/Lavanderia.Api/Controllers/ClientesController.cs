using Lavanderia.Api.Domain;
using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

[Route("api/[controller]")]
[Authorize(Policy = "Modulo:CLIENTES")]
public class ClientesController : TenantAwareControllerBase
{
    private readonly IClienteRepository _repo;

    public ClientesController(IClienteRepository repo) => _repo = repo;

    [HttpGet("analitica")]
    public async Task<ActionResult<List<ClienteAnaliticaDto>>> Analitica(CancellationToken ct)
        => Ok(await _repo.ListarAnaliticaAsync(NegocioId, ct));

    [HttpGet("cumpleanos-proximos")]
    public async Task<ActionResult<List<ClienteCumpleanosDto>>> CumpleanosProximos([FromQuery] int dias = 30, CancellationToken ct = default)
        => Ok(await _repo.ListarCumpleanosProximosAsync(NegocioId, Math.Clamp(dias, 1, 90), ct));

    [HttpGet]
    public async Task<ActionResult<List<ClienteDto>>> Buscar(
        [FromQuery] string? texto,
        [FromQuery] string? campo,
        [FromQuery] int limite = 50,
        CancellationToken ct = default)
    {
        var list = await _repo.BuscarAsync(texto, campo, Math.Clamp(limite, 1, 500), NegocioId, ct);
        return Ok(list.Select(Map).ToList());
    }

    [HttpPost("fusionar")]
    public async Task<IActionResult> Fusionar([FromBody] FusionarClientesRequest req, CancellationToken ct)
    {
        var origen = await _repo.ObtenerPorIdAsync(req.OrigenId, NegocioId, ct);
        var destino = await _repo.ObtenerPorIdAsync(req.DestinoId, NegocioId, ct);
        if (origen is null || destino is null) return NotFound();

        try
        {
            await _repo.FusionarAsync(req.OrigenId, req.DestinoId, NegocioId, ct);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }

        return Ok(new { mensaje = $"Se fusionó \"{origen.Nombre}\" dentro de \"{destino.Nombre}\"." });
    }

    [HttpGet("frecuentes")]
    public async Task<ActionResult<List<ClienteFrecuenteDto>>> Frecuentes(
        [FromQuery] DateTime? desde,
        [FromQuery] DateTime? hasta,
        [FromQuery] int limite = 25,
        CancellationToken ct = default)
    {
        var h = (hasta ?? DateTime.Today).Date.AddDays(1);
        var d = (desde ?? h.AddDays(-31)).Date;
        return Ok(await _repo.ListarFrecuentesAsync(d, h, Math.Clamp(limite, 1, 200), NegocioId, ct));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ClienteDto>> Obtener(int id, CancellationToken ct)
    {
        var c = await _repo.ObtenerPorIdAsync(id, NegocioId, ct);
        if (c is null) return NotFound();
        return Ok(Map(c));
    }

    [HttpPost]
    public async Task<ActionResult<ClienteDto>> Crear([FromBody] ClienteDto dto, CancellationToken ct)
    {
        var nombre = dto.Nombre?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(nombre))
            return BadRequest(new { mensaje = "El nombre del cliente es obligatorio." });

        var celular = LimpiarTexto(dto.Celular);
        var dni = LimpiarTexto(dto.Dni);
        var documentoFiscal = LimpiarTexto(dto.DocumentoFiscal);
        var direccion = LimpiarTexto(dto.Direccion);

        var duplicado = await _repo.BuscarDuplicadoAsync(celular, dni, documentoFiscal, NegocioId, null, ct);
        if (duplicado is not null)
            return Conflict(new { mensaje = MensajeDuplicado(duplicado, celular, dni, documentoFiscal), clienteId = duplicado.Id });

        var id = await _repo.CrearAsync(new Cliente
        {
            NegocioId = NegocioId,
            Nombre = nombre,
            Celular = celular,
            Dni = dni,
            DocumentoFiscal = documentoFiscal,
            Direccion = direccion,
            Puntos = dto.Puntos,
            FechaNacimiento = dto.FechaNacimiento
        }, ct);

        var creado = await _repo.ObtenerPorIdAsync(id, NegocioId, ct);
        return CreatedAtAction(nameof(Obtener), new { id }, Map(creado!));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Actualizar(int id, [FromBody] ClienteDto dto, CancellationToken ct)
    {
        var existente = await _repo.ObtenerPorIdAsync(id, NegocioId, ct);
        if (existente is null) return NotFound();

        var nombre = dto.Nombre?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(nombre))
            return BadRequest(new { mensaje = "El nombre del cliente es obligatorio." });

        var celular = LimpiarTexto(dto.Celular);
        var dni = LimpiarTexto(dto.Dni);
        var documentoFiscal = LimpiarTexto(dto.DocumentoFiscal);
        var duplicado = await _repo.BuscarDuplicadoAsync(celular, dni, documentoFiscal, NegocioId, id, ct);
        if (duplicado is not null)
            return Conflict(new { mensaje = MensajeDuplicado(duplicado, celular, dni, documentoFiscal), clienteId = duplicado.Id });

        existente.Nombre = nombre;
        existente.Celular = celular;
        existente.Dni = dni;
        existente.DocumentoFiscal = documentoFiscal;
        existente.Direccion = LimpiarTexto(dto.Direccion);
        existente.Puntos = dto.Puntos;
        existente.FechaNacimiento = dto.FechaNacimiento;

        await _repo.ActualizarAsync(existente, NegocioId, ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Desactivar(int id, CancellationToken ct)
    {
        var existente = await _repo.ObtenerPorIdAsync(id, NegocioId, ct);
        if (existente is null) return NotFound();

        var pedidos = await _repo.ContarPedidosAsync(id, NegocioId, ct);
        await _repo.DesactivarAsync(id, NegocioId, ct);
        return Ok(new
        {
            mensaje = pedidos > 0
                ? $"Cliente desactivado. Se conservan sus {pedidos} pedidos históricos."
                : "Cliente eliminado.",
            desactivado = true
        });
    }

    [HttpGet("{id:int}/puntos")]
    public async Task<ActionResult<List<MovimientoPuntosDto>>> ListarPuntos(int id, CancellationToken ct)
    {
        var list = await _repo.ListarMovimientosPuntosAsync(id, NegocioId, ct);
        return Ok(list.Select(m => new MovimientoPuntosDto
        {
            Id = m.Id,
            ClienteId = m.ClienteId,
            Fecha = m.Fecha,
            Motivo = m.Motivo,
            Puntos = m.Puntos,
            Tipo = m.Tipo,
            UsuarioNombre = m.UsuarioNombre
        }).ToList());
    }

    [HttpPost("{id:int}/puntos")]
    public async Task<IActionResult> AgregarPuntos(int id, [FromBody] CrearMovimientoPuntosRequest req, CancellationToken ct)
    {
        var cliente = await _repo.ObtenerPorIdAsync(id, NegocioId, ct);
        if (cliente is null) return NotFound();

        var tipo = req.Tipo.ToUpperInvariant();
        if (tipo != "SUMA" && tipo != "RESTA")
            return BadRequest(new { mensaje = "Tipo inválido. Debe ser SUMA o RESTA." });

        await _repo.AgregarMovimientoPuntosAsync(new MovimientoPuntos
        {
            NegocioId = NegocioId,
            ClienteId = id,
            Motivo = req.Motivo.Trim(),
            Puntos = req.Puntos,
            Tipo = tipo,
            UsuarioId = UsuarioId
        }, NegocioId, ct);

        return NoContent();
    }

    private static ClienteDto Map(Cliente c) => new()
    {
        Id = c.Id,
        Nombre = c.Nombre,
        Celular = c.Celular,
        Dni = c.Dni,
        DocumentoFiscal = c.DocumentoFiscal,
        Direccion = c.Direccion,
        Puntos = c.Puntos,
        FechaCreacion = c.FechaCreacion,
        FechaNacimiento = c.FechaNacimiento
    };

    private static string? LimpiarTexto(string? valor)
        => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    private static string MensajeDuplicado(Cliente duplicado, string? celular, string? dni, string? documentoFiscal)
    {
        if (!string.IsNullOrWhiteSpace(celular) && string.Equals(duplicado.Celular, celular, StringComparison.Ordinal))
            return "Ya existe un cliente con ese celular.";
        if (!string.IsNullOrWhiteSpace(dni) && string.Equals(duplicado.Dni, dni, StringComparison.Ordinal))
            return "Ya existe un cliente con ese DNI.";
        if (!string.IsNullOrWhiteSpace(documentoFiscal) && string.Equals(duplicado.DocumentoFiscal, documentoFiscal, StringComparison.Ordinal))
            return "Ya existe un cliente con ese documento fiscal.";
        return "Ya existe un cliente con esos datos.";
    }
}
