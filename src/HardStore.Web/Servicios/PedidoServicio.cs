using HardStore.Web.Datos;
using HardStore.Web.Dominio;
using HardStore.Web.Dominio.Precios;

namespace HardStore.Web.Servicios;

/// <summary>Datos del comprador que llegan del formulario de checkout.</summary>
/// <summary>
/// Datos de contacto y envío que el cliente escribe en el checkout.
///
/// Todos los campos llegan como texto de un formulario, así que se declaran
/// anulables: un input vacío llega como null, no como cadena vacía. La
/// normalización se hace una sola vez, en Confirmar.
/// </summary>
public sealed record DatosComprador(
    string? Nombre,
    string? Email,
    string? Telefono = "",
    string? Direccion = "",
    string? Observaciones = "");

/// <summary>
/// CAPA: Servicios · Confirmación de la compra.
///
/// El punto delicado, igual que en la versión PHP: un pedido con varias líneas
/// tiene que descontar TODO el stock o NADA. Si la tercera línea no tiene
/// stock, las dos primeras no deben quedar descontadas.
///
/// Se resuelve en dos pasadas dentro de un solo bloqueo del almacén:
///   1ª — revalidar todo y armar las líneas, sin modificar nada.
///   2ª — recién ahí descontar stock, registrar el uso del cupón y guardar.
///
/// El precio NO se recibe del navegador: se vuelve a cotizar acá con
/// <see cref="CotizadorServicio"/> justo antes de cobrar, así el total que se
/// guarda es el que corresponde a los datos vigentes y no el que alguien
/// escribió en el formulario.
/// </summary>
public sealed class PedidoServicio
{
    private readonly IAlmacen _almacen;
    private readonly CotizadorServicio _cotizador;
    private static readonly object CandadoCheckout = new();

    public PedidoServicio(IAlmacen almacen, CotizadorServicio cotizador)
    {
        _almacen = almacen;
        _cotizador = cotizador;
    }

    /// <summary>
    /// Confirma la compra y devuelve el pedido guardado.
    ///
    /// <paramref name="usuarioId"/> es null en las compras como invitado.
    /// </summary>
    public Pedido Confirmar(
        IEnumerable<ItemPedido> items,
        DatosComprador comprador,
        string? codigoCupon,
        string? clavePlanPago,
        string? marcaTarjeta = null,
        int? usuarioId = null)
    {
        var listaItems = (items ?? Enumerable.Empty<ItemPedido>()).ToList();

        if (listaItems.Count == 0)
        {
            throw new ReglaNegocioException("El carrito está vacío.");
        }
        if (listaItems.Count > 50)
        {
            throw new ReglaNegocioException("Demasiados productos distintos en el carrito.");
        }
        if ((comprador.Nombre ?? "").Trim().Length < 2)
        {
            throw new ReglaNegocioException("Ingresá tu nombre completo.");
        }
        if (!Usuario.EsEmailValido(comprador.Email ?? ""))
        {
            throw new ReglaNegocioException("Ingresá un email válido.");
        }

        // A partir de acá se trabaja con los datos ya limpios.
        comprador = comprador with
        {
            Nombre = comprador.Nombre!.Trim(),
            Email = comprador.Email!.Trim(),
            Telefono = (comprador.Telefono ?? "").Trim(),
            Direccion = (comprador.Direccion ?? "").Trim(),
            Observaciones = (comprador.Observaciones ?? "").Trim(),
        };

        var plan = PlanesPago.PorClave(clavePlanPago)
            ?? throw new ReglaNegocioException("Elegí un medio de pago válido.");

        if (PlanesPago.EsConTarjeta(plan.Medio))
        {
            if (string.IsNullOrWhiteSpace(marcaTarjeta))
            {
                throw new ReglaNegocioException("Elegí la marca de la tarjeta.");
            }
            if (!PlanesPago.MarcasTarjeta.Contains(marcaTarjeta.Trim()))
            {
                throw new ReglaNegocioException("La marca de tarjeta elegida no está habilitada.");
            }
        }
        else
        {
            // Con transferencia o efectivo no corresponde guardar marca.
            marcaTarjeta = null;
        }

        // Todo el checkout ocurre bajo un único candado: dos compras
        // simultáneas del último producto se serializan y la segunda ve el
        // stock ya descontado, en lugar de vender la misma unidad dos veces.
        lock (CandadoCheckout)
        {
            // ---------- 1ª pasada: cotizar SIN recortar al stock ----------
            // Si falta stock queremos que falle, no que ajuste en silencio a
            // espaldas del cliente en el momento de cobrar.
            var cotizacion = _cotizador.Cotizar(
                listaItems, codigoCupon, plan.Clave, recortarAlStock: false);

            if (cotizacion.EstaVacia)
            {
                throw new ReglaNegocioException(
                    "Ninguno de los productos del carrito está disponible.");
            }

            foreach (var linea in cotizacion.Lineas)
            {
                var producto = _almacen.Productos.PorId(linea.ProductoId)
                    ?? throw new ReglaNegocioException(
                        $"El producto «{linea.Nombre}» ya no está disponible.");

                if (!producto.Activo)
                {
                    throw new ReglaNegocioException(
                        $"«{producto.Nombre}» está desactivado y no se puede comprar.");
                }
                if (!producto.TieneStockPara(linea.Cantidad))
                {
                    throw new ReglaNegocioException(
                        $"Stock insuficiente de «{producto.Nombre}»: " +
                        $"quedan {producto.Stock} y se piden {linea.Cantidad}.");
                }
            }

            // Si el cliente escribió un cupón y el cotizador lo descartó, se
            // avisa en lugar de cobrarle sin el descuento que esperaba.
            if (!string.IsNullOrWhiteSpace(codigoCupon) && cotizacion.CuponCodigo is null)
            {
                var motivo = cotizacion.Avisos.FirstOrDefault(a => a.Contains("cupón", StringComparison.OrdinalIgnoreCase));
                throw new ReglaNegocioException(motivo ?? "El cupón ingresado no se puede aplicar.");
            }

            // ---------- 2ª pasada: recién ahora se modifica el estado ----------
            var pedido = new Pedido(
                codigo: _almacen.Pedidos.SiguienteCodigo(),
                clienteNombre: comprador.Nombre!,
                clienteEmail: comprador.Email!,
                usuarioId: usuarioId,
                clienteTelefono: comprador.Telefono!,
                direccionEnvio: comprador.Direccion!,
                observaciones: comprador.Observaciones!);

            foreach (var linea in cotizacion.Lineas)
            {
                pedido.AgregarLinea(new LineaPedido(
                    productoId: linea.ProductoId,
                    codigo: linea.Codigo,
                    descripcion: linea.Nombre,
                    precioUnitario: linea.PrecioUnitario,
                    cantidad: linea.Cantidad,
                    unidadesBonificadas: linea.UnidadesBonificadas,
                    descuentoPromocion: linea.DescuentoPromocion,
                    promocionAplicada: linea.PromocionNombre));
            }

            pedido.FijarImportes(
                bruto: cotizacion.Bruto,
                descuentoPromociones: cotizacion.DescuentoPromociones,
                cuponCodigo: cotizacion.CuponCodigo,
                descuentoCupon: cotizacion.DescuentoCupon,
                plan: plan,
                ajustePago: cotizacion.AjustePago,
                total: cotizacion.Total,
                marcaTarjeta: marcaTarjeta);

            _almacen.Pedidos.Guardar(pedido);

            // Descuento de stock
            foreach (var linea in pedido.Lineas)
            {
                var producto = _almacen.Productos.PorId(linea.ProductoId!.Value)!;
                producto.DescontarStock(linea.Cantidad);
                _almacen.Productos.Guardar(producto);
            }

            // Uso del cupón
            if (cotizacion.CuponCodigo is not null)
            {
                var cupon = _almacen.Cupones.PorCodigo(cotizacion.CuponCodigo);
                if (cupon is not null)
                {
                    cupon.RegistrarUso();
                    _almacen.Cupones.Guardar(cupon);
                }
            }

            // Una sola escritura al final: si algo hubiera fallado antes, no
            // quedó nada persistido.
            _almacen.Confirmar();

            return pedido;
        }
    }

    /// <summary>
    /// Cancela un pedido y devuelve el stock.
    ///
    /// Igual que el alta: primero valida que todos los productos sigan
    /// existiendo, después marca el pedido y sólo al final devuelve las
    /// unidades. Si se marcara al final, un reintento devolvería dos veces las
    /// mismas unidades e inventaría inventario — es el bug que corregimos en la
    /// versión PHP y acá ya está bien de entrada.
    /// </summary>
    public Pedido Cancelar(int pedidoId, string motivo)
    {
        lock (CandadoCheckout)
        {
            var pedido = Obtener(pedidoId);

            if (pedido.EstaCancelado)
            {
                throw new ReglaNegocioException($"El pedido {pedido.Codigo} ya estaba cancelado.");
            }
            if (string.IsNullOrWhiteSpace(motivo))
            {
                throw new ReglaNegocioException("Para cancelar un pedido hay que indicar el motivo.");
            }

            foreach (var linea in pedido.Lineas)
            {
                if (linea.ProductoId is null || _almacen.Productos.PorId(linea.ProductoId.Value) is null)
                {
                    throw new ReglaNegocioException(
                        $"No se puede cancelar {pedido.Codigo}: el producto «{linea.Descripcion}» " +
                        "ya no está en el catálogo.");
                }
            }

            pedido.CambiarEstado(EstadoPedido.Cancelado);
            pedido.AgregarObservacion($"Cancelado: {motivo.Trim()}");
            _almacen.Pedidos.Guardar(pedido);

            foreach (var linea in pedido.Lineas)
            {
                var producto = _almacen.Productos.PorId(linea.ProductoId!.Value)!;
                producto.AumentarStock(linea.Cantidad);
                _almacen.Productos.Guardar(producto);
            }

            _almacen.Confirmar();
            return pedido;
        }
    }

    public Pedido CambiarEstado(int pedidoId, EstadoPedido nuevo)
    {
        var pedido = Obtener(pedidoId);

        if (nuevo == EstadoPedido.Cancelado)
        {
            throw new ReglaNegocioException(
                "Para cancelar un pedido usá la acción de cancelar, que devuelve el stock.");
        }

        pedido.CambiarEstado(nuevo);
        _almacen.Pedidos.Guardar(pedido);
        _almacen.Confirmar();

        return pedido;
    }

    /* ------------------------------ Consultas --------------------------- */

    public Pedido Obtener(int id)
        => _almacen.Pedidos.PorId(id) ?? throw NoEncontradoException.Pedido(id);

    public Pedido ObtenerPorCodigo(string codigo)
        => _almacen.Pedidos.PorCodigo(codigo) ?? throw NoEncontradoException.Pedido(codigo);

    public IReadOnlyList<Pedido> Todos() => _almacen.Pedidos.Todos();

    public IReadOnlyList<Pedido> DeUsuario(int usuarioId) => _almacen.Pedidos.PorUsuario(usuarioId);

    /// <summary>Resumen para el tablero del panel de administración.</summary>
    public Dictionary<string, decimal> Tablero()
    {
        var pedidos = _almacen.Pedidos.Todos().Where(p => !p.EstaCancelado).ToList();
        var productos = _almacen.Productos.Todos(soloActivos: true);

        var facturado = pedidos.Sum(p => p.Total);

        return new Dictionary<string, decimal>
        {
            ["productos"] = productos.Count,
            ["agotados"] = productos.Count(p => p.EstaAgotado),
            ["aReponer"] = _almacen.Productos.ConStockBajo().Count,
            ["inventario"] = Math.Round(productos.Sum(p => p.Valorizado), 2),
            ["pedidos"] = pedidos.Count,
            ["facturado"] = Math.Round(facturado, 2),
            ["ticket"] = pedidos.Count == 0 ? 0m : Math.Round(facturado / pedidos.Count, 2),
            ["pendientes"] = pedidos.Count(p => p.Estado == EstadoPedido.Pendiente),
        };
    }
}
