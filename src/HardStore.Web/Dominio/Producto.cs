namespace HardStore.Web.Dominio;

/// <summary>
/// CAPA: Dominio · Un producto del catálogo.
///
/// Igual que en la versión PHP, la entidad protege sus propias invariantes: no
/// tiene setters públicos libres, así que un producto con precio negativo o
/// stock negativo no puede existir, sin importar quién lo intente.
///
/// Esta clase no conoce EF Core, ni el JSON, ni los controladores: es la capa
/// más interna. Las propiedades tienen setter privado para que EF Core y
/// System.Text.Json puedan materializarla, pero el resto del programa sólo
/// puede cambiarlas a través de los métodos que validan.
/// </summary>
public class Producto
{
    public int Id { get; private set; }

    public string Codigo { get; private set; } = "";
    public string Nombre { get; private set; } = "";
    public string Descripcion { get; private set; } = "";
    public string Marca { get; private set; } = "";

    public int CategoriaId { get; private set; }

    public decimal Precio { get; private set; }
    public int Stock { get; private set; }
    public int StockMinimo { get; private set; }

    /// <summary>Ruta relativa dentro de wwwroot, o null si todavía no tiene foto.</summary>
    public string? Imagen { get; private set; }

    public bool Destacado { get; private set; }
    public bool Activo { get; private set; } = true;

    public DateTime CreadoEn { get; private set; } = DateTime.Now;

    /// <summary>Constructor sin argumentos que necesitan EF Core y el deserializador.</summary>
    private Producto() { }

    /// <summary>
    /// Reconstruye la entidad tal como estaba guardada. La usa SÓLO la capa de
    /// datos al leer del archivo o de la base: es la contracara del constructor
    /// público, que valida datos nuevos. Acá los datos ya fueron validados
    /// cuando se crearon, así que se restauran tal cual — incluidas la fecha de
    /// creación y las banderas que no tienen setter público.
    /// </summary>
    internal static Producto Rehidratar(
        int id, string codigo, string nombre, int categoriaId, decimal precio,
        int stock, int stockMinimo, string descripcion, string marca,
        string? imagen, bool destacado, bool activo, DateTime creadoEn)
        => new()
        {
            Id = id,
            Codigo = codigo,
            Nombre = nombre,
            CategoriaId = categoriaId,
            Precio = precio,
            Stock = stock,
            StockMinimo = stockMinimo,
            Descripcion = descripcion,
            Marca = marca,
            Imagen = imagen,
            Destacado = destacado,
            Activo = activo,
            CreadoEn = creadoEn,
        };

    public Producto(
        string codigo,
        string nombre,
        int categoriaId,
        decimal precio,
        int stock = 0,
        int stockMinimo = 0,
        string descripcion = "",
        string marca = "",
        bool destacado = false)
    {
        CambiarCodigo(codigo);
        CambiarNombre(nombre);
        CambiarPrecio(precio);
        CambiarStockMinimo(stockMinimo);
        CambiarDescripcion(descripcion);
        CambiarMarca(marca);

        if (stock < 0)
        {
            throw new ArgumentException("El stock inicial no puede ser negativo.");
        }
        if (categoriaId <= 0)
        {
            throw new ArgumentException("El producto debe pertenecer a una categoría.");
        }

        CategoriaId = categoriaId;
        Stock = stock;
        Destacado = destacado;
    }

    /// <summary>Lo asigna el almacén al persistir. Una vez puesto, no cambia.</summary>
    public void AsignarId(int id)
    {
        if (Id != 0)
        {
            throw new InvalidOperationException("El producto ya tiene un id asignado.");
        }
        Id = id;
    }

    /* ------------------------- Datos del producto ------------------------ */

    public void CambiarCodigo(string codigo)
    {
        codigo = (codigo ?? "").Trim().ToUpperInvariant();

        if (codigo.Length < 2 || codigo.Length > 20)
        {
            throw new ArgumentException("El código debe tener entre 2 y 20 caracteres.");
        }
        if (!codigo.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
        {
            throw new ArgumentException("El código admite sólo letras, números y guiones.");
        }

        Codigo = codigo;
    }

    public void CambiarNombre(string nombre)
    {
        nombre = (nombre ?? "").Trim();

        if (nombre.Length < 2 || nombre.Length > 150)
        {
            throw new ArgumentException("El nombre debe tener entre 2 y 150 caracteres.");
        }

        Nombre = nombre;
    }

    public void CambiarDescripcion(string? descripcion)
    {
        Descripcion = (descripcion ?? "").Trim();
    }

    public void CambiarMarca(string? marca)
    {
        Marca = (marca ?? "").Trim();
    }

    public void CambiarPrecio(decimal precio)
    {
        if (precio <= 0)
        {
            throw new ArgumentException("El precio debe ser mayor a cero.");
        }

        // El dinero se guarda con dos decimales, nunca con más precisión.
        Precio = Math.Round(precio, 2, MidpointRounding.AwayFromZero);
    }

    public void CambiarCategoria(int categoriaId)
    {
        if (categoriaId <= 0)
        {
            throw new ArgumentException("Categoría inválida.");
        }
        CategoriaId = categoriaId;
    }

    public void CambiarStockMinimo(int minimo)
    {
        if (minimo < 0)
        {
            throw new ArgumentException("El stock mínimo no puede ser negativo.");
        }
        StockMinimo = minimo;
    }

    public void CambiarDestacado(bool destacado) => Destacado = destacado;

    /// <summary>
    /// Asocia una imagen ya guardada por ImagenServicio. La entidad no sabe
    /// nada de archivos ni de HTTP: sólo guarda la ruta que le pasan.
    /// </summary>
    public void CambiarImagen(string? rutaRelativa)
    {
        rutaRelativa = string.IsNullOrWhiteSpace(rutaRelativa) ? null : rutaRelativa.Trim();

        if (rutaRelativa is not null && rutaRelativa.StartsWith('/'))
        {
            // Se normaliza sin barra inicial para que las vistas puedan
            // resolverla con el helper de contenido sin duplicar la barra.
            rutaRelativa = rutaRelativa.TrimStart('/');
        }

        Imagen = rutaRelativa;
    }

    public void Activar() => Activo = true;
    public void Desactivar() => Activo = false;

    /* ------------------------------- Stock ------------------------------- */

    public bool TieneStockPara(int cantidad) => cantidad > 0 && Stock >= cantidad;

    public void AumentarStock(int cantidad)
    {
        if (cantidad <= 0)
        {
            throw new ArgumentException("La cantidad a ingresar debe ser mayor a cero.");
        }
        Stock += cantidad;
    }

    /// <summary>
    /// Única puerta de salida del stock. Por eso es acá, y no en un servicio,
    /// donde se garantiza que nunca quede negativo.
    /// </summary>
    public void DescontarStock(int cantidad)
    {
        if (cantidad <= 0)
        {
            throw new ArgumentException("La cantidad a descontar debe ser mayor a cero.");
        }
        if (!TieneStockPara(cantidad))
        {
            throw new ArgumentException(
                $"Stock insuficiente de «{Nombre}»: hay {Stock} y se piden {cantidad}.");
        }
        Stock -= cantidad;
    }

    /// <summary>Corrección de inventario: fija el valor exacto contado.</summary>
    public void AjustarStock(int nuevoStock)
    {
        if (nuevoStock < 0)
        {
            throw new ArgumentException("El stock no puede quedar negativo.");
        }
        Stock = nuevoStock;
    }

    /* ----------------------------- Consultas ----------------------------- */

    public bool EstaAgotado => Stock == 0;

    public bool NecesitaReposicion => Stock <= StockMinimo;

    /// <summary>Valor del stock a precio de venta. Las cuentas de dinero son del dominio.</summary>
    public decimal Valorizado => Math.Round(Stock * Precio, 2, MidpointRounding.AwayFromZero);
}
