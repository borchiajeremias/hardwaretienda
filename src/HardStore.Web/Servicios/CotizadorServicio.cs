using HardStore.Web.Datos;
using HardStore.Web.Dominio;
using HardStore.Web.Dominio.Precios;

namespace HardStore.Web.Servicios;

/// <summary>Lo que el navegador manda: sólo id y cantidad, nunca precios.</summary>
public sealed record ItemPedido(int ProductoId, int Cantidad);

/// <summary>
/// CAPA: Servicios · El motor de precios. Es el servicio más importante del
/// sistema y el único lugar donde se decide cuánto paga el cliente.
///
/// REGLA DE ORO: el carrito del navegador manda únicamente {productoId,
/// cantidad}. Los precios, las promociones, el cupón y el coeficiente del medio
/// de pago se resuelven acá, con los datos de la base. Manipular localStorage no
/// cambia lo que se cobra. Era el punto más importante de la versión PHP y se
/// mantiene igual.
///
/// ORDEN DE APLICACIÓN — no es arbitrario:
///
///   1. Bruto            precio de lista × cantidad
///   2. Promociones      2x1 / 3x2 / % por línea      (la mejor para cada línea)
///   3. Cupón            sobre el subtotal ya promocionado
///   4. Medio de pago    coeficiente sobre lo que queda a pagar
///
/// Por qué el cupón va después de las promociones: si fuera antes, un cupón del
/// 20 % sobre el precio de lista y después un 3x2 daría un total distinto que al
/// revés, y el cliente tendría razón al discutirlo. Y el medio de pago va
/// último porque el 10 % de la transferencia se calcula sobre lo que realmente
/// se debe, no sobre el precio de vidriera.
/// </summary>
public sealed class CotizadorServicio
{
    private readonly IAlmacen _almacen;

    public CotizadorServicio(IAlmacen almacen) => _almacen = almacen;

    /// <summary>
    /// Cotiza un carrito completo.
    ///
    /// No lanza excepción por cosas que el cliente puede corregir: si pidió más
    /// unidades que el stock, recorta y avisa; si el cupón no aplica, lo ignora
    /// y avisa. Los avisos viajan en la cotización para que la pantalla los
    /// muestre. Lo que sí hace es no cotizar productos inexistentes o inactivos.
    /// </summary>
    public Cotizacion Cotizar(
        IEnumerable<ItemPedido> items,
        string? codigoCupon = null,
        string? clavePlanPago = null,
        bool recortarAlStock = true)
    {
        var avisos = new List<string>();
        var lineas = new List<LineaCotizada>();

        var promocionesVigentes = _almacen.Promociones.Vigentes();

        foreach (var (productoId, cantidadPedida) in Consolidar(items))
        {
            var producto = _almacen.Productos.PorId(productoId);

            // Un producto que ya no existe o se desactivó simplemente sale del
            // carrito con un aviso: no tiene sentido frenar toda la compra.
            if (producto is null)
            {
                avisos.Add($"Un producto del carrito ya no está disponible y se quitó.");
                continue;
            }
            if (!producto.Activo)
            {
                avisos.Add($"«{producto.Nombre}» ya no está disponible y se quitó del carrito.");
                continue;
            }

            var cantidad = cantidadPedida;

            if (recortarAlStock && cantidad > producto.Stock)
            {
                if (producto.Stock == 0)
                {
                    avisos.Add($"«{producto.Nombre}» quedó sin stock y se quitó del carrito.");
                    continue;
                }

                avisos.Add(
                    $"De «{producto.Nombre}» quedan {producto.Stock} unidades: se ajustó la cantidad.");
                cantidad = producto.Stock;
            }

            lineas.Add(CotizarLinea(producto, cantidad, promocionesVigentes));
        }

        // ---------------------------- Cupón ------------------------------
        var subtotal = lineas.Sum(l => l.Subtotal);
        subtotal = Math.Round(subtotal, 2, MidpointRounding.AwayFromZero);

        Cupon? cupon = null;
        var descuentoCupon = 0m;

        if (!string.IsNullOrWhiteSpace(codigoCupon))
        {
            cupon = _almacen.Cupones.PorCodigo(codigoCupon);

            if (cupon is null)
            {
                avisos.Add($"El cupón {codigoCupon.Trim().ToUpperInvariant()} no existe.");
            }
            else
            {
                var motivo = cupon.MotivoDeRechazo(subtotal);
                if (motivo is not null)
                {
                    avisos.Add(motivo);
                    cupon = null;
                }
                else
                {
                    descuentoCupon = cupon.DescuentoSobre(subtotal);
                }
            }
        }

        // ------------------------- Medio de pago -------------------------
        var plan = PlanesPago.PorClave(clavePlanPago) ?? PlanesPago.PorDefecto;

        return new Cotizacion
        {
            Lineas = lineas,
            Avisos = avisos,
            CuponCodigo = cupon?.Codigo,
            CuponDescripcion = cupon?.Etiqueta,
            DescuentoCupon = descuentoCupon,
            Plan = plan,
        };
    }

    /// <summary>
    /// Cotiza una línea eligiendo la promoción que MÁS le convenga al cliente.
    ///
    /// Si un producto está alcanzado por dos promociones (una del producto y
    /// otra de su categoría) no se suman: se aplica la mejor. Sumarlas podría
    /// regalar el producto entero, y no es lo que el comercio quiso ofrecer.
    /// </summary>
    private static LineaCotizada CotizarLinea(
        Producto producto,
        int cantidad,
        IReadOnlyList<Promocion> promociones)
    {
        Promocion? mejor = null;
        var mejorDescuento = 0m;

        foreach (var promo in promociones)
        {
            if (!promo.AplicaA(producto))
            {
                continue;
            }

            var descuento = promo.DescuentoSobre(cantidad, producto.Precio);
            if (descuento > mejorDescuento)
            {
                mejor = promo;
                mejorDescuento = descuento;
            }
        }

        return new LineaCotizada(
            ProductoId: producto.Id,
            Codigo: producto.Codigo,
            Nombre: producto.Nombre,
            Imagen: producto.Imagen,
            PrecioUnitario: producto.Precio,
            Cantidad: cantidad,
            StockDisponible: producto.Stock,
            UnidadesBonificadas: mejor?.UnidadesBonificadas(cantidad) ?? 0,
            DescuentoPromocion: mejorDescuento,
            PromocionNombre: mejor?.Nombre,
            PromocionEtiqueta: mejor?.Etiqueta);
    }

    /// <summary>
    /// Promociones vigentes que le corresponden a un producto, para mostrar la
    /// etiqueta «3x2» en la tarjeta del catálogo.
    /// </summary>
    public IReadOnlyList<Promocion> PromocionesDe(Producto producto)
        => _almacen.Promociones.Vigentes().Where(p => p.AplicaA(producto)).ToList();

    /// <summary>
    /// Mapa productoId → etiqueta de promoción, para pintar el catálogo entero
    /// sin consultar las promociones una vez por producto.
    /// </summary>
    public Dictionary<int, string> EtiquetasDePromocion(IEnumerable<Producto> productos)
    {
        var vigentes = _almacen.Promociones.Vigentes();
        var mapa = new Dictionary<int, string>();

        foreach (var producto in productos)
        {
            var promo = vigentes
                .Where(p => p.AplicaA(producto))
                // La más "vistosa" primero: la que más descuento da con la
                // cantidad mínima que la dispara.
                .OrderByDescending(p => p.Tipo == TipoPromocion.LlevaNPagaM
                    ? p.DescuentoSobre(p.LlevaN, producto.Precio)
                    : p.DescuentoSobre(1, producto.Precio))
                .FirstOrDefault();

            if (promo is not null)
            {
                mapa[producto.Id] = promo.Etiqueta;
            }
        }

        return mapa;
    }

    /// <summary>
    /// La misma cotización calculada para cada plan de pago, para la tabla
    /// comparativa del checkout. No recorre promociones ni cupón de nuevo.
    /// </summary>
    public IReadOnlyList<(PlanPago Plan, decimal Total, decimal ValorCuota)> Comparativa(
        Cotizacion cotizacion)
    {
        var basePago = cotizacion.BaseFinanciacion;

        return PlanesPago.Todos
            .Select(plan => (plan, plan.TotalSobre(basePago), plan.ValorCuota(basePago)))
            .ToList();
    }

    /// <summary>
    /// Consolida el carrito: descarta basura y suma las cantidades repetidas.
    /// Si el mismo producto viene tres veces, se convierte en una sola línea.
    /// </summary>
    private static IEnumerable<(int ProductoId, int Cantidad)> Consolidar(IEnumerable<ItemPedido> items)
    {
        var mapa = new Dictionary<int, int>();

        foreach (var item in items ?? Enumerable.Empty<ItemPedido>())
        {
            if (item.ProductoId <= 0 || item.Cantidad <= 0)
            {
                continue;
            }

            // Tope defensivo: nadie compra 10.000 unidades desde la web.
            var cantidad = Math.Min(item.Cantidad, 99);

            mapa[item.ProductoId] = mapa.TryGetValue(item.ProductoId, out var actual)
                ? Math.Min(actual + cantidad, 99)
                : cantidad;
        }

        return mapa.Select(par => (par.Key, par.Value));
    }
}
