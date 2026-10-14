#if CON_EF_CORE
using HardStore.Web.Dominio;

namespace HardStore.Web.Datos.EfCore;

/* ===================================================================
   Filas de la base: una clase por tabla.

   Mismo criterio que los DTO del almacén JSON (ver Datos/Json/RegistrosJson.cs):
   las entidades del dominio tienen setter privado a propósito, así que EF Core
   no puede materializarlas directamente sin abrirlas al resto del programa.
   Estas clases son «tontas» — propiedades públicas y nada más — y se traducen
   a dominio con ADominio() / De().

   El esquema queda además desacoplado del dominio: si mañana Producto cambia
   un método, la tabla no se mueve.
   =================================================================== */

/// <summary>Lo único que el volcado genérico necesita saber de una fila.</summary>
internal interface IFilaConId
{
    int Id { get; }
}

internal sealed class ProductoFila : IFilaConId
{
    public int Id { get; set; }
    public string Codigo { get; set; } = "";
    public string Nombre { get; set; } = "";
    public int CategoriaId { get; set; }
    public decimal Precio { get; set; }
    public int Stock { get; set; }
    public int StockMinimo { get; set; }
    public string Descripcion { get; set; } = "";
    public string Marca { get; set; } = "";
    public string? Imagen { get; set; }
    public bool Destacado { get; set; }
    public bool Activo { get; set; }
    public DateTime CreadoEn { get; set; }

    public static ProductoFila De(Producto p) => new()
    {
        Id = p.Id, Codigo = p.Codigo, Nombre = p.Nombre, CategoriaId = p.CategoriaId,
        Precio = p.Precio, Stock = p.Stock, StockMinimo = p.StockMinimo,
        Descripcion = p.Descripcion, Marca = p.Marca, Imagen = p.Imagen,
        Destacado = p.Destacado, Activo = p.Activo, CreadoEn = p.CreadoEn,
    };

    public void Copiar(Producto p)
    {
        Codigo = p.Codigo; Nombre = p.Nombre; CategoriaId = p.CategoriaId;
        Precio = p.Precio; Stock = p.Stock; StockMinimo = p.StockMinimo;
        Descripcion = p.Descripcion; Marca = p.Marca; Imagen = p.Imagen;
        Destacado = p.Destacado; Activo = p.Activo; CreadoEn = p.CreadoEn;
    }

    public Producto ADominio() => Producto.Rehidratar(
        Id, Codigo, Nombre, CategoriaId, Precio, Stock, StockMinimo,
        Descripcion, Marca, Imagen, Destacado, Activo, CreadoEn);
}

internal sealed class CategoriaFila : IFilaConId
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Descripcion { get; set; } = "";

    public static CategoriaFila De(Categoria c) => new()
    {
        Id = c.Id, Nombre = c.Nombre, Slug = c.Slug, Descripcion = c.Descripcion,
    };

    public void Copiar(Categoria c)
    {
        Nombre = c.Nombre; Slug = c.Slug; Descripcion = c.Descripcion;
    }

    public Categoria ADominio() => Categoria.Rehidratar(Id, Nombre, Slug, Descripcion);
}

internal sealed class UsuarioFila : IFilaConId
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string PasswordSalt { get; set; } = "";
    public string Telefono { get; set; } = "";
    public string Direccion { get; set; } = "";
    public RolUsuario Rol { get; set; }
    public bool Activo { get; set; }
    public DateTime CreadoEn { get; set; }

    public static UsuarioFila De(Usuario u) => new()
    {
        Id = u.Id, Nombre = u.Nombre, Email = u.Email,
        PasswordHash = u.PasswordHash, PasswordSalt = u.PasswordSalt,
        Telefono = u.Telefono, Direccion = u.Direccion,
        Rol = u.Rol, Activo = u.Activo, CreadoEn = u.CreadoEn,
    };

    public void Copiar(Usuario u)
    {
        Nombre = u.Nombre; Email = u.Email;
        PasswordHash = u.PasswordHash; PasswordSalt = u.PasswordSalt;
        Telefono = u.Telefono; Direccion = u.Direccion;
        Rol = u.Rol; Activo = u.Activo; CreadoEn = u.CreadoEn;
    }

    public Usuario ADominio() => Usuario.Rehidratar(
        Id, Nombre, Email, PasswordHash, PasswordSalt,
        Telefono, Direccion, Rol, Activo, CreadoEn);
}

internal sealed class LineaPedidoFila : IFilaConId
{
    public int Id { get; set; }
    public int PedidoId { get; set; }
    public int? ProductoId { get; set; }
    public string Codigo { get; set; } = "";
    public string Descripcion { get; set; } = "";
    public decimal PrecioUnitario { get; set; }
    public int Cantidad { get; set; }
    public int UnidadesBonificadas { get; set; }
    public decimal DescuentoPromocion { get; set; }
    public string? PromocionAplicada { get; set; }

    public static LineaPedidoFila De(LineaPedido l) => new()
    {
        Id = l.Id, PedidoId = l.PedidoId, ProductoId = l.ProductoId,
        Codigo = l.Codigo, Descripcion = l.Descripcion,
        PrecioUnitario = l.PrecioUnitario, Cantidad = l.Cantidad,
        UnidadesBonificadas = l.UnidadesBonificadas,
        DescuentoPromocion = l.DescuentoPromocion,
        PromocionAplicada = l.PromocionAplicada,
    };

    public LineaPedido ADominio() => LineaPedido.Rehidratar(
        Id, PedidoId, ProductoId, Codigo, Descripcion, PrecioUnitario,
        Cantidad, UnidadesBonificadas, DescuentoPromocion, PromocionAplicada);
}

internal sealed class PedidoFila : IFilaConId
{
    public int Id { get; set; }
    public string Codigo { get; set; } = "";
    public int? UsuarioId { get; set; }
    public string ClienteNombre { get; set; } = "";
    public string ClienteEmail { get; set; } = "";
    public string ClienteTelefono { get; set; } = "";
    public string DireccionEnvio { get; set; } = "";
    public decimal Bruto { get; set; }
    public decimal DescuentoPromociones { get; set; }
    public string? CuponCodigo { get; set; }
    public decimal DescuentoCupon { get; set; }
    public MedioPago Medio { get; set; }
    public int Cuotas { get; set; }
    public decimal CoeficientePago { get; set; }
    public decimal AjustePago { get; set; }

    /// <summary>
    /// Sólo la MARCA de la tarjeta («Visa», «Mastercard»…). No existe ninguna
    /// columna para el número, el vencimiento ni el código de seguridad: el
    /// sistema no los pide en ninguna pantalla y no los podría guardar aunque
    /// llegaran.
    /// </summary>
    public string? MarcaTarjeta { get; set; }

    public decimal Total { get; set; }
    public EstadoPedido Estado { get; set; }
    public string Observaciones { get; set; } = "";
    public DateTime CreadoEn { get; set; }

    public List<LineaPedidoFila> Lineas { get; set; } = new();

    public static PedidoFila De(Pedido p) => new()
    {
        Id = p.Id, Codigo = p.Codigo, UsuarioId = p.UsuarioId,
        ClienteNombre = p.ClienteNombre, ClienteEmail = p.ClienteEmail,
        ClienteTelefono = p.ClienteTelefono, DireccionEnvio = p.DireccionEnvio,
        Bruto = p.Bruto, DescuentoPromociones = p.DescuentoPromociones,
        CuponCodigo = p.CuponCodigo, DescuentoCupon = p.DescuentoCupon,
        Medio = p.Medio, Cuotas = p.Cuotas, CoeficientePago = p.CoeficientePago,
        AjustePago = p.AjustePago, MarcaTarjeta = p.MarcaTarjeta, Total = p.Total,
        Estado = p.Estado, Observaciones = p.Observaciones, CreadoEn = p.CreadoEn,
        Lineas = p.Lineas.Select(LineaPedidoFila.De).ToList(),
    };

    /// <summary>
    /// De un pedido ya guardado sólo cambian el estado y las observaciones
    /// (cancelar, marcar enviado…). Los importes y las líneas quedan
    /// congelados: es lo que el cliente aceptó pagar.
    /// </summary>
    public void Copiar(Pedido p)
    {
        Estado = p.Estado;
        Observaciones = p.Observaciones;
    }

    public Pedido ADominio() => Pedido.Rehidratar(
        Id, Codigo, UsuarioId, ClienteNombre, ClienteEmail, ClienteTelefono,
        DireccionEnvio, Bruto, DescuentoPromociones, CuponCodigo, DescuentoCupon,
        Medio, Cuotas, CoeficientePago, AjustePago, MarcaTarjeta, Total, Estado,
        Observaciones, CreadoEn, Lineas.Select(l => l.ADominio()));
}

internal sealed class PromocionFila : IFilaConId
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public TipoPromocion Tipo { get; set; }
    public AlcancePromocion Alcance { get; set; }
    public int ObjetivoId { get; set; }
    public int LlevaN { get; set; }
    public int PagaM { get; set; }
    public decimal Porcentaje { get; set; }
    public DateTime Desde { get; set; }
    public DateTime Hasta { get; set; }
    public bool Activa { get; set; }

    public static PromocionFila De(Promocion p) => new()
    {
        Id = p.Id, Nombre = p.Nombre, Tipo = p.Tipo, Alcance = p.Alcance,
        ObjetivoId = p.ObjetivoId, LlevaN = p.LlevaN, PagaM = p.PagaM,
        Porcentaje = p.Porcentaje, Desde = p.Desde, Hasta = p.Hasta, Activa = p.Activa,
    };

    public void Copiar(Promocion p)
    {
        Nombre = p.Nombre; Tipo = p.Tipo; Alcance = p.Alcance;
        ObjetivoId = p.ObjetivoId; LlevaN = p.LlevaN; PagaM = p.PagaM;
        Porcentaje = p.Porcentaje; Desde = p.Desde; Hasta = p.Hasta; Activa = p.Activa;
    }

    public Promocion ADominio() => Promocion.Rehidratar(
        Id, Nombre, Tipo, Alcance, ObjetivoId, LlevaN, PagaM, Porcentaje, Desde, Hasta, Activa);
}

internal sealed class CuponFila : IFilaConId
{
    public int Id { get; set; }
    public string Codigo { get; set; } = "";
    public string Descripcion { get; set; } = "";
    public TipoCupon Tipo { get; set; }
    public decimal Valor { get; set; }
    public decimal MinimoCompra { get; set; }
    public decimal TopeDescuento { get; set; }
    public DateTime Desde { get; set; }
    public DateTime Hasta { get; set; }
    public int UsosMaximos { get; set; }
    public int UsosRealizados { get; set; }
    public bool Activo { get; set; }

    public static CuponFila De(Cupon c) => new()
    {
        Id = c.Id, Codigo = c.Codigo, Descripcion = c.Descripcion, Tipo = c.Tipo,
        Valor = c.Valor, MinimoCompra = c.MinimoCompra, TopeDescuento = c.TopeDescuento,
        Desde = c.Desde, Hasta = c.Hasta, UsosMaximos = c.UsosMaximos,
        UsosRealizados = c.UsosRealizados, Activo = c.Activo,
    };

    public void Copiar(Cupon c)
    {
        Codigo = c.Codigo; Descripcion = c.Descripcion; Tipo = c.Tipo;
        Valor = c.Valor; MinimoCompra = c.MinimoCompra; TopeDescuento = c.TopeDescuento;
        Desde = c.Desde; Hasta = c.Hasta; UsosMaximos = c.UsosMaximos;
        UsosRealizados = c.UsosRealizados; Activo = c.Activo;
    }

    public Cupon ADominio() => Cupon.Rehidratar(
        Id, Codigo, Descripcion, Tipo, Valor, MinimoCompra, TopeDescuento,
        Desde, Hasta, UsosMaximos, UsosRealizados, Activo);
}
#endif
