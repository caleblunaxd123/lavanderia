using Lavanderia.Api.Domain;
using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;

namespace Lavanderia.Api.Services;

public interface IPedidoService
{
    Task<PedidoDto> CrearAsync(CrearPedidoRequest req, int usuarioId, int negocioId, int sedeId, CancellationToken ct = default);
    Task<PedidoDto?> ObtenerAsync(int id, int sedeId, CancellationToken ct = default);
    Task<PagedResultDto<PedidoDto>> ListarPaginadoAsync(string? filtro, string? busqueda, DateTime? desde, DateTime? hasta, string? campoFecha, int pagina, int tamanoPagina, int sedeId, CancellationToken ct = default);
    Task<PagedResultDto<PedidoDto>> ListarPorClienteAsync(int clienteId, string? filtro, int pagina, int tamanoPagina, int sedeId, CancellationToken ct = default);
    Task AvanzarAreaAsync(int pedidoId, AvanzarAreaRequest req, int usuarioId, int sedeId, CancellationToken ct = default);
    Task<List<PedidoHistorialDto>> ObtenerHistorialAsync(int pedidoId, int sedeId, CancellationToken ct = default);
    Task AvanzarSiguienteAreaAsync(int pedidoId, int usuarioId, int sedeId, string? recibidoPor = null, CancellationToken ct = default);
    Task<DashboardDto> DashboardAsync(int sedeId, CancellationToken ct = default);
    Task RegistrarPagoAsync(int pedidoId, RegistrarPagoRequest req, int usuarioId, int sedeId, CancellationToken ct = default);
    Task AgregarItemAsync(int pedidoId, AgregarItemRequest req, int negocioId, int sedeId, CancellationToken ct = default);
    Task AnularAsync(int pedidoId, string motivo, int usuarioId, int negocioId, int sedeId, CancellationToken ct = default);
    Task DonarAsync(int pedidoId, int usuarioId, int sedeId, CancellationToken ct = default);
    Task ReenviarAlmacenAsync(int pedidoId, int usuarioId, int sedeId, CancellationToken ct = default);
    Task CambiarFechaEntregaAsync(int pedidoId, CambiarFechaEntregaRequest req, int usuarioId, int sedeId, CancellationToken ct = default);
    Task<List<PedidoAbandonadoDto>> ListarAbandonadosAsync(int diasMinimo, int sedeId, CancellationToken ct = default);
    Task<int> SiguienteNumeroAsync(int sedeId, CancellationToken ct = default);
    Task ConvertirADeliveryAsync(int pedidoId, int negocioId, int sedeId, CancellationToken ct = default);
    Task<Guid?> ObtenerOCrearLinkPagoAsync(int pedidoId, int negocioId, int sedeId, CancellationToken ct = default);
    Task AsignarMotorizadoAsync(int pedidoId, int? motorizadoId, int sedeId, CancellationToken ct = default);
}

public class PedidoService : IPedidoService
{
    private static readonly string[] ModalidadesValidas = ["Tienda", "Recojo", "Delivery"];
    private static readonly string[] FiltrosClienteValidos = ["pendientes", "en-proceso", "con-deuda", "entregados", "todos"];
    private readonly IPedidoRepository _pedidos;
    private readonly IClienteRepository _clientes;
    private readonly IServicioRepository _servicios;
    private readonly IAreaLavadoRepository _areas;
    private readonly IPagosRepository _pagos;
    private readonly IMotorizadoRepository _motorizados;
    private readonly IConfiguracionNegocioRepository _configNegocio;
    private readonly ILogger<PedidoService> _log;

    public PedidoService(
        IPedidoRepository pedidos,
        IClienteRepository clientes,
        IServicioRepository servicios,
        IAreaLavadoRepository areas,
        IPagosRepository pagos,
        IMotorizadoRepository motorizados,
        IConfiguracionNegocioRepository configNegocio,
        ILogger<PedidoService> log)
    {
        _pedidos = pedidos;
        _clientes = clientes;
        _servicios = servicios;
        _areas = areas;
        _pagos = pagos;
        _motorizados = motorizados;
        _configNegocio = configNegocio;
        _log = log;
    }

    public async Task<PedidoDto> CrearAsync(CrearPedidoRequest req, int usuarioId, int negocioId, int sedeId, CancellationToken ct = default)
    {
        var metodosValidos = new[] { "EFECTIVO", "YAPE", "PLIN", "TRANSFERENCIA", "POS", "TARJETA" };
        if (req.MontoPagado > 0 && !metodosValidos.Contains(req.MetodoPagoInicial.ToUpperInvariant()))
            throw new InvalidOperationException("Método de pago inválido.");

        var modalidad = NormalizarModalidad(req.Modalidad);
        if (!ModalidadesValidas.Contains(modalidad))
            throw new InvalidOperationException("Modalidad inválida.");
        if (!EsPedidoDomicilio(modalidad) && req.CostoDelivery is > 0)
            throw new InvalidOperationException("El costo de domicilio solo aplica a pedidos de Recojo o Delivery.");

        var fechaIngreso = req.FechaIngreso ?? DateTime.Now;
        if (fechaIngreso > DateTime.Now.AddMinutes(5))
            throw new InvalidOperationException("La fecha de ingreso no puede estar en el futuro.");
        if (req.FechaEntregaEst is DateTime fechaEntrega && fechaEntrega < fechaIngreso)
            throw new InvalidOperationException("La fecha de entrega debe ser posterior a la fecha de ingreso.");

        int clienteId;
        int puntosDisponibles = 0;
        if (req.ClienteId is int id && id > 0)
        {
            var cliente = await _clientes.ObtenerPorIdAsync(id, negocioId, ct)
                ?? throw new InvalidOperationException("El cliente no existe en este negocio.");
            puntosDisponibles = cliente.Puntos;

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

            ValidarDatosContacto(cliente.Celular, cliente.Direccion, modalidad);
            clienteId = cliente.Id;
        }
        else if (req.ClienteNuevo is { } nuevo && !string.IsNullOrWhiteSpace(nuevo.Nombre))
        {
            ValidarDatosContacto(nuevo.Celular, nuevo.Direccion, modalidad);
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
        var servicioDelivery = EsPedidoDomicilio(modalidad)
            ? await _servicios.ObtenerCargoDeliveryAsync(negocioId, ct)
            : null;
        if (EsPedidoDomicilio(modalidad) && servicioDelivery is null)
            throw new InvalidOperationException("El servicio de domicilio no está configurado para este negocio.");

        var costoDelivery = Math.Round(req.CostoDelivery ?? servicioDelivery?.Precio ?? 0m, 2);
        if (costoDelivery < 0)
            throw new InvalidOperationException("El costo de domicilio no puede ser negativo.");
        var incluyoCargoDomicilio = false;
        decimal subtotal = 0m;
        foreach (var it in req.Items)
        {
            var servicio = await _servicios.ObtenerPorIdAsync(it.ServicioId, negocioId, ct)
                ?? throw new InvalidOperationException($"Servicio {it.ServicioId} no existe.");
            if (!servicio.Activo)
                throw new InvalidOperationException($"El servicio '{servicio.Nombre}' está inactivo.");
            if (servicio.EsCargoDelivery)
            {
                if (!EsPedidoDomicilio(modalidad))
                    throw new InvalidOperationException("El cargo de domicilio no aplica a pedidos de Tienda.");
                if (it.Cantidad != 1)
                    throw new InvalidOperationException("El cargo de domicilio debe tener cantidad 1.");
                if (incluyoCargoDomicilio)
                    throw new InvalidOperationException("El pedido no puede tener más de un cargo de domicilio.");
                incluyoCargoDomicilio = true;
            }

            var precioUnitario = servicio.EsCargoDelivery ? costoDelivery : servicio.Precio;
            var totalItem = Math.Round(precioUnitario * it.Cantidad, 2);
            subtotal += totalItem;
            itemsPersistir.Add(new PedidoItem
            {
                ServicioId = servicio.Id,
                Cantidad = it.Cantidad,
                PrecioUnit = precioUnitario,
                Total = totalItem,
                Descripcion = it.Descripcion
            });
        }

        // El servidor agrega el cargo si el navegador omite el ítem. El precio histórico
        // queda en PedidoItem, aunque luego cambie la tarifa general del negocio.
        if (servicioDelivery is not null && !incluyoCargoDomicilio)
        {
            itemsPersistir.Add(new PedidoItem
            {
                ServicioId = servicioDelivery.Id,
                Cantidad = 1,
                PrecioUnit = costoDelivery,
                Total = costoDelivery,
                Descripcion = "Tarifa de domicilio"
            });
            subtotal += costoDelivery;
        }

        var config = await _configNegocio.ObtenerAsync(negocioId, ct);

        // Tope de descuento manual: el negocio puede limitar cuánto descuento aplica el personal.
        if (req.DescuentoPct < 0) throw new InvalidOperationException("El descuento no puede ser negativo.");
        if (config is { MaxDescuentoPct: > 0 } && req.DescuentoPct > config.MaxDescuentoPct)
            throw new InvalidOperationException($"El descuento máximo permitido es {config.MaxDescuentoPct:0.#}%.");

        var descuentoPct = Math.Round(subtotal * (req.DescuentoPct / 100m), 2);

        // Canje de puntos: cada punto vale ValorPuntoCanje soles de descuento (0 = canje deshabilitado).
        var puntosACanjear = req.PuntosACanjear ?? 0;
        decimal descuentoPuntos = 0m;
        if (puntosACanjear > 0)
        {
            if (config is null || config.ValorPuntoCanje <= 0)
                throw new InvalidOperationException("El canje de puntos no está habilitado en este negocio.");
            if (puntosACanjear > puntosDisponibles)
                throw new InvalidOperationException($"El cliente solo tiene {puntosDisponibles} punto(s) disponibles.");
            descuentoPuntos = Math.Round(puntosACanjear * config.ValorPuntoCanje, 2);
            if (descuentoPuntos > subtotal - descuentoPct + 0.001m)
                throw new InvalidOperationException($"Los puntos a canjear (S/ {descuentoPuntos:F2}) superan el monto disponible del pedido.");
        }

        var descuento = descuentoPct + descuentoPuntos;
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
            FechaIngreso = fechaIngreso,
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

        // Fidelización: canjear los puntos usados y otorgar los ganados por esta compra. Es
        // secundario al pedido — si algo falla aquí, el pedido igual queda creado; se loguea para
        // reconciliar, pero nunca se le devuelve un error al usuario por esto.
        await AplicarPuntosAsync(pedidoId, numero, clienteId, total, puntosACanjear,
            config?.SolesPorPunto ?? 0m, config?.ValorPuntoCanje ?? 0m, usuarioId, negocioId, ct);

        if (EsPedidoDomicilio(pedido.Modalidad))
            await AsegurarLinkPagoAsync(pedidoId, pedido, negocioId, sedeId, ct);

        return (await ObtenerAsync(pedidoId, sedeId, ct))!;
    }

    private async Task AplicarPuntosAsync(int pedidoId, int numero, int clienteId, decimal total,
        int puntosACanjear, decimal solesPorPunto, decimal valorPuntoCanje, int usuarioId, int negocioId, CancellationToken ct)
    {
        try
        {
            if (puntosACanjear > 0 && valorPuntoCanje > 0)
            {
                await _clientes.AgregarMovimientoPuntosAsync(new MovimientoPuntos
                {
                    ClienteId = clienteId, Puntos = puntosACanjear, Tipo = "RESTA",
                    Motivo = $"Canje en pedido #{numero}", UsuarioId = usuarioId, PedidoId = pedidoId
                }, negocioId, ct);
            }

            var puntosGanados = solesPorPunto > 0 ? (int)Math.Floor(total / solesPorPunto) : 0;
            if (puntosGanados > 0)
            {
                await _clientes.AgregarMovimientoPuntosAsync(new MovimientoPuntos
                {
                    ClienteId = clienteId, Puntos = puntosGanados, Tipo = "SUMA",
                    Motivo = $"Pedido #{numero}", UsuarioId = usuarioId, PedidoId = pedidoId
                }, negocioId, ct);
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "No se pudieron aplicar los puntos del pedido {PedidoId} (#{Numero}). El pedido quedó creado.", pedidoId, numero);
        }
    }

    public async Task ConvertirADeliveryAsync(int pedidoId, int negocioId, int sedeId, CancellationToken ct = default)
    {
        var pedido = await _pedidos.ObtenerPorIdAsync(pedidoId, sedeId, ct)
            ?? throw new InvalidOperationException("Pedido no encontrado.");
        if (pedido.Anulado)
            throw new InvalidOperationException("El pedido está anulado.");
        if (pedido.EstadoProceso == "ENTREGADO")
            throw new InvalidOperationException("El pedido ya fue entregado, no se puede convertir a Delivery.");

        if (pedido.Modalidad != "Delivery")
        {
            var cliente = await _clientes.ObtenerPorIdAsync(pedido.ClienteId, negocioId, ct)
                ?? throw new InvalidOperationException("El cliente del pedido no existe.");
            ValidarDatosContacto(cliente.Celular, cliente.Direccion, "Delivery");

            var actualizado = await _pedidos.CambiarModalidadAsync(pedidoId, "Delivery", sedeId, ct);
            if (!actualizado) throw new InvalidOperationException("Pedido no encontrado.");
            pedido.Modalidad = "Delivery";
        }

        await AsegurarLinkPagoAsync(pedidoId, pedido, negocioId, sedeId, ct);
    }

    public async Task AsignarMotorizadoAsync(int pedidoId, int? motorizadoId, int sedeId, CancellationToken ct = default)
    {
        var pedido = await _pedidos.ObtenerPorIdAsync(pedidoId, sedeId, ct)
            ?? throw new InvalidOperationException("Pedido no encontrado.");
        if (pedido.Anulado)
            throw new InvalidOperationException("El pedido está anulado.");
        if (pedido.EstadoProceso == "ENTREGADO")
            throw new InvalidOperationException("El pedido ya fue entregado, no se puede reasignar el repartidor.");

        if (motorizadoId is int id)
        {
            var motorizado = await _motorizados.ObtenerPorIdAsync(id, sedeId, ct)
                ?? throw new InvalidOperationException("El repartidor no pertenece a esta sede.");
            if (!motorizado.Activo)
                throw new InvalidOperationException("Ese repartidor está desactivado.");
        }

        var actualizado = await _pedidos.AsignarMotorizadoAsync(pedidoId, motorizadoId, sedeId, ct);
        if (!actualizado) throw new InvalidOperationException("Pedido no encontrado.");
    }

    public async Task<Guid?> ObtenerOCrearLinkPagoAsync(int pedidoId, int negocioId, int sedeId, CancellationToken ct = default)
    {
        var pedido = await _pedidos.ObtenerPorIdAsync(pedidoId, sedeId, ct)
            ?? throw new InvalidOperationException("Pedido no encontrado.");
        if (pedido.Anulado)
            throw new InvalidOperationException("El pedido está anulado, no se puede generar un link de pago.");
        if (!EsPedidoDomicilio(pedido.Modalidad))
            throw new InvalidOperationException("Este pedido no tiene seguimiento público porque se entrega en tienda.");

        return await AsegurarLinkPagoAsync(pedidoId, pedido, negocioId, sedeId, ct);
    }

    private async Task<Guid?> AsegurarLinkPagoAsync(int pedidoId, Pedido pedido, int negocioId, int sedeId, CancellationToken ct)
    {
        if (pedido.Anulado || !EsPedidoDomicilio(pedido.Modalidad)) return null;
        var saldo = Math.Max(0m, pedido.Total - pedido.MontoPagado);
        if (saldo <= 0.01m) return null;

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

    public async Task<PagedResultDto<PedidoDto>> ListarPaginadoAsync(
        string? filtro,
        string? busqueda,
        DateTime? desde,
        DateTime? hasta,
        string? campoFecha,
        int pagina,
        int tamanoPagina,
        int sedeId,
        CancellationToken ct = default)
    {
        var rangoDesde = desde?.Date;
        var rangoHasta = hasta?.Date;
        var filtroFecha = string.Equals(filtro, "fecha", StringComparison.OrdinalIgnoreCase)
            ? NormalizarCampoFecha(campoFecha)
            : null;

        var (items, total) = await _pedidos.ListarPaginadoAsync(
            filtro, busqueda, rangoDesde, rangoHasta, filtroFecha, pagina, tamanoPagina, sedeId, ct);
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
        var filtroNormalizado = string.IsNullOrWhiteSpace(filtro) ? "en-proceso" : filtro.Trim().ToLowerInvariant();
        if (!FiltrosClienteValidos.Contains(filtroNormalizado))
            filtroNormalizado = "en-proceso";

        var (items, total) = await _pedidos.ListarPorClienteAsync(clienteId, filtroNormalizado, pagina, tamanoPagina, sedeId, ct);
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
        // "ANULADO" queda fuera adrede: anular tiene su propio flujo (AnularAsync) que exige
        // motivo y marca el flag Pedido.Anulado; dejarlo pasar por aca dejaria un pedido con
        // EstadoProceso = ANULADO pero Anulado = 0, invisible para los filtros/reportes que
        // confian en ese flag.
        var estadosValidos = new[] { "PENDIENTE", "EN_PROCESO", "LISTO", "ENTREGADO" };
        if (!estadosValidos.Contains(req.NuevoEstado))
            throw new InvalidOperationException($"Estado invalido: {req.NuevoEstado}");

        var pedido = await _pedidos.ObtenerPorIdAsync(pedidoId, sedeId, ct)
            ?? throw new InvalidOperationException("Pedido no encontrado.");
        if (pedido.Anulado)
            throw new InvalidOperationException("El pedido está anulado.");
        if (pedido.EstadoProceso == "ENTREGADO")
            throw new InvalidOperationException("El pedido ya fue entregado.");
        if (req.NuevoEstado == "ENTREGADO" && pedido.MontoPagado + 0.01m < pedido.Total)
            throw new InvalidOperationException(
                $"El pedido tiene saldo pendiente de S/ {pedido.Total - pedido.MontoPagado:F2}. Registra el pago antes de entregar.");

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
        if (!servicio.Activo)
            throw new InvalidOperationException($"El servicio '{servicio.Nombre}' está inactivo.");
        if (servicio.EsCargoDelivery)
            throw new InvalidOperationException("El cargo de domicilio se gestiona desde la modalidad del pedido y no puede agregarse como un ítem adicional.");

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

    public async Task AnularAsync(int pedidoId, string motivo, int usuarioId, int negocioId, int sedeId, CancellationToken ct = default)
    {
        var pedido = await _pedidos.ObtenerPorIdAsync(pedidoId, sedeId, ct)
            ?? throw new InvalidOperationException("Pedido no encontrado.");
        if (pedido.Anulado)
            throw new InvalidOperationException("El pedido ya está anulado.");
        if (pedido.EstadoProceso == "ENTREGADO")
            throw new InvalidOperationException("No se puede anular un pedido ya entregado.");
        if (pedido.MontoPagado > 0.01m)
            throw new InvalidOperationException(
                $"El pedido tiene S/ {pedido.MontoPagado:F2} en pagos registrados. Gestiona la devolución antes de anularlo.");

        await _pedidos.AnularAsync(pedidoId, usuarioId, motivo, sedeId, ct);

        // Deshace los puntos ganados/canjeados en este pedido (secundario, no debe romper la anulación).
        try { await _clientes.RevertirPuntosPedidoAsync(pedidoId, negocioId, usuarioId, ct); }
        catch (Exception ex) { _log.LogError(ex, "No se pudieron revertir los puntos del pedido anulado {PedidoId}.", pedidoId); }
    }

    public async Task DonarAsync(int pedidoId, int usuarioId, int sedeId, CancellationToken ct = default)
        => await _pedidos.DonarAsync(pedidoId, usuarioId, sedeId, ct);

    public async Task ReenviarAlmacenAsync(int pedidoId, int usuarioId, int sedeId, CancellationToken ct = default)
        => await _pedidos.ReenviarAlmacenAsync(pedidoId, usuarioId, sedeId, ct);

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
        var (_, totOtros) = await _pedidos.ListarPaginadoAsync("otros", null, null, null, null, 1, 1, sedeId, ct);
        var (_, totUltimos) = await _pedidos.ListarPaginadoAsync("ultimos", null, null, null, null, 1, 1, sedeId, ct);

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
        ClientePuntos = p.ClientePuntos,
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
        MotorizadoId = p.MotorizadoId,
        MotorizadoNombre = p.MotorizadoNombre,
        MotorizadoCelular = p.MotorizadoCelular,
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

    private static void ValidarDatosContacto(string? celular, string? direccion, string modalidad)
    {
        // El celular es obligatorio en TODO pedido: es el canal para avisar al cliente
        // (WhatsApp de "listo para recoger", link de pago, etc.).
        if (string.IsNullOrWhiteSpace(celular))
            throw new InvalidOperationException("El cliente debe tener un celular registrado para crear el pedido.");
        if (EsPedidoDomicilio(modalidad) && string.IsNullOrWhiteSpace(direccion))
            throw new InvalidOperationException("Para pedidos a domicilio debes registrar la dirección del cliente.");
    }

    private static string? LimpiarTexto(string? valor)
        => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    private static string? NormalizarCampoFecha(string? campoFecha)
    {
        return (campoFecha ?? "").Trim().ToLowerInvariant() switch
        {
            "entrega" => "entrega",
            _ => "ingreso"
        };
    }
}
