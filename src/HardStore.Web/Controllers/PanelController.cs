using HardStore.Web.Dominio;
using HardStore.Web.Modelos;
using HardStore.Web.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HardStore.Web.Controllers;

/// <summary>
/// CAPA: Presentación · Panel de administración.
///
/// Todo el controlador está detrás de [Authorize(Roles = "Admin")]: es lo que
/// permite que el dueño de la tienda administre el catálogo desde la propia
/// página, sin abrir phpMyAdmin ni escribir un UPDATE.
///
/// El alta y la edición de producto incluyen la subida de la imagen: el
/// administrador elige el archivo y el sistema lo guarda y actualiza el
/// producto solo.
/// </summary>
[Authorize(Roles = "Admin")]
[Route("admin")]
public sealed class PanelController : Controller
{
    private readonly CatalogoServicio _catalogo;
    private readonly PedidoServicio _pedidos;
    private readonly UsuarioServicio _usuarios;

    public PanelController(
        CatalogoServicio catalogo,
        PedidoServicio pedidos,
        UsuarioServicio usuarios)
    {
        _catalogo = catalogo;
        _pedidos = pedidos;
        _usuarios = usuarios;
    }

    /* ===================================================================
       TABLERO
       =================================================================== */

    [HttpGet("")]
    [HttpGet("panel")]
    public IActionResult Index()
    {
        return View(new PanelVm
        {
            Metricas = _pedidos.Tablero(),
            AReponer = _catalogo.AReponer().Take(8).ToList(),
            UltimosPedidos = _pedidos.Todos().Take(8).ToList(),
            Promociones = _catalogo.Promociones().Count(p => p.EstaVigente()),
            Cupones = _catalogo.Cupones().Count(c => c.Activo && !c.EstaVencido()),
        });
    }

    /* ===================================================================
       PRODUCTOS
       =================================================================== */

    [HttpGet("productos")]
    public IActionResult Productos(string? q)
    {
        var productos = string.IsNullOrWhiteSpace(q)
            ? _catalogo.Todos()
            : _catalogo.Todos()
                .Where(p => p.Nombre.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                         || p.Codigo.Contains(q, StringComparison.CurrentCultureIgnoreCase))
                .ToList();

        ViewBag.Busqueda = q;
        ViewBag.Categorias = _catalogo.Categorias().ToDictionary(c => c.Id, c => c.Nombre);

        return View(productos);
    }

    [HttpGet("productos/nuevo")]
    public IActionResult NuevoProducto()
        => View("FormProducto", new ProductoFormVm { Categorias = _catalogo.Categorias() });

    [HttpGet("productos/{id:int}/editar")]
    public IActionResult EditarProducto(int id)
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

        return View("FormProducto", new ProductoFormVm
        {
            Id = producto.Id,
            Codigo = producto.Codigo,
            Nombre = producto.Nombre,
            CategoriaId = producto.CategoriaId,
            Precio = producto.Precio,
            Stock = producto.Stock,
            StockMinimo = producto.StockMinimo,
            Descripcion = producto.Descripcion,
            Marca = producto.Marca,
            Destacado = producto.Destacado,
            Activo = producto.Activo,
            ImagenActual = producto.Imagen,
            Categorias = _catalogo.Categorias(),
        });
    }

    /// <summary>
    /// Guarda el producto. El formulario es multipart porque incluye el archivo
    /// de la imagen: eso es lo que reemplaza al UPDATE manual en la base.
    /// </summary>
    [HttpPost("productos/guardar")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> GuardarProducto(ProductoFormVm vm, CancellationToken ct)
    {
        vm.Categorias = _catalogo.Categorias();

        if (!ModelState.IsValid)
        {
            return View("FormProducto", vm);
        }

        try
        {
            if (vm.EsNuevo)
            {
                var creado = await _catalogo.CrearProductoAsync(
                    vm.Codigo, vm.Nombre, vm.CategoriaId, vm.Precio, vm.Stock, vm.StockMinimo,
                    vm.Descripcion ?? "", vm.Marca ?? "", vm.Destacado, vm.Imagen, ct);

                TempData["Exito"] = $"Producto «{creado.Nombre}» creado" +
                    (creado.Imagen is null ? "." : " con su imagen.");
            }
            else
            {
                var actualizado = await _catalogo.ActualizarProductoAsync(
                    vm.Id, vm.Codigo, vm.Nombre, vm.CategoriaId, vm.Precio, vm.StockMinimo,
                    vm.Descripcion ?? "", vm.Marca ?? "", vm.Destacado, vm.Activo,
                    vm.Imagen, vm.QuitarImagen, ct);

                TempData["Exito"] = $"Producto «{actualizado.Nombre}» actualizado.";
            }
        }
        catch (Exception ex) when (ex is ReglaNegocioException or NoEncontradoException)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View("FormProducto", vm);
        }

        return RedirectToAction(nameof(Productos));
    }

    [HttpPost("productos/{id:int}/eliminar")]
    [ValidateAntiForgeryToken]
    public IActionResult EliminarProducto(int id)
    {
        try
        {
            TempData["Exito"] = _catalogo.EliminarProducto(id);
        }
        catch (Exception ex) when (ex is ReglaNegocioException or NoEncontradoException)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Productos));
    }

    [HttpPost("productos/{id:int}/stock")]
    [ValidateAntiForgeryToken]
    public IActionResult MoverStock(int id, string accion, int cantidad)
    {
        try
        {
            var producto = accion == "ajustar"
                ? _catalogo.AjustarStock(id, cantidad)
                : _catalogo.IngresarStock(id, cantidad);

            TempData["Exito"] = $"«{producto.Nombre}» quedó con {producto.Stock} unidades.";
        }
        catch (Exception ex) when (ex is ReglaNegocioException or NoEncontradoException)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Productos));
    }

    /* ===================================================================
       CATEGORÍAS
       =================================================================== */

    [HttpGet("categorias")]
    public IActionResult Categorias() => View(_catalogo.CategoriasConTotales());

    [HttpPost("categorias/crear")]
    [ValidateAntiForgeryToken]
    public IActionResult CrearCategoria(string nombre, string? descripcion)
    {
        try
        {
            var categoria = _catalogo.CrearCategoria(nombre, descripcion);
            TempData["Exito"] = $"Categoría «{categoria.Nombre}» creada.";
        }
        catch (ReglaNegocioException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Categorias));
    }

    [HttpPost("categorias/{id:int}/eliminar")]
    [ValidateAntiForgeryToken]
    public IActionResult EliminarCategoria(int id)
    {
        try
        {
            _catalogo.EliminarCategoria(id);
            TempData["Exito"] = "Categoría eliminada.";
        }
        catch (Exception ex) when (ex is ReglaNegocioException or NoEncontradoException)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Categorias));
    }

    /* ===================================================================
       PROMOCIONES
       =================================================================== */

    [HttpGet("promociones")]
    public IActionResult Promociones()
    {
        ViewBag.Form = new PromocionFormVm
        {
            Productos = _catalogo.Todos(soloActivos: true),
            Categorias = _catalogo.Categorias(),
        };
        ViewBag.Objetivos = _catalogo.Promociones()
            .ToDictionary(p => p.Id, p => _catalogo.DescribirObjetivo(p));

        return View(_catalogo.Promociones());
    }

    [HttpPost("promociones/crear")]
    [ValidateAntiForgeryToken]
    public IActionResult CrearPromocion(PromocionFormVm vm)
    {
        try
        {
            var promo = vm.Tipo == TipoPromocion.LlevaNPagaM
                ? _catalogo.CrearPromocionNxM(
                    vm.Nombre, vm.Alcance, vm.ObjetivoId, vm.LlevaN, vm.PagaM, vm.Desde, vm.Hasta)
                : _catalogo.CrearPromocionPorcentual(
                    vm.Nombre, vm.Alcance, vm.ObjetivoId, vm.Porcentaje, vm.Desde, vm.Hasta);

            TempData["Exito"] = $"Promoción «{promo.Nombre}» ({promo.Etiqueta}) creada.";
        }
        catch (Exception ex) when (ex is ReglaNegocioException or NoEncontradoException)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Promociones));
    }

    [HttpPost("promociones/{id:int}/alternar")]
    [ValidateAntiForgeryToken]
    public IActionResult AlternarPromocion(int id)
    {
        try
        {
            var promo = _catalogo.AlternarPromocion(id);
            TempData["Exito"] = $"«{promo.Nombre}» quedó {(promo.Activa ? "activa" : "pausada")}.";
        }
        catch (NoEncontradoException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Promociones));
    }

    [HttpPost("promociones/{id:int}/eliminar")]
    [ValidateAntiForgeryToken]
    public IActionResult EliminarPromocion(int id)
    {
        try
        {
            _catalogo.EliminarPromocion(id);
            TempData["Exito"] = "Promoción eliminada.";
        }
        catch (NoEncontradoException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Promociones));
    }

    /* ===================================================================
       CUPONES
       =================================================================== */

    [HttpGet("cupones")]
    public IActionResult Cupones()
    {
        ViewBag.Form = new CuponFormVm();
        return View(_catalogo.Cupones());
    }

    [HttpPost("cupones/crear")]
    [ValidateAntiForgeryToken]
    public IActionResult CrearCupon(CuponFormVm vm)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Revisá los datos del cupón.";
            return RedirectToAction(nameof(Cupones));
        }

        try
        {
            var cupon = _catalogo.CrearCupon(
                vm.Codigo, vm.Tipo, vm.Valor, vm.Desde, vm.Hasta,
                vm.MinimoCompra, vm.TopeDescuento, vm.UsosMaximos, vm.Descripcion ?? "");

            TempData["Exito"] = $"Cupón {cupon.Codigo} creado: {cupon.Etiqueta}.";
        }
        catch (ReglaNegocioException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Cupones));
    }

    [HttpPost("cupones/{id:int}/alternar")]
    [ValidateAntiForgeryToken]
    public IActionResult AlternarCupon(int id)
    {
        try
        {
            var cupon = _catalogo.AlternarCupon(id);
            TempData["Exito"] = $"{cupon.Codigo} quedó {(cupon.Activo ? "activo" : "desactivado")}.";
        }
        catch (NoEncontradoException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Cupones));
    }

    [HttpPost("cupones/{id:int}/eliminar")]
    [ValidateAntiForgeryToken]
    public IActionResult EliminarCupon(int id)
    {
        try
        {
            _catalogo.EliminarCupon(id);
            TempData["Exito"] = "Cupón eliminado.";
        }
        catch (Exception ex) when (ex is ReglaNegocioException or NoEncontradoException)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Cupones));
    }

    /* ===================================================================
       PEDIDOS
       =================================================================== */

    [HttpGet("pedidos")]
    public IActionResult Pedidos() => View(_pedidos.Todos());

    [HttpGet("pedidos/{id:int}")]
    public IActionResult Pedido(int id)
    {
        try
        {
            return View(_pedidos.Obtener(id));
        }
        catch (NoEncontradoException)
        {
            return NotFound();
        }
    }

    [HttpPost("pedidos/{id:int}/estado")]
    [ValidateAntiForgeryToken]
    public IActionResult CambiarEstado(int id, EstadoPedido estado)
    {
        try
        {
            var pedido = _pedidos.CambiarEstado(id, estado);
            TempData["Exito"] = $"{pedido.Codigo} quedó como {pedido.Estado}.";
        }
        catch (Exception ex) when (ex is ReglaNegocioException or NoEncontradoException)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Pedidos));
    }

    [HttpPost("pedidos/{id:int}/cancelar")]
    [ValidateAntiForgeryToken]
    public IActionResult CancelarPedido(int id, string motivo)
    {
        try
        {
            var pedido = _pedidos.Cancelar(id, motivo);
            TempData["Exito"] = $"{pedido.Codigo} cancelado y stock devuelto.";
        }
        catch (Exception ex) when (ex is ReglaNegocioException or NoEncontradoException)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Pedidos));
    }

    /* ===================================================================
       USUARIOS
       =================================================================== */

    [HttpGet("usuarios")]
    public IActionResult Usuarios() => View(_usuarios.Todos());

    [HttpPost("usuarios/{id:int}/rol")]
    [ValidateAntiForgeryToken]
    public IActionResult CambiarRol(int id, RolUsuario rol)
    {
        try
        {
            var usuario = _usuarios.CambiarRol(id, rol);
            TempData["Exito"] = $"{usuario.Nombre} ahora es {usuario.Rol}.";
        }
        catch (Exception ex) when (ex is ReglaNegocioException or NoEncontradoException)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Usuarios));
    }
}
