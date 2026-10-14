using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using HardStore.Web.Dominio;
using HardStore.Web.Dominio.Precios;

namespace HardStore.Web.Modelos;

/* ===================================================================
   Modelos de vista: lo que viaja entre los controladores y las vistas.

   Están separados de las entidades a propósito. Si las vistas ataran sus
   formularios directamente a Producto, cualquier campo que el navegador
   mande se escribiría en la entidad — el problema clásico de
   «over-posting», por el que alguien podría mandar Stock=9999 en el
   formulario de edición y modificar el stock sin pasar por el servicio.
   =================================================================== */

/// <summary>Datos del catálogo público.</summary>
public sealed class CatalogoVm
{
    public IReadOnlyList<Producto> Productos { get; init; } = Array.Empty<Producto>();
    public IReadOnlyList<(Categoria Categoria, int Cantidad)> Categorias { get; init; }
        = Array.Empty<(Categoria, int)>();

    /// <summary>productoId → etiqueta de promoción («3x2», «15 % OFF»).</summary>
    public Dictionary<int, string> Promociones { get; init; } = new();

    public Dictionary<int, string> NombresCategoria { get; init; } = new();

    public int Total { get; init; }
    public int Pagina { get; init; }
    public int Paginas { get; init; }

    public string? CategoriaSlug { get; init; }
    public string? Busqueda { get; init; }
    public decimal? PrecioMin { get; init; }
    public decimal? PrecioMax { get; init; }
    public string Orden { get; init; } = "destacados";
    public bool SoloConStock { get; init; }

    public decimal PrecioMinimoCatalogo { get; init; }
    public decimal PrecioMaximoCatalogo { get; init; }

    public IReadOnlyList<PlanPago> Planes { get; init; } = PlanesPago.Todos;
}

/// <summary>Formulario de alta y edición de producto en el panel.</summary>
public sealed class ProductoFormVm
{
    public int Id { get; set; }

    [Required(ErrorMessage = "El código es obligatorio.")]
    [StringLength(20, MinimumLength = 2, ErrorMessage = "El código debe tener entre 2 y 20 caracteres.")]
    [RegularExpression("^[A-Za-z0-9-]+$", ErrorMessage = "El código admite sólo letras, números y guiones.")]
    public string Codigo { get; set; } = "";

    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [StringLength(150, MinimumLength = 2, ErrorMessage = "El nombre debe tener entre 2 y 150 caracteres.")]
    public string Nombre { get; set; } = "";

    [Required(ErrorMessage = "Elegí una categoría.")]
    [Range(1, int.MaxValue, ErrorMessage = "Elegí una categoría.")]
    public int CategoriaId { get; set; }

    [Range(0.01, 999999999, ErrorMessage = "El precio debe ser mayor a cero.")]
    public decimal Precio { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "El stock no puede ser negativo.")]
    public int Stock { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "El stock mínimo no puede ser negativo.")]
    public int StockMinimo { get; set; }

    [StringLength(1000)]
    public string? Descripcion { get; set; } = "";

    [StringLength(60)]
    public string? Marca { get; set; } = "";

    public bool Destacado { get; set; }
    public bool Activo { get; set; } = true;

    /// <summary>La imagen que se está subiendo. Null si no se cambia.</summary>
    public IFormFile? Imagen { get; set; }

    public bool QuitarImagen { get; set; }

    /// <summary>Ruta de la imagen actual, para mostrar la vista previa.</summary>
    public string? ImagenActual { get; set; }

    [BindNever]
    [ValidateNever]
    public IReadOnlyList<Categoria> Categorias { get; set; } = Array.Empty<Categoria>();

    public bool EsNuevo => Id == 0;
}

/// <summary>Login.</summary>
public sealed class IngresarVm
{
    [Required(ErrorMessage = "Ingresá tu email.")]
    [EmailAddress(ErrorMessage = "El email no es válido.")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Ingresá tu contraseña.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = "";

    public bool Recordarme { get; set; } = true;

    public string? VolverA { get; set; }
}

/// <summary>Registro de cliente.</summary>
public sealed class RegistrarseVm
{
    [Required(ErrorMessage = "Ingresá tu nombre y apellido.")]
    [StringLength(100, MinimumLength = 2)]
    public string Nombre { get; set; } = "";

    [Required(ErrorMessage = "Ingresá tu email.")]
    [EmailAddress(ErrorMessage = "El email no es válido.")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Elegí una contraseña.")]
    [MinLength(8, ErrorMessage = "La contraseña debe tener al menos 8 caracteres.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = "";

    [Compare(nameof(Password), ErrorMessage = "Las contraseñas no coinciden.")]
    [DataType(DataType.Password)]
    public string PasswordRepetida { get; set; } = "";

    [Phone(ErrorMessage = "El teléfono no es válido.")]
    public string? Telefono { get; set; } = "";

    [StringLength(255)]
    public string? Direccion { get; set; } = "";
}

/// <summary>El checkout completo: cotización + datos del comprador.</summary>
public sealed class CheckoutVm
{
    [BindNever]
    [ValidateNever]
    public Cotizacion Cotizacion { get; set; } = new();

    [BindNever]
    [ValidateNever]
    public IReadOnlyList<(PlanPago Plan, decimal Total, decimal ValorCuota)> Comparativa { get; set; }
        = Array.Empty<(PlanPago, decimal, decimal)>();

    [Required(ErrorMessage = "Ingresá tu nombre y apellido.")]
    [StringLength(120, MinimumLength = 2)]
    public string Nombre { get; set; } = "";

    [Required(ErrorMessage = "Ingresá tu email.")]
    [EmailAddress(ErrorMessage = "El email no es válido.")]
    public string Email { get; set; } = "";

    public string? Telefono { get; set; } = "";

    public string? Direccion { get; set; } = "";

    [StringLength(500)]
    public string? Observaciones { get; set; } = "";

    /// <summary>Clave del plan elegido, con el formato «TarjetaCredito:6».</summary>
    public string? PlanPago { get; set; } = "";

    /// <summary>Marca de la tarjeta. Sólo el nombre: nunca se piden datos de la tarjeta.</summary>
    public string? MarcaTarjeta { get; set; }

    public string? Cupon { get; set; }

    /// <summary>El carrito serializado, que el JavaScript copia desde localStorage.</summary>
    public string ItemsJson { get; set; } = "[]";

    [BindNever]
    [ValidateNever]
    public IReadOnlyList<PlanPago> Planes { get; init; } = PlanesPago.Todos;

    [BindNever]
    [ValidateNever]
    public IReadOnlyList<string> Marcas { get; init; } = PlanesPago.MarcasTarjeta;
}

/// <summary>Tablero del panel.</summary>
public sealed class PanelVm
{
    public Dictionary<string, decimal> Metricas { get; init; } = new();
    public IReadOnlyList<Producto> AReponer { get; init; } = Array.Empty<Producto>();
    public IReadOnlyList<Pedido> UltimosPedidos { get; init; } = Array.Empty<Pedido>();
    public int Promociones { get; init; }
    public int Cupones { get; init; }
}

/// <summary>Formulario de promoción.</summary>
public sealed class PromocionFormVm
{
    [Required(ErrorMessage = "Ponele un nombre a la promoción.")]
    [StringLength(80, MinimumLength = 2)]
    public string Nombre { get; set; } = "";

    public TipoPromocion Tipo { get; set; } = TipoPromocion.LlevaNPagaM;
    public AlcancePromocion Alcance { get; set; } = AlcancePromocion.Producto;

    [Range(1, int.MaxValue, ErrorMessage = "Elegí a qué se aplica.")]
    public int ObjetivoId { get; set; }

    [Range(2, 20, ErrorMessage = "Las unidades que se llevan van de 2 a 20.")]
    public int LlevaN { get; set; } = 2;

    [Range(1, 19, ErrorMessage = "Las unidades que se pagan van de 1 a 19.")]
    public int PagaM { get; set; } = 1;

    [Range(0, 100, ErrorMessage = "El porcentaje va de 0 a 100.")]
    public decimal Porcentaje { get; set; } = 10m;

    [DataType(DataType.Date)]
    public DateTime Desde { get; set; } = DateTime.Today;

    [DataType(DataType.Date)]
    public DateTime Hasta { get; set; } = DateTime.Today.AddMonths(1);

    [BindNever]
    [ValidateNever]
    public IReadOnlyList<Producto> Productos { get; set; } = Array.Empty<Producto>();
    [BindNever]
    [ValidateNever]
    public IReadOnlyList<Categoria> Categorias { get; set; } = Array.Empty<Categoria>();
}

/// <summary>Formulario de cupón.</summary>
public sealed class CuponFormVm
{
    [Required(ErrorMessage = "Ingresá el código.")]
    [StringLength(25, MinimumLength = 3)]
    [RegularExpression("^[A-Za-z0-9]+$", ErrorMessage = "El código admite sólo letras y números.")]
    public string Codigo { get; set; } = "";

    public TipoCupon Tipo { get; set; } = TipoCupon.Porcentaje;

    [Range(0.01, 99999999, ErrorMessage = "El valor debe ser mayor a cero.")]
    public decimal Valor { get; set; } = 10m;

    [Range(0, 99999999)]
    public decimal MinimoCompra { get; set; }

    [Range(0, 99999999)]
    public decimal TopeDescuento { get; set; }

    [Range(0, 100000, ErrorMessage = "Los usos máximos no pueden ser negativos.")]
    public int UsosMaximos { get; set; }

    [DataType(DataType.Date)]
    public DateTime Desde { get; set; } = DateTime.Today;

    [DataType(DataType.Date)]
    public DateTime Hasta { get; set; } = DateTime.Today.AddMonths(1);

    [StringLength(200)]
    public string? Descripcion { get; set; } = "";
}
