using Lavanderia.Api.Domain;
using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;

namespace Lavanderia.Api.Services;

public interface IPedidoService
{
    Task<PedidoDto> CrearAsync(CrearPedidoRequest req, int usuarioId, int negocioId, int sedeId, CancellationToken ct = default);
    Task<PedidoDto?> ObtenerAsync(int id, int sedeId, CancellationToken ct = default);
    Task<PagedResultDto<PedidoDto>> ListarPaginadoAsync(string? filtro, string? busqueda, int pagina, int tamanoPagina, int sedeId, CancellationToken ct = default);
    Task<PagedResultDto<PedidoDto>> ListarPorClienteAsync(int clienteId, string? filtro, int pagina, int tamanoPagina, int sedeId, CancellationToken ct = default);
    Task AvanzarAreaAsync(int pedidoId, AvanzarAreaRequest req, int usuarioId, int sedeId, CancellationToken ct = default);
    Task<List<PedidoHistorialDto>> ObtenerHistorialAsync(int pedidoId, int sedeId, CancellationToken ct = default);
    Task AvanzarSiguienteAreaAsync(int pedidoId, int usuarioId, int sedeId, string? recibidoPor = null, CancellationToken ct = default);
    Task<DashboardDto> DashboardAsync(int sedeId, CancellationToken ct = default);
    Task RegistrarPagoAsync(int pedidoId, RegistrarPagoRequest req, int usuarioId, int sedeId, CancellationToken ct = default);
    Task AgregarItemAsync(int pedidoId, AgregarItemRequest req, int negocioId, int sedeId, CancellationToken ct = default);
    Task AnularAsync(int pedidoId, string motivo, int usuarioId, int sedeId, CancellationToken ct = default);
    Task CambiarFechaEntregaAsync(int pedidoId, CambiarFechaEntregaRequest req, int usuarioId, int sedeId, CancellationToken ct = default);
    Task<List<PedidoAbandonadoDto>> ListarAbandonadosAsync(int diasMinimo, int sedeId, CancellationToken ct = default);
    Task<int> SiguienteNumeroAsync(int sedeId, CancellationToken ct = default);
    Task ConvertirADeliveryAsync(int pedidoId, int negocioId, int sedeId, CancellationToken ct = default);
    Task<Guid?> ObtenerOCrearLinkPagoAsync(int pedidoId, int negocioId, int sedeId, CancellationToken ct = default);
}

public class PedidoService : IPedidoService
{
    private static readonly string[] ModalidadesValidas = ["Tienda", "Recojo", "Delivery"];
    private readonly IPedidoRepository _pedidos;
    private readonly IClienteRepository _clientes;
    private readonly IServicioRepository _servicios;
    private readonly IAreaLavadoRepository _areas;
    private readonly IPagosRepository _pagos;

    public PedidoService(
        IPedidoRepository pedidos,
        IClienteRepository clientes,
        IServicioRepository servicios,
        IAreaLavadoRepository areas,
        IPagosRepository pagos)
    {
        _pedidos = pedidos;
        _clientes = clientes;
        _servicios = servicios;
        _areas = areas;
        _pagos = pagos;
    }

    public async Task<PedidoDto> CrearAsync(CrearPedidoRequest req, int usuarioId, int negocioId, int sedeId, CancellationToken ct = default)
    {
        var metodosValidos = new[] { "EFECTIVO", "YAPE", "PLIN", "TRANSFERENCIA", "POS", "TARJETA" };
        if (req.MontoPagado > 0 && !metodosValidos.Contains(req.MetodoPagoInicial.ToUpperInvariant()))
            throw new InvalidOperationException("Método de pago inválido.");

        var modalidad = NormalizarModalidad(req.Modalidad);
        if (!ModalidadesValidas.Contains(modalidad))
            throw new InvalidOperationException("Modalidad inválida.");

        int clienteId;
        if (req.ClienteId is int id && id > 0)
        {
            var cliente = await _clientes.ObtenerPorIdAsync(id, negocioId, ct)
                ?? throw new InvalidOperationException("El cliente no existe en este negocio.");

            var snapshot = req.ClienteNuevo;
            if (snapshot is not null)
            {
                cliente.Nombre = string.IsNullOrWhiteSpace(snapshot.Nombre) ? cliente.Nombre : snapshot.Nombre.Trim();
                cliente.Celular = LimpiarTexto(snapshot.Celular) ?? cliente.Celular;
                cliente.Dni = LimpiarTexto(snapshot.Dni) ?? cliente.Dni;
                cliente.DocumentoFiscal = LimpiarTexto(snapshot.DocumentoFiscal) ?? cliente.DocumentoFiscal;
                cliente.Direccion = LimpiarTexto(snapshot.Direccion) ?? cliente.Direccion;
                await _clientes.ActualizarAsync(cliente, negocioId, ct);
            }

            ValidarDatosDomicilio(cliente.Celular, cliente.Direccion, modalidad);
            clienteId = cliente.Id;
        }
        else if (req.ClienteNuevo is { } nuevo && !string.IsNullOrWhiteSpace(nuevo.Nombre))
        {
            ValidarDatosDomicilio(nuevo.Celular, nuevo.Direccion, modalidad);
            clienteId = await _clientes.CrearAsync(new Cliente
            {
                NegocioId = negocioId,
                Nombre = nuevo.Nombre.Trim(),
                Celular = LimpiarTexto(nuevo.Celular),
                Dni = LimpiarTexto(nuevo.Dni),
                DocumentoFiscal = LimpiarTexto(nuevo.DocumentoFiscal),
                Direccion = LimpiarTexto(nuevo.Direccion)
            }, ct);
        }
        else
        {
            throw new InvalidOperationException("Debe indicar un cliente existente o los datos del cliente nuevo.");
        }

        var itemsPersistir = new List<PedidoItem>();
        decimal subtotal = 0m;
        foreach (var it in req.Items)
        {
            var servicio = await _servicios.ObtenerPorIdAsync(it.ServicioId, negocioId, ct)
                ?? throw new InvalidOperationException($"Servicio {it.ServicioId} no existe.");
            var totalItem = Math.Round(servicio.Precio * it.Cantidad, 2);
            subtotal += totalItem;
            itemsPersistir.Add(new PedidoItem
            {
                ServicioId = servicio.Id,
                Cantidad = it.Cantidad,
                PrecioUnit = servicio.Precio,
                Total = totalItem,
                Descripcion = it.Descripcion
            });
        }

        var descuento = Math.Round(subtotal * (req.DescuentoPct / 100m), 2);
        var recargoUrgente = req.EsUrgente ? Math.Round(subtotal * (req.RecargoUrgentePct / 100m), 2) : 0m;
        var totalSinRedondear = Math.Max(0m, subtotal - descuento + recargoUrgente);
        var total = Math.Round(totalSinRedondear * 10m, MidpointRounding.AwayFromZero) / 10m;
        var redondeo = total - totalSinRedondear;
        if (req.MontoPagado < 0)
            throw new InvalidOperationException("El monto pagado no puede ser negativo.");
        if (req.MontoPagado > total + 0.01m)
            throw new InvalidOperationException($"El monto pagado excede el total del pedido (S/ {total:F2}).");
        var estadoPago = req.MontoPagado <= 0 ? "PENDIENTE" : req.MontoPagado >= total ? "PAGADO" : "PARCIAL";

        int? areaId = req.AreaInicialId;
        if (areaId is int areaInicialId)
        {
            var area = await _areas.ObtenerPorIdAsync(areaInicialId, sedeId, ct)
                ?? throw new InvalidOperationException("El área inicial no pertenece a esta sede.");
            if (!area.Activa)
                throw new InvalidOperationException("El área inicial no está activa.");
        }
        else
        {
            var areas = await _areas.ListarActivasAsync(sedeId, ct);
            areaId = areas.OrderBy(a => a.Orden).FirstOrDefault()?.Id;
        }

        var numero = await _pedidos.SiguienteNumeroAsync(sedeId, ct);

        var pedido = new Pedido
        {
            SedeId = sedeId,
            Numero = numero,
            ClienteId = clienteId,
            UsuarioId = usuarioId,
            FechaIngreso = req.FechaIngreso ?? DateTime.Now,
            FechaEntregaEst = req.FechaEntregaEst,
            Modalidad = modalidad,
            Subtotal = subtotal,
            Descuento = descuento,
            EsUrgente = req.EsUrgente,
            RecargoUrgente = recargoUrgente,
            Redondeo = redondeo,
            Total = total,
            MontoPagado = req.MontoPagado,
            MetodoPagoInicial = req.MontoPagado > 0 ? req.MetodoPagoInicial.ToUpperInvariant() : "EFECTIVO",
            EstadoPago = estadoPago,
            EstadoProceso = "PENDIENTE",
            AreaActualId = areaId,
            Observaciones = req.Observaciones,
            CodigoAntiguo = req.CodigoAntiguo,
            Items = itemsPersistir
        };

        var pedidoId = await _pedidos.CrearAsync(pedido, ct);

        if (EsPedidoDomicilio(pedido.Modalidad))
            await AsegurarLinkPagoAsync(pedidoId, pedido, negocioId, sedeId, ct);

        return (await ObtenerAsync(pedidoId, sedeId, ct))!;
    }

    public async Task ConvertirADeliveryAsync(int pedidoId, int negocioId, int sedeId, CancellationToken ct = default)
    {
        var pedido = await _pedidos.ObtenerPorIdAsync(pedidoId, sedeId, ct)
            ?? throw new InvalidOperationException("Pedido no encontrado.");
        if (pedido.Anulado)
            throw new InvalidOperationException("El pedido está anulado.");

        if (pedido.Modalidad != "Delivery")
        {
            var actualizado = await _pedidos.CambiarModalidadAsync(pedidoId, "Delivery", sedeId, ct);
            if (!actualizado) throw new InvalidOperationException("Pedido no encontrado.");
            pedido.Modalidad = "Delivery";
        }

        await AsegurarLinkPagoAsync(pedidoId, pedido, negocioId, sedeId, ct);
    }

    public async Task<Guid?> ObtenerOCrearLinkPagoAsync(int pedidoId, int negocioId, int sedeId, CancellationToken ct = default)
    {
        var pedido = await _pedidos.ObtenerPorIdAsync(pedidoId, sedeId, ct)
            ?? throw new InvalidOperationException("Pedido no encontrado.");
        if (!EsPedidoDomicilio(pedido.Modalidad))
            throw new InvalidOperationException("Este pedido no tiene seguimiento público porque se entrega en tienda.");

        return await AsegurarLinkPagoAsync(pedidoId, pedido, negocioId, sedeId, ct);
    }

    private async Task<Guid?> AsegurarLinkPagoAsync(int pedidoId, Pedido pedido, int negocioId, int sedeId, CancellationToken ct)
    {
        if (pedido.Anulado || !EsPedidoDomicilio(pedido.Modalidad)) return null;
        var saldo = Math.Max(0m, pedido.Total - pedido.MontoPagado);

        var vigente = await _pagos.ObtenerVigentePorPedidoAsync(pedidoId, ct);
        if (vigente is not null) return vigente.Token;

        var nueva = await _pagos.CrearSolicitudAsync(negocioId, sedeId, pedidoId, saldo, ct);
        return nueva.Token;
    }

    public async Task<PedidoDto?> ObtenerAsync(int id, int sedeId, CancellationToken ct = default)
    {
        var p = await _pedidos.ObtenerPorIdAsync(id, sedeId, ct);
        return p == null ? null : Map(p);
    }

    public async Task<PagedResultDto<PedidoDto>> ListarPaginadoAsync(string? filtro, string? busqueda, int pagina, int tamanoPagina, int sedeId, CancellationToken ct = default)
    {
        var (items, total) = await _pedidos.ListarPaginadoAsync(filtro, busqueda, pagina, tamanoPagina, sedeId, ct);
        return new PagedResultDto<PedidoDto>
        {
            Items = items.Select(Map).ToList(),
            Total = total,
            Pagina = pagina,
            TamanoPagina = tamanoPagina
        };
    }

    public async Task<PagedResultDto<PedidoDto>> ListarPorClienteAsync(int clienteId, string? filtro, int pagina, int tamanoPagina, int sedeId, CancellationToken ct = default)
    {
        var (items, total) = await _pedidos.ListarPorClienteAsync(clienteId, filtro, pagina, tamanoPagina, sedeId, ct);
        return new PagedResultDto<PedidoDto>
        {
            Items = items.Select(Map).ToList(),
            Total = total,
            Pagina = pagina,
            TamanoPagina = tamanoPagina
        };
    }

    public async Task AvanzarAreaAsync(int pedidoId, AvanzarAreaRequest req, int usuarioId, int sedeId, CancellationToken ct = default)
    {
        var estadosValidos = new[] { "PENDIENTE", "EN_PROCESO", "LISTO", "ENTREGADO", "ANULADO" };
        if (!estadosValidos.Contains(req.NuevoEstado))
            throw new InvalidOperationException($"Estado invalido: {req.NuevoEstado}");
        if (req.NuevaAreaId is int areaId)
        {
            var area = await _areas.ObtenerPorIdAsync(areaId, sedeId, ct)
                ?? throw new InvalidOperationException("El área no pertenece a esta sede.");
            if (!area.Activa)
                throw new InvalidOperationException("El área no está activa.");
        }
        await _pedidos.AvanzarAreaAsync(pedidoId, req.NuevaAreaId, req.NuevoEstado, usuarioId, req.Nota, sedeId, ct);
    }

    public async Task<List<PedidoHistorialDto>> ObtenerHistorialAsync(int pedidoId, int sedeId, CancellationToken ct = default)
    {
        var lista = await _pedidos.ObtenerHistorialAsync(pedidoId, sedeId, ct);
        return lista.Select(h => new PedidoHistorialDto
        {
            Id = h.Id,
            AreaId = h.AreaId,
            AreaNombre = h.AreaNombre,
            EstadoProceso = h.EstadoProceso,
            Fecha = h.Fecha,
            Nota = h.Nota,
            NotificadoWsp = h.NotificadoWsp
        }).ToList();
    }

    public async Task AvanzarSiguienteAreaAsync(int pedidoId, int usuarioId, int sedeId, string? recibidoPor = null, CancellationToken ct = default)
    {
        var pedido = await _pedidos.ObtenerPorIdAsync(pedidoId, sedeId, ct)
            ?? throw new InvalidOperationException("Pedido no encontrado.");

        if (pedido.Anulado)
            throw new InvalidOperationException("El pedido está anulado.");
        if (pedido.EstadoProceso == "ENTREGADO")
            throw new InvalidOperationException("El pedido ya fue entregado.");

        var areas = (await _areas.ListarActivasAsync(sedeId, ct)).OrderBy(a => a.Orden).ToList();
        if (areas.Count == 0)
            throw new InvalidOperationException("No hay áreas de lavado configuradas.");

        int? proximaAreaId;
        string proximoEstado;
        string nota;

        if (pedido.EstadoProceso == "LISTO")
        {
            if (pedido.MontoPagado + 0.01m < pedido.Total)
                throw new InvalidOperationException(
                    $"El pedido tiene saldo pendiente de S/ {pedido.Total - pedido.MontoPagado:F2}. Registra el pago antes de entregar.");

            proximaAreaId = pedido.AreaActualId;
            proximoEstado = "ENTREGADO";
            nota = !string.IsNullOrWhiteSpace(recibidoPor)
                ? $"Entregado a {recibidoPor.Trim()} (recogido por tercero, no el titular)"
                : "Entregado al cliente";
        }
        else if (pedido.AreaActualId is null)
        {
            proximaAreaId = areas[0].Id;
            proximoEstado = "EN_PROCESO";
            nota = $"Ingresa a: {areas[0].Nombre}";
        }
        else
        {
            var idxActual = areas.FindIndex(a => a.Id == pedido.AreaActualId);

            if (idxActual == -1 || idxActual == areas.Count - 1)
            {
                proximaAreaId = pedido.AreaActualId;
                proximoEstado = "LISTO";
                nota = RequiereEntregaDomicilio(pedido.Modalidad) ? "Listo para salir a ruta" : "Listo para recojo";
            }
            else
            {
                var siguiente = areas[idxActual + 1];
                proximaAreaId = siguiente.Id;
                proximoEstado = "EN_PROCESO";
                nota = $"Avanza a: {siguiente.Nombre}";
            }
        }

        await _pedidos.AvanzarAreaAsync(pedidoId, proximaAreaId, proximoEstado, usuarioId, nota, sedeId, ct);
    }

    public async Task RegistrarPagoAsync(int pedidoId, RegistrarPagoRequest req, int usuarioId, int sedeId, CancellationToken ct = default)
    {
        var pedido = await _pedidos.ObtenerPorIdAsync(pedidoId, sedeId, ct)
            ?? throw new InvalidOperationException("Pedido no encontrado.");

        if (pedido.Anulado)
            throw new InvalidOperationException("El pedido está anulado.");

        var saldo = pedido.Total - pedido.MontoPagado;
        if (req.Monto <= 0)
            throw new InvalidOperationException("El monto debe ser mayor a 0.");
        if (req.Monto > saldo + 0.01m)
            throw new InvalidOperationException($"El monto excede el saldo pendiente (S/ {saldo:F2}).");

        var metodosValidos = new[] { "EFECTIVO", "YAPE", "PLIN", "TRANSFERENCIA", "POS", "TARJETA" };
        if (!metodosValidos.Contains(req.Metodo.ToUpperInvariant()))
            throw new InvalidOperationException("Método de pago inválido.");

        await _pedidos.RegistrarPagoAsync(pedidoId, req.Monto, req.Metodo.ToUpperInvariant(), usuarioId, req.Descripcion, sedeId, ct);
    }

    public async Task AgregarItemAsync(int pedidoId, AgregarItemRequest req, int negocioId, int sedeId, CancellationToken ct = default)
    {
        var pedido = await _pedidos.ObtenerPorIdAsync(pedidoId, sedeId, ct)
            ?? throw new InvalidOperationException("Pedido no encontrado.");
        if (pedido.Anulado)
            throw new InvalidOperationException("El pedido está anulado.");
        if (pedido.EstadoProceso == "ENTREGADO")
            throw new InvalidOperationException("El pedido ya fue entregado, no puedes agregarle ítems.");

        var servicio = await _servicios.ObtenerPorIdAsync(req.ServicioId, negocioId, ct)
            ?? throw new InvalidOperationException($"Servicio {req.ServicioId} no existe.");

        var totalItem = Math.Round(servicio.Precio * req.Cantidad, 2);

        await _pedidos.AgregarItemAsync(pedidoId, new PedidoItem
        {
            ServicioId = servicio.Id,
            Cantidad = req.Cantidad,
            PrecioUnit = servicio.Precio,
            Total = totalItem,
            Descripcion = req.Descripcion
        }, sedeId, ct);
    }

    public async Task CambiarFechaEntregaAsync(int pedidoId, CambiarFechaEntregaRequest req, int usuarioId, int sedeId, CancellationToken ct = default)
    {
        var pedido = await _pedidos.ObtenerPorIdAsync(pedidoId, sedeId, ct)
            ?? throw new InvalidOperationException("Pedido no encontrado.");
        if (pedido.Anulado)
            throw new InvalidOperationException("El pedido está anulado.");
        if (pedido.EstadoProceso == "ENTREGADO")
            throw new InvalidOperationException("El pedido ya fue entregado.");
        if (req.Fecha < DateTime.Now.AddMinutes(-5))
            throw new InvalidOperationException("La fecha debe ser posterior al momento actual.");

        await _pedidos.ActualizarFechaEntregaAsync(pedidoId, req.Fecha, usuarioId, req.Motivo, sedeId, ct);
    }

    public async Task AnularAsync(int pedidoId, string motivo, int usuarioId, int sedeId, CancellationToken ct = default)
    {
        var pedido = await _pedidos.ObtenerPorIdAsync(pedidoId, sedeId, ct)
            ?? throw new InvalidOperationException("Pedido no encontrado.");
        if (pedido.Anulado)
            throw new InvalidOperationException("El pedido ya está anulado.");
        if (pedido.EstadoProceso == "ENTREGADO")
            throw new InvalidOperationException("No se puede anular un pedido ya entregado.");

        await _pedidos.AnularAsync(pedidoId, usuarioId, motivo, sedeId, ct);
    }

    public async Task<List<PedidoAbandonadoDto>> ListarAbandonadosAsync(int diasMinimo, int sedeId, CancellationToken ct = default)
    {
        var lista = await _pedidos.ListarListosAbandonadosAsync(diasMinimo, sedeId, ct);
        return lista.Select(a => new PedidoAbandonadoDto(
            a.PedidoId, a.Numero, a.ClienteNombre, a.ClienteCelular,
            a.Total, a.MontoPagado, a.FechaListo, a.DiasEsperando)).ToList();
    }

    public async Task<DashboardDto> DashboardAsync(int sedeId, CancellationToken ct = default)
    {
        var estados = await _pedidos.ContadoresPorEstadoAsync(sedeId, ct);
        var porArea = await _pedidos.ConteoPorAreaAsync(sedeId, ct);
        var areas = await _areas.ListarActivasAsync(sedeId, ct);
        var ventas = await _pedidos.VentasDelDiaAsync(DateTime.Today, sedeId, ct);
        var pedidosDelMes = await _pedidos.PedidosDelMesAsync(DateTime.Today, sedeId, ct);

        var totPendientes = estados.GetValueOrDefault("PENDIENTE", 0)
                          + estados.GetValueOrDefault("EN_PROCESO", 0)
                          + estados.GetValueOrDefault("LISTO", 0);
        var (_, totOtros) = await _pedidos.ListarPaginadoAsync("otros", null, 1, 1, sedeId, ct);
        var (_, totUltimos) = await _pedidos.ListarPaginadoAsync("ultimos", null, 1, 1, sedeId, ct);

        return new DashboardDto
        {
            PedidosPorEstado = estados,
            PedidosPorArea = areas
                .Select(a => new AreaConteoDto(a.Id, a.Nombre, porArea.GetValueOrDefault(a.Id, 0)))
                .ToList(),
            VentasDelDia = ventas,
            TotalPendientes = estados.GetValueOrDefault("PENDIENTE", 0),
            TotalEnProceso = estados.GetValueOrDefault("EN_PROCESO", 0),
            TotalListos = estados.GetValueOrDefault("LISTO", 0),
            PedidosDelMes = pedidosDelMes,
            TotalPendientesTab = totPendientes,
            TotalOtrosTab = totOtros,
            TotalUltimosTab = totUltimos
        };
    }

    public Task<int> SiguienteNumeroAsync(int sedeId, CancellationToken ct = default) => _pedidos.SiguienteNumeroAsync(sedeId, ct);

    private static PedidoDto Map(Pedido p) => new()
    {
        Id = p.Id,
        Numero = p.Numero,
        ClienteId = p.ClienteId,
        ClienteNombre = p.ClienteNombre,
        ClienteCelular = p.ClienteCelular,
        ClienteDni = p.ClienteDni,
        UsuarioNombre = p.UsuarioNombre,
        FechaIngreso = p.FechaIngreso,
        FechaEntregaEst = p.FechaEntregaEst,
        Modalidad = p.Modalidad,
        Subtotal = p.Subtotal,
        Descuento = p.Descuento,
        EsUrgente = p.EsUrgente,
        RecargoUrgente = p.RecargoUrgente,
        Redondeo = p.Redondeo,
        Total = p.Total,
        MontoPagado = p.MontoPagado,
        EstadoPago = p.EstadoPago,
        EstadoProceso = p.EstadoProceso,
        AreaActualId = p.AreaActualId,
        AreaActualNombre = p.AreaActualNombre,
        Observaciones = p.Observaciones,
        Anulado = p.Anulado,
        MotivoAnulacion = p.MotivoAnulacion,
        CodigoAntiguo = p.CodigoAntiguo,
        Items = p.Items.Select(i => new PedidoItemDto
        {
            Id = i.Id,
            ServicioId = i.ServicioId,
            ServicioNombre = i.ServicioNombre,
            Cantidad = i.Cantidad,
            PrecioUnit = i.PrecioUnit,
            Total = i.Total,
            Descripcion = i.Descripcion
        }).ToList()
    };

    private static string NormalizarModalidad(string? modalidad)
    {
        return (modalidad ?? "").Trim().ToLowerInvariant() switch
        {
            "tienda" => "Tienda",
            "recojo" => "Recojo",
            "delivery" => "Delivery",
            _ => modalidad?.Trim() ?? "Tienda"
        };
    }

    private static bool EsPedidoDomicilio(string? modalidad)
        => modalidad is "Recojo" or "Delivery";

    private static bool RequiereEntregaDomicilio(string? modalidad)
        => string.Equals(modalidad, "Delivery", StringComparison.OrdinalIgnoreCase);

    private static void ValidarDatosDomicilio(string? celular, string? direccion, string modalidad)
    {
        if (!EsPedidoDomicilio(modalidad)) return;
        if (string.IsNullOrWhiteSpace(celular))
            throw new InvalidOperationException("Para pedidos a domicilio el cliente debe tener celular.");
        if (string.IsNullOrWhiteSpace(direccion))
            throw new InvalidOperationException("Para pedidos a domicilio debes registrar la dirección del cliente.");
    }

    private static string? LimpiarTexto(string? valor)
        => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}
