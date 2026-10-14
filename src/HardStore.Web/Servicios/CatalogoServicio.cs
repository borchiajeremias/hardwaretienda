using HardStore.Web.Datos;
using HardStore.Web.Dominio;

namespace HardStore.Web.Servicios;

/// <summary>Filtros del catálogo público.</summary>
public sealed record FiltroCatalogo(
    string? CategoriaSlug = null,
    string? Busqueda = null,
    decimal? PrecioMin = null,
    decimal? PrecioMax = null,
    string Orden = "destacados",
    bool SoloConStock = false,
    int Pagina = 1,
    int PorPagina = 12);

/// <summary>Resultado paginado del catálogo.</summary>
public sealed record ResultadoCatalogo(
    IReadOnlyList<Producto> Productos,
    int Total,
    int Pagina,
    int PorPagina)
{
    public int Paginas => PorPagina <= 0 ? 1 : (int)Math.Ceiling(Total / (double)PorPagina);
}

/// <summary>
/// CAPA: Servicios · CRUD de productos, categorías, promociones y cupones.
///
/// Es el único camino por el que los controladores tocan el catálogo. Nunca
/// devuelve HTML ni texto formateado: recibe datos, aplica reglas y devuelve
/// entidades o lanza excepciones.
///
/// Reglas que hace cumplir, portadas de la versión PHP:
///  · El código de producto es único.
///  · El producto pertenece a una categoría que existe.
///  · Un producto que figura en un pedido no se borra: se desactiva.
///  · Una categoría con productos no se elimina.
/// </summary>
public sealed class CatalogoServicio
{
    private readonly IAlmacen _almacen;
    private readonly ImagenServicio _imagenes;

    public CatalogoServicio(IAlmacen almacen, ImagenServicio imagenes)
    {
        _almacen = almacen;
        _imagenes = imagenes;
    }

    /* ===================================================================
       CATÁLOGO PÚBLICO
       =================================================================== */

    public ResultadoCatalogo Buscar(FiltroCatalogo filtro)
    {
        IEnumerable<Producto> query = _almacen.Productos.Todos(soloActivos: true);

        if (!string.IsNullOrWhiteSpace(filtro.CategoriaSlug))
        {
            var categoria = _almacen.Categorias.PorSlug(filtro.CategoriaSlug);
            query = categoria is null
                ? Enumerable.Empty<Producto>()
                : query.Where(p => p.CategoriaId == categoria.Id);
        }

        if (!string.IsNullOrWhiteSpace(filtro.Busqueda))
        {
            var texto = filtro.Busqueda.Trim();
            query = query.Where(p =>
                p.Nombre.Contains(texto, StringComparison.CurrentCultureIgnoreCase)
                || p.Codigo.Contains(texto, StringComparison.CurrentCultureIgnoreCase)
                || p.Marca.Contains(texto, StringComparison.CurrentCultureIgnoreCase)
                || p.Descripcion.Contains(texto, StringComparison.CurrentCultureIgnoreCase));
        }

        if (filtro.PrecioMin is { } minimo)
        {
            query = query.Where(p => p.Precio >= minimo);
        }
        if (filtro.PrecioMax is { } maximo)
        {
            query = query.Where(p => p.Precio <= maximo);
        }
        if (filtro.SoloConStock)
        {
            query = query.Where(p => p.Stock > 0);
        }

        // Orden: sólo se aceptan las claves conocidas. Es el equivalente a la
        // whitelist del ORDER BY que tenía la versión PHP.
        query = filtro.Orden switch
        {
            "nuevos" => query.OrderByDescending(p => p.CreadoEn).ThenByDescending(p => p.Id),
            "precio_asc" => query.OrderBy(p => p.Precio),
            "precio_desc" => query.OrderByDescending(p => p.Precio),
            "nombre" => query.OrderBy(p => p.Nombre, StringComparer.CurrentCultureIgnoreCase),
            _ => query.OrderByDescending(p => p.Destacado).ThenByDescending(p => p.Id),
        };

        var lista = query.ToList();

        var porPagina = Math.Clamp(filtro.PorPagina, 1, 60);
        var pagina = Math.Max(filtro.Pagina, 1);

        var pagina1 = lista
            .Skip((pagina - 1) * porPagina)
            .Take(porPagina)
            .ToList();

        return new ResultadoCatalogo(pagina1, lista.Count, pagina, porPagina);
    }

    public Producto Obtener(int id)
        => _almacen.Productos.PorId(id) ?? throw NoEncontradoException.Producto(id);

    public Producto ObtenerPorCodigo(string codigo)
        => _almacen.Productos.PorCodigo(codigo) ?? throw NoEncontradoException.Producto(codigo);

    public IReadOnlyList<Producto> Todos(bool soloActivos = false)
        => _almacen.Productos.Todos(soloActivos);

    public IReadOnlyList<Producto> AReponer() => _almacen.Productos.ConStockBajo();

    /* ===================================================================
       PRODUCTOS · ALTA, MODIFICACIÓN, BAJA
       =================================================================== */

    public async Task<Producto> CrearProductoAsync(
        string codigo,
        string nombre,
        int categoriaId,
        decimal precio,
        int stock,
        int stockMinimo,
        string descripcion,
        string marca,
        bool destacado,
        IFormFile? imagen,
        CancellationToken ct = default)
    {
        ExigirCategoria(categoriaId);
        ExigirCodigoLibre(codigo);

        Producto producto;
        try
        {
            producto = new Producto(
                codigo, nombre, categoriaId, precio, stock, stockMinimo, descripcion, marca, destacado);
        }
        catch (ArgumentException ex)
        {
            throw new ReglaNegocioException(ex.Message, ex);
        }

        // La imagen se guarda DESPUÉS de validar el producto: si el código
        // estaba repetido, no queda un archivo huérfano en el disco.
        if (imagen is not null && imagen.Length > 0)
        {
            producto.CambiarImagen(await _imagenes.GuardarAsync(imagen, ct));
        }

        _almacen.Productos.Guardar(producto);
        _almacen.Confirmar();

        return producto;
    }

    public async Task<Producto> ActualizarProductoAsync(
        int id,
        string codigo,
        string nombre,
        int categoriaId,
        decimal precio,
        int stockMinimo,
        string descripcion,
        string marca,
        bool destacado,
        bool activo,
        IFormFile? imagenNueva,
        bool quitarImagen = false,
        CancellationToken ct = default)
    {
        var producto = Obtener(id);

        ExigirCategoria(categoriaId);

        var codigoNormalizado = (codigo ?? "").Trim().ToUpperInvariant();
        if (codigoNormalizado != producto.Codigo)
        {
            ExigirCodigoLibre(codigoNormalizado);
        }

        try
        {
            producto.CambiarCodigo(codigoNormalizado);
            producto.CambiarNombre(nombre);
            producto.CambiarCategoria(categoriaId);
            producto.CambiarPrecio(precio);
            producto.CambiarStockMinimo(stockMinimo);
            producto.CambiarDescripcion(descripcion);
            producto.CambiarMarca(marca);
            producto.CambiarDestacado(destacado);
        }
        catch (ArgumentException ex)
        {
            throw new ReglaNegocioException(ex.Message, ex);
        }

        if (activo)
        {
            producto.Activar();
        }
        else
        {
            producto.Desactivar();
        }

        // El stock no se edita desde el formulario del producto: se mueve por
        // las acciones de stock, para que quede claro qué pasó.
        var imagenAnterior = producto.Imagen;

        if (imagenNueva is not null && imagenNueva.Length > 0)
        {
            producto.CambiarImagen(await _imagenes.GuardarAsync(imagenNueva, ct));
            _imagenes.Borrar(imagenAnterior);
        }
        else if (quitarImagen)
        {
            producto.CambiarImagen(null);
            _imagenes.Borrar(imagenAnterior);
        }

        _almacen.Productos.Guardar(producto);
        _almacen.Confirmar();

        return producto;
    }

    /// <summary>
    /// Baja de producto. Si figura en algún pedido, se desactiva en lugar de
    /// borrarse: los comprobantes viejos tienen que seguir siendo legibles.
    /// </summary>
    public string EliminarProducto(int id)
    {
        var producto = Obtener(id);
        var pedidos = _almacen.Pedidos.ConProducto(id).Count;

        if (pedidos > 0)
        {
            producto.Desactivar();
            _almacen.Productos.Guardar(producto);
            _almacen.Confirmar();

            return $"«{producto.Nombre}» figura en {pedidos} pedido(s), así que se desactivó " +
                   "en lugar de borrarse.";
        }

        // Al borrar el producto se borra también su imagen, para no dejar
        // archivos sin dueño en el disco.
        _imagenes.Borrar(producto.Imagen);

        // Y las promociones que apuntaban sólo a él quedan sin objetivo.
        foreach (var promo in _almacen.Promociones.ConObjetivo(AlcancePromocion.Producto, id))
        {
            _almacen.Promociones.Eliminar(promo.Id);
        }

        _almacen.Productos.Eliminar(id);
        _almacen.Confirmar();

        return $"«{producto.Nombre}» se eliminó definitivamente.";
    }

    /* ------------------------------- Stock ------------------------------- */

    public Producto IngresarStock(int id, int cantidad)
    {
        var producto = Obtener(id);

        try
        {
            producto.AumentarStock(cantidad);
        }
        catch (ArgumentException ex)
        {
            throw new ReglaNegocioException(ex.Message, ex);
        }

        _almacen.Productos.Guardar(producto);
        _almacen.Confirmar();

        return producto;
    }

    public Producto AjustarStock(int id, int stockContado)
    {
        var producto = Obtener(id);

        try
        {
            producto.AjustarStock(stockContado);
        }
        catch (ArgumentException ex)
        {
            throw new ReglaNegocioException(ex.Message, ex);
        }

        _almacen.Productos.Guardar(producto);
        _almacen.Confirmar();

        return producto;
    }

    /* ===================================================================
       CATEGORÍAS
       =================================================================== */

    public IReadOnlyList<Categoria> Categorias() => _almacen.Categorias.Todas();

    public Categoria ObtenerCategoria(int id)
        => _almacen.Categorias.PorId(id) ?? throw NoEncontradoException.Categoria(id);

    public IReadOnlyList<(Categoria Categoria, int Cantidad)> CategoriasConTotales()
        => _almacen.Categorias.Todas()
            .Select(c => (c, _almacen.Productos.PorCategoria(c.Id).Count(p => p.Activo)))
            .ToList();

    public Categoria CrearCategoria(string nombre, string? descripcion = null)
    {
        if (_almacen.Categorias.PorNombre(nombre) is not null)
        {
            throw new ReglaNegocioException($"Ya existe una categoría llamada «{nombre}».");
        }

        Categoria categoria;
        try
        {
            categoria = new Categoria(nombre, descripcion: descripcion ?? "");
        }
        catch (ArgumentException ex)
        {
            throw new ReglaNegocioException(ex.Message, ex);
        }

        if (_almacen.Categorias.PorSlug(categoria.Slug) is not null)
        {
            throw new ReglaNegocioException(
                $"Ya existe una categoría con la dirección «{categoria.Slug}».");
        }

        _almacen.Categorias.Guardar(categoria);
        _almacen.Confirmar();

        return categoria;
    }

    public Categoria ActualizarCategoria(int id, string nombre, string? descripcion)
    {
        var categoria = ObtenerCategoria(id);

        var otra = _almacen.Categorias.PorNombre(nombre);
        if (otra is not null && otra.Id != id)
        {
            throw new ReglaNegocioException($"Ya existe otra categoría llamada «{nombre}».");
        }

        try
        {
            categoria.CambiarNombre(nombre);
            categoria.CambiarSlug(nombre);
            categoria.CambiarDescripcion(descripcion);
        }
        catch (ArgumentException ex)
        {
            throw new ReglaNegocioException(ex.Message, ex);
        }

        _almacen.Categorias.Guardar(categoria);
        _almacen.Confirmar();

        return categoria;
    }

    public void EliminarCategoria(int id)
    {
        ObtenerCategoria(id);

        var cantidad = _almacen.Productos.PorCategoria(id).Count;
        if (cantidad > 0)
        {
            throw new ReglaNegocioException(
                $"La categoría tiene {cantidad} producto(s). Movelos a otra antes de eliminarla.");
        }

        foreach (var promo in _almacen.Promociones.ConObjetivo(AlcancePromocion.Categoria, id))
        {
            _almacen.Promociones.Eliminar(promo.Id);
        }

        _almacen.Categorias.Eliminar(id);
        _almacen.Confirmar();
    }

    /* ===================================================================
       PROMOCIONES
       =================================================================== */

    public IReadOnlyList<Promocion> Promociones() => _almacen.Promociones.Todas();

    public Promocion ObtenerPromocion(int id)
        => _almacen.Promociones.PorId(id) ?? throw NoEncontradoException.Promocion(id);

    public Promocion CrearPromocionNxM(
        string nombre,
        AlcancePromocion alcance,
        int objetivoId,
        int llevaN,
        int pagaM,
        DateTime desde,
        DateTime hasta)
    {
        ExigirObjetivo(alcance, objetivoId);

        try
        {
            var promo = Promocion.LlevaNPaga(nombre, alcance, objetivoId, llevaN, pagaM, desde, hasta);
            _almacen.Promociones.Guardar(promo);
            _almacen.Confirmar();
            return promo;
        }
        catch (ArgumentException ex)
        {
            throw new ReglaNegocioException(ex.Message, ex);
        }
    }

    public Promocion CrearPromocionPorcentual(
        string nombre,
        AlcancePromocion alcance,
        int objetivoId,
        decimal porcentaje,
        DateTime desde,
        DateTime hasta)
    {
        ExigirObjetivo(alcance, objetivoId);

        try
        {
            var promo = Promocion.Porcentual(nombre, alcance, objetivoId, porcentaje, desde, hasta);
            _almacen.Promociones.Guardar(promo);
            _almacen.Confirmar();
            return promo;
        }
        catch (ArgumentException ex)
        {
            throw new ReglaNegocioException(ex.Message, ex);
        }
    }

    public Promocion AlternarPromocion(int id)
    {
        var promo = ObtenerPromocion(id);

        if (promo.Activa)
        {
            promo.Desactivar();
        }
        else
        {
            promo.Activar();
        }

        _almacen.Promociones.Guardar(promo);
        _almacen.Confirmar();

        return promo;
    }

    public void EliminarPromocion(int id)
    {
        ObtenerPromocion(id);
        _almacen.Promociones.Eliminar(id);
        _almacen.Confirmar();
    }

    /// <summary>Nombre del objetivo de una promoción, para los listados.</summary>
    public string DescribirObjetivo(Promocion promo)
    {
        if (promo.Alcance == AlcancePromocion.Categoria)
        {
            return _almacen.Categorias.PorId(promo.ObjetivoId)?.Nombre ?? "(categoría eliminada)";
        }
        return _almacen.Productos.PorId(promo.ObjetivoId)?.Nombre ?? "(producto eliminado)";
    }

    /* ===================================================================
       CUPONES
       =================================================================== */

    public IReadOnlyList<Cupon> Cupones() => _almacen.Cupones.Todos();

    public Cupon ObtenerCupon(int id)
        => _almacen.Cupones.PorId(id) ?? throw NoEncontradoException.Cupon(id);

    public Cupon CrearCupon(
        string codigo,
        TipoCupon tipo,
        decimal valor,
        DateTime desde,
        DateTime hasta,
        decimal minimoCompra,
        decimal topeDescuento,
        int usosMaximos,
        string descripcion)
    {
        if (_almacen.Cupones.PorCodigo(codigo) is not null)
        {
            throw new ReglaNegocioException(
                $"Ya existe un cupón con el código {codigo.Trim().ToUpperInvariant()}.");
        }

        try
        {
            var cupon = new Cupon(
                codigo, tipo, valor, desde, hasta, minimoCompra, topeDescuento, usosMaximos, descripcion);

            _almacen.Cupones.Guardar(cupon);
            _almacen.Confirmar();

            return cupon;
        }
        catch (ArgumentException ex)
        {
            throw new ReglaNegocioException(ex.Message, ex);
        }
    }

    public Cupon AlternarCupon(int id)
    {
        var cupon = ObtenerCupon(id);

        if (cupon.Activo)
        {
            cupon.Desactivar();
        }
        else
        {
            cupon.Activar();
        }

        _almacen.Cupones.Guardar(cupon);
        _almacen.Confirmar();

        return cupon;
    }

    public void EliminarCupon(int id)
    {
        var cupon = ObtenerCupon(id);

        if (cupon.UsosRealizados > 0)
        {
            throw new ReglaNegocioException(
                $"El cupón {cupon.Codigo} ya se usó {cupon.UsosRealizados} vez/veces: " +
                "desactivalo en lugar de borrarlo, así los pedidos que lo citan siguen teniendo sentido.");
        }

        _almacen.Cupones.Eliminar(id);
        _almacen.Confirmar();
    }

    /* ===================================================================
       VALIDACIONES INTERNAS
       =================================================================== */

    private void ExigirCategoria(int categoriaId)
    {
        if (_almacen.Categorias.PorId(categoriaId) is null)
        {
            throw NoEncontradoException.Categoria(categoriaId);
        }
    }

    private void ExigirCodigoLibre(string codigo)
    {
        if (_almacen.Productos.PorCodigo(codigo) is not null)
        {
            throw new ReglaNegocioException(
                $"Ya existe un producto con el código {codigo.Trim().ToUpperInvariant()}.");
        }
    }

    private void ExigirObjetivo(AlcancePromocion alcance, int objetivoId)
    {
        if (alcance == AlcancePromocion.Categoria)
        {
            ExigirCategoria(objetivoId);
        }
        else if (_almacen.Productos.PorId(objetivoId) is null)
        {
            throw NoEncontradoException.Producto(objetivoId);
        }
    }
}
