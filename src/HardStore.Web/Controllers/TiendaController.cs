using System.Text.Json;
using HardStore.Web.Datos;
using HardStore.Web.Dominio;
using HardStore.Web.Modelos;
using HardStore.Web.Servicios;
using Microsoft.AspNetCore.Mvc;

namespace HardStore.Web.Controllers;

/// <summary>
/// CAPA: Presentación · La tienda pública.
///
/// Fijate lo que este controlador NO hace: no valida precios, no calcula
/// totales, no decide qué promoción conviene. Le pide todo a los servicios y
/// arma el modelo de vista. Es el mismo criterio que en la versión PHP.
/// </summary>
public sealed class TiendaController : Controller
{
    private readonly CatalogoServicio _catalogo;
    private readonly CotizadorServicio _cotizador;

    public TiendaController(CatalogoServicio catalogo, CotizadorServicio cotizador)
    {
        _catalogo = catalogo;
        _cotizador = cotizador;
    }

    /// <summary>Catálogo con filtros. Es la portada del sitio.</summary>
    [HttpGet]
    public IActionResult Index(
        string? categoria,
        string? q,
        decimal? min,
        decimal? max,
        string orden = "destacados",
        bool soloStock = false,
        int pagina = 1)
    {
        var resultado = _catalogo.Buscar(new FiltroCatalogo(
            CategoriaSlug: categoria,
            Busqueda: q,
            PrecioMin: min,
            PrecioMax: max,
            Orden: orden,
            SoloConStock: soloStock,
            Pagina: pagina,
            PorPagina: 12));

        var todos = _catalogo.Todos(soloActivos: true);

        var vm = new CatalogoVm
        {
            Productos = resultado.Productos,
            Categorias = _catalogo.CategoriasConTotales(),
            Promociones = _cotizador.EtiquetasDePromocion(resultado.Productos),
            NombresCategoria = _catalogo.Categorias().ToDictionary(c => c.Id, c => c.Nombre),
            Total = resultado.Total,
            Pagina = resultado.Pagina,
            Paginas = resultado.Paginas,
            CategoriaSlug = categoria,
            Busqueda = q,
            PrecioMin = min,
            PrecioMax = max,
            Orden = orden,
            SoloConStock = soloStock,
            PrecioMinimoCatalogo = todos.Count == 0 ? 0 : todos.Min(p => p.Precio),
            PrecioMaximoCatalogo = todos.Count == 0 ? 0 : todos.Max(p => p.Precio),
        };

        return View(vm);
    }

    /// <summary>Ficha de un producto.</summary>
    [HttpGet]
    public IActionResult Detalle(int id)
    {
        Producto producto;
        try
        {
            producto = _catalogo.Obtener(id);
        }
        catch (NoEncontradoException)
        {
            return NotFound();
        }

        if (!producto.Activo)
        {
            return NotFound();
        }

        ViewBag.Categoria = _catalogo.Categorias().FirstOrDefault(c => c.Id == producto.CategoriaId)?.Nombre;
        ViewBag.Promociones = _cotizador.PromocionesDe(producto);
        ViewBag.Planes = PlanesPago.Todos;

        return View(producto);
    }

    [HttpGet]
    public IActionResult Error() => View();
}

/// <summary>
/// CAPA: Presentación · Endpoints JSON que consume el JavaScript del carrito.
///
/// El carrito vive en localStorage y sólo guarda {id, cantidad}. Cada vez que
/// cambia, le pide a este endpoint que lo cotice: así el total que ve el
/// cliente sale siempre del servidor, con los precios y las promociones reales.
/// </summary>
[ApiController]
[Route("api")]
public sealed class ApiController : ControllerBase
{
    private readonly CatalogoServicio _catalogo;
    private readonly CotizadorServicio _cotizador;

    public ApiController(CatalogoServicio catalogo, CotizadorServicio cotizador)
    {
        _catalogo = catalogo;
        _cotizador = cotizador;
    }

    /// <summary>GET /api/productos — catálogo filtrado, para el render dinámico.</summary>
    [HttpGet("productos")]
    public IActionResult Productos(
        string? categoria,
        string? q,
        decimal? min,
        decimal? max,
        string orden = "destacados",
        bool soloStock = false,
        int pagina = 1,
        int porPagina = 12)
    {
        var resultado = _catalogo.Buscar(new FiltroCatalogo(
            categoria, q, min, max, orden, soloStock, pagina, porPagina));

        var categorias = _catalogo.Categorias().ToDictionary(c => c.Id, c => c.Nombre);
        var promociones = _cotizador.EtiquetasDePromocion(resultado.Productos);

        return Ok(new
        {
            ok = true,
            total = resultado.Total,
            pagina = resultado.Pagina,
            paginas = resultado.Paginas,
            productos = resultado.Productos.Select(p => new
            {
                id = p.Id,
                codigo = p.Codigo,
                nombre = p.Nombre,
                marca = p.Marca,
                descripcion = p.Descripcion,
                precio = p.Precio,
                stock = p.Stock,
                imagen = p.Imagen,
                destacado = p.Destacado,
                categoria = categorias.TryGetValue(p.CategoriaId, out var nombre) ? nombre : "",
                promocion = promociones.TryGetValue(p.Id, out var etiqueta) ? etiqueta : null,
            }),
        });
    }

    /// <summary>GET /api/producto/5 — una ficha, para cuando el carrito no tiene el dato.</summary>
    [HttpGet("producto/{id:int}")]
    public IActionResult Producto(int id)
    {
        try
        {
            var p = _catalogo.Obtener(id);
            return Ok(new
            {
                ok = true,
                producto = new
                {
                    id = p.Id,
                    codigo = p.Codigo,
                    nombre = p.Nombre,
                    precio = p.Precio,
                    stock = p.Stock,
                    imagen = p.Imagen,
                    activo = p.Activo,
                },
            });
        }
        catch (NoEncontradoException ex)
        {
            return NotFound(new { ok = false, error = ex.Message });
        }
    }

    /// <summary>Lo que manda el navegador para cotizar: sólo ids y cantidades.</summary>
    public sealed record ItemEntrada(int Id, int Cantidad);

    public sealed record PedidoCotizar(
        List<ItemEntrada>? Items,
        string? Cupon,
        string? Plan);

    /// <summary>
    /// POST /api/cotizar — el corazón del carrito.
    ///
    /// Recibe ids y cantidades y devuelve el desglose completo: promociones
    /// aplicadas, cupón, coeficiente del medio de pago, cuotas y total. Ningún
    /// precio viaja desde el navegador.
    /// </summary>
    [HttpPost("cotizar")]
    public IActionResult Cotizar([FromBody] PedidoCotizar entrada)
    {
        var items = (entrada?.Items ?? new List<ItemEntrada>())
            .Select(i => new ItemPedido(i.Id, i.Cantidad));

        var cotizacion = _cotizador.Cotizar(items, entrada?.Cupon, entrada?.Plan);

        return Ok(new
        {
            ok = true,
            avisos = cotizacion.Avisos,
            lineas = cotizacion.Lineas.Select(l => new
            {
                id = l.ProductoId,
                codigo = l.Codigo,
                nombre = l.Nombre,
                imagen = l.Imagen,
                precioUnitario = l.PrecioUnitario,
                cantidad = l.Cantidad,
                stock = l.StockDisponible,
                bonificadas = l.UnidadesBonificadas,
                descuentoPromo = l.DescuentoPromocion,
                promocion = l.PromocionEtiqueta,
                promocionNombre = l.PromocionNombre,
                subtotal = l.Subtotal,
            }),
            bruto = cotizacion.Bruto,
            descuentoPromociones = cotizacion.DescuentoPromociones,
            subtotal = cotizacion.SubtotalConPromociones,
            cupon = cotizacion.CuponCodigo,
            cuponDescripcion = cotizacion.CuponDescripcion,
            descuentoCupon = cotizacion.DescuentoCupon,
            baseFinanciacion = cotizacion.BaseFinanciacion,
            plan = new
            {
                clave = cotizacion.Plan.Clave,
                etiqueta = cotizacion.Plan.Etiqueta,
                detalle = cotizacion.Plan.Detalle,
                cuotas = cotizacion.Plan.Cuotas,
                ajustePorcentual = cotizacion.Plan.AjustePorcentual,
            },
            ajustePago = cotizacion.AjustePago,
            total = cotizacion.Total,
            neto = cotizacion.Neto,
            iva = cotizacion.Iva,
            ahorro = cotizacion.AhorroTotal,
            unidades = cotizacion.CantidadDeArticulos,
            bonificadas = cotizacion.UnidadesBonificadas,
            valorCuota = cotizacion.ValorCuota,
            ultimaCuota = cotizacion.UltimaCuota,
            comparativa = _cotizador.Comparativa(cotizacion).Select(c => new
            {
                clave = c.Plan.Clave,
                etiqueta = c.Plan.Etiqueta,
                cuotas = c.Plan.Cuotas,
                total = c.Total,
                valorCuota = c.ValorCuota,
                ajuste = c.Plan.AjustePorcentual,
            }),
        });
    }
}
