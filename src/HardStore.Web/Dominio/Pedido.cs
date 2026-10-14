namespace HardStore.Web.Dominio;

public enum EstadoPedido
{
    Pendiente = 0,
    Pagado = 1,
    Enviado = 2,
    Entregado = 3,
    Cancelado = 4,
}

/// <summary>
/// CAPA: Dominio · Una línea del pedido.
///
/// Guarda una FOTO del producto al momento de comprar: código, nombre y precio
/// se copian, no se referencian. Si mañana cambia el precio de lista, el pedido
/// ya emitido no se altera. También congela el descuento de promoción que se
/// aplicó y con qué promoción, para poder explicarle al cliente su factura seis
/// meses después.
/// </summary>
public class LineaPedido
{
    public int Id { get; private set; }
    public int PedidoId { get; private set; }

    public int? ProductoId { get; private set; }
    public string Codigo { get; private set; } = "";
    public string Descripcion { get; private set; } = "";

    public decimal PrecioUnitario { get; private set; }
    public int Cantidad { get; private set; }

    /// <summary>Unidades regaladas por una promoción NxM. 0 si no hubo promo.</summary>
    public int UnidadesBonificadas { get; private set; }

    /// <summary>Descuento en pesos que aportó la promoción a esta línea.</summary>
    public decimal DescuentoPromocion { get; private set; }

    /// <summary>Nombre de la promoción aplicada, para el comprobante. Null si no hubo.</summary>
    public string? PromocionAplicada { get; private set; }

    private LineaPedido() { }

    /// <summary>
    /// Reconstruye la entidad tal como estaba guardada. La usa SÓLO la capa de
    /// datos al leer del archivo o de la base: es la contracara del constructor
    /// público, que valida datos nuevos. Acá los datos ya fueron validados
    /// cuando se crearon, así que se restauran tal cual — incluidas la fecha de
    /// creación y las banderas que no tienen setter público.
    /// </summary>
    internal static LineaPedido Rehidratar(
        int id, int pedidoId, int? productoId, string codigo, string descripcion,
        decimal precioUnitario, int cantidad, int unidadesBonificadas,
        decimal descuentoPromocion, string? promocionAplicada)
        => new()
        {
            Id = id,
            PedidoId = pedidoId,
            ProductoId = productoId,
            Codigo = codigo,
            Descripcion = descripcion,
            PrecioUnitario = precioUnitario,
            Cantidad = cantidad,
            UnidadesBonificadas = unidadesBonificadas,
            DescuentoPromocion = descuentoPromocion,
            PromocionAplicada = promocionAplicada,
        };

    public LineaPedido(
        int? productoId,
        string codigo,
        string descripcion,
        decimal precioUnitario,
        int cantidad,
        int unidadesBonificadas = 0,
        decimal descuentoPromocion = 0m,
        string? promocionAplicada = null)
    {
        if (cantidad <= 0)
        {
            throw new ArgumentException("La cantidad debe ser mayor a cero.");
        }
        if (precioUnitario <= 0)
        {
            throw new ArgumentException("El precio unitario debe ser mayor a cero.");
        }
        if (unidadesBonificadas < 0 || unidadesBonificadas > cantidad)
        {
            throw new ArgumentException("Las unidades bonificadas no pueden superar la cantidad.");
        }
        if (descuentoPromocion < 0)
        {
            throw new ArgumentException("El descuento de la promoción no puede ser negativo.");
        }

        ProductoId = productoId;
        Codigo = (codigo ?? "").Trim();
        Descripcion = (descripcion ?? "").Trim();
        PrecioUnitario = Math.Round(precioUnitario, 2, MidpointRounding.AwayFromZero);
        Cantidad = cantidad;
        UnidadesBonificadas = unidadesBonificadas;
        DescuentoPromocion = Math.Round(descuentoPromocion, 2, MidpointRounding.AwayFromZero);
        PromocionAplicada = string.IsNullOrWhiteSpace(promocionAplicada) ? null : promocionAplicada.Trim();
    }

    public void AsignarId(int id) => Id = id;
    public void AsignarPedido(int pedidoId) => PedidoId = pedidoId;

    /// <summary>Precio × cantidad, antes de la promoción.</summary>
    public decimal Bruto => Math.Round(PrecioUnitario * Cantidad, 2, MidpointRounding.AwayFromZero);

    /// <summary>Lo que efectivamente se cobra por esta línea.</summary>
    public decimal Subtotal => Math.Round(Bruto - DescuentoPromocion, 2, MidpointRounding.AwayFromZero);

    public bool TienePromocion => DescuentoPromocion > 0;
}

/// <summary>
/// CAPA: Dominio · Cabecera del pedido.
///
/// El pedido guarda el desglose completo y ya calculado: bruto, descuento por
/// promociones, descuento por cupón, ajuste del medio de pago y total. No se
/// recalcula al mostrarlo, porque los coeficientes y las promociones cambian
/// con el tiempo y el comprobante tiene que seguir diciendo lo mismo siempre.
/// </summary>
public class Pedido
{
    public int Id { get; private set; }

    public string Codigo { get; private set; } = "";

    public int? UsuarioId { get; private set; }

    public string ClienteNombre { get; private set; } = "";
    public string ClienteEmail { get; private set; } = "";
    public string ClienteTelefono { get; private set; } = "";
    public string DireccionEnvio { get; private set; } = "";

    private readonly List<LineaPedido> _lineas = new();
    public IReadOnlyList<LineaPedido> Lineas => _lineas;

    /* ------------------------ Desglose congelado ------------------------ */

    public decimal Bruto { get; private set; }
    public decimal DescuentoPromociones { get; private set; }

    public string? CuponCodigo { get; private set; }
    public decimal DescuentoCupon { get; private set; }

    public MedioPago Medio { get; private set; }
    public int Cuotas { get; private set; } = 1;
    public decimal CoeficientePago { get; private set; } = 1m;
    public decimal AjustePago { get; private set; }
    public string? MarcaTarjeta { get; private set; }

    public decimal Total { get; private set; }

    public EstadoPedido Estado { get; private set; } = EstadoPedido.Pendiente;
    public string Observaciones { get; private set; } = "";
    public DateTime CreadoEn { get; private set; } = DateTime.Now;

    private Pedido() { }

    /// <summary>
    /// Reconstruye la entidad tal como estaba guardada. La usa SÓLO la capa de
    /// datos al leer del archivo o de la base: es la contracara del constructor
    /// público, que valida datos nuevos. Acá los datos ya fueron validados
    /// cuando se crearon, así que se restauran tal cual — incluidas la fecha de
    /// creación y las banderas que no tienen setter público.
    /// </summary>
    internal static Pedido Rehidratar(
        int id, string codigo, int? usuarioId, string clienteNombre, string clienteEmail,
        string clienteTelefono, string direccionEnvio, decimal bruto, decimal descuentoPromociones,
        string? cuponCodigo, decimal descuentoCupon, MedioPago medio, int cuotas,
        decimal coeficientePago, decimal ajustePago, string? marcaTarjeta, decimal total,
        EstadoPedido estado, string observaciones, DateTime creadoEn, IEnumerable<LineaPedido> lineas)
    {
        var pedido = new Pedido
        {
            Id = id,
            Codigo = codigo,
            UsuarioId = usuarioId,
            ClienteNombre = clienteNombre,
            ClienteEmail = clienteEmail,
            ClienteTelefono = clienteTelefono,
            DireccionEnvio = direccionEnvio,
            Bruto = bruto,
            DescuentoPromociones = descuentoPromociones,
            CuponCodigo = cuponCodigo,
            DescuentoCupon = descuentoCupon,
            Medio = medio,
            Cuotas = cuotas,
            CoeficientePago = coeficientePago,
            AjustePago = ajustePago,
            MarcaTarjeta = marcaTarjeta,
            Total = total,
            Estado = estado,
            Observaciones = observaciones,
            CreadoEn = creadoEn,
        };

        pedido._lineas.AddRange(lineas);
        return pedido;
    }

    public Pedido(
        string codigo,
        string clienteNombre,
        string clienteEmail,
        int? usuarioId = null,
        string clienteTelefono = "",
        string direccionEnvio = "",
        string observaciones = "")
    {
        if (string.IsNullOrWhiteSpace(codigo))
        {
            throw new ArgumentException("El pedido necesita un código.");
        }
        if ((clienteNombre ?? "").Trim().Length < 2)
        {
            throw new ArgumentException("El pedido necesita el nombre del cliente.");
        }
        if (!Usuario.EsEmailValido(clienteEmail))
        {
            throw new ArgumentException("El pedido necesita un email válido.");
        }

        Codigo = codigo.Trim();
        ClienteNombre = clienteNombre!.Trim();
        ClienteEmail = clienteEmail!.Trim().ToLowerInvariant();
        UsuarioId = usuarioId;
        ClienteTelefono = (clienteTelefono ?? "").Trim();
        DireccionEnvio = (direccionEnvio ?? "").Trim();
        Observaciones = (observaciones ?? "").Trim();
    }

    public void AsignarId(int id)
    {
        if (Id != 0)
        {
            throw new InvalidOperationException("El pedido ya tiene un id asignado.");
        }
        Id = id;
    }

    public void AgregarLinea(LineaPedido linea)
    {
        // Si el producto ya está, se suman las cantidades en lugar de duplicar.
        var existente = _lineas.FirstOrDefault(l =>
            l.ProductoId is not null && l.ProductoId == linea.ProductoId);

        if (existente is not null)
        {
            throw new InvalidOperationException(
                $"La línea del producto {linea.Codigo} ya está en el pedido: consolidá antes de agregar.");
        }

        _lineas.Add(linea);
    }

    /// <summary>
    /// Congela el desglose calculado por el cotizador. Se llama una sola vez,
    /// al crear el pedido: después el comprobante es inmutable.
    /// </summary>
    public void FijarImportes(
        decimal bruto,
        decimal descuentoPromociones,
        string? cuponCodigo,
        decimal descuentoCupon,
        PlanPago plan,
        decimal ajustePago,
        decimal total,
        string? marcaTarjeta = null)
    {
        if (bruto < 0 || descuentoPromociones < 0 || descuentoCupon < 0 || total < 0)
        {
            throw new ArgumentException("Los importes del pedido no pueden ser negativos.");
        }

        Bruto = bruto;
        DescuentoPromociones = descuentoPromociones;
        CuponCodigo = string.IsNullOrWhiteSpace(cuponCodigo) ? null : cuponCodigo.Trim().ToUpperInvariant();
        DescuentoCupon = descuentoCupon;
        Medio = plan.Medio;
        Cuotas = plan.Cuotas;
        CoeficientePago = plan.Coeficiente;
        AjustePago = ajustePago;
        Total = total;
        MarcaTarjeta = string.IsNullOrWhiteSpace(marcaTarjeta) ? null : marcaTarjeta.Trim();
    }

    public void CambiarEstado(EstadoPedido nuevo)
    {
        if (Estado == EstadoPedido.Cancelado && nuevo != EstadoPedido.Cancelado)
        {
            throw new InvalidOperationException("Un pedido cancelado no puede volver a otro estado.");
        }
        Estado = nuevo;
    }

    public void AgregarObservacion(string texto)
    {
        texto = (texto ?? "").Trim();
        if (texto.Length == 0)
        {
            return;
        }
        Observaciones = Observaciones.Length == 0 ? texto : $"{Observaciones} | {texto}";
    }

    /* ----------------------------- Consultas ---------------------------- */

    public int CantidadDeArticulos => _lineas.Sum(l => l.Cantidad);

    public int UnidadesBonificadas => _lineas.Sum(l => l.UnidadesBonificadas);

    /// <summary>Subtotal después de promociones y antes del cupón.</summary>
    public decimal SubtotalConPromociones
        => Math.Round(Bruto - DescuentoPromociones, 2, MidpointRounding.AwayFromZero);

    /// <summary>Importe de lista al que se le aplica el coeficiente del medio de pago.</summary>
    public decimal BaseFinanciacion
        => Math.Round(SubtotalConPromociones - DescuentoCupon, 2, MidpointRounding.AwayFromZero);

    /// <summary>IVA contenido en el total (los precios de lista ya lo incluyen).</summary>
    public decimal Iva => Math.Round(Total - Neto, 2, MidpointRounding.AwayFromZero);

    public decimal Neto => Math.Round(Total / 1.21m, 2, MidpointRounding.AwayFromZero);

    public decimal ValorCuota
    {
        get
        {
            var plan = PlanesPago.Buscar(Medio, Cuotas);
            return plan is null || Cuotas <= 1
                ? Total
                : Math.Round(Total / Cuotas, 2, MidpointRounding.AwayFromZero);
        }
    }

    public string EtiquetaPago
    {
        get
        {
            var nombre = PlanesPago.NombreMedio(Medio);
            var marca = MarcaTarjeta is null ? "" : $" {MarcaTarjeta}";
            return Cuotas > 1 ? $"{nombre}{marca} · {Cuotas} cuotas" : $"{nombre}{marca}";
        }
    }

    public bool EstaCancelado => Estado == EstadoPedido.Cancelado;
}
