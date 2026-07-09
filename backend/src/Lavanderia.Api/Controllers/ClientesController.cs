using Lavanderia.Api.Domain;
using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

[Route("api/[controller]")]
public class ClientesController : TenantAwareControllerBase
{
    private readonly IClienteRepository _repo;

    public ClientesController(IClienteRepository repo) => _repo = repo;

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
        if (!string.IsNullOrWhiteSpace(dto.Celular))
        {
            var existe = await _repo.BuscarPorCelularOrDniAsync(dto.Celular, NegocioId, ct);
            if (existe is not null)
                return Conflict(new { mensaje = "Ya existe un cliente con ese celular.", clienteId = existe.Id });
        }

        var id = await _repo.CrearAsync(new Cliente
        {
            NegocioId = NegocioId,
            Nombre = dto.Nombre.Trim(),
            Celular = dto.Celular,
            Dni = dto.Dni,
            DocumentoFiscal = dto.DocumentoFiscal,
            Direccion = dto.Direccion,
            Puntos = dto.Puntos
        }, ct);

        var creado = await _repo.ObtenerPorIdAsync(id, NegocioId, ct);
        return CreatedAtAction(nameof(Obtener), new { id }, Map(creado!));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Actualizar(int id, [FromBody] ClienteDto dto, CancellationToken ct)
    {
        var existente = await _repo.ObtenerPorIdAsync(id, NegocioId, ct);
        if (existente is null) return NotFound();

        existente.Nombre = dto.Nombre.Trim();
        existente.Celular = dto.Celular;
        existente.Dni = dto.Dni;
        existente.DocumentoFiscal = dto.DocumentoFiscal;
        existente.Direccion = dto.Direccion;
        existente.Puntos = dto.Puntos;

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
        FechaCreacion = c.FechaCreacion
    };
}
