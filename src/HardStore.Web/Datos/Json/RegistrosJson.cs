using HardStore.Web.Datos.Memoria;
using HardStore.Web.Dominio;

namespace HardStore.Web.Datos.Json;

/* ===================================================================
   DTO planos para el archivo.

   Por qué no se serializan las entidades directamente: sus propiedades
   tienen setter privado, así que el deserializador no podría escribirlas
   sin abrirlas al resto del programa — que es justo lo que la capa de
   dominio quiere evitar.

   Con estos registros el archivo también queda estable: si mañana una
   entidad cambia un método interno, el formato del JSON no se mueve.
   =================================================================== */

internal sealed record ProductoJson(
    int Id, string Codigo, string Nombre, int CategoriaId, decimal Precio,
    int Stock, int StockMinimo, string Descripcion, string Marca,
    string? Imagen, bool Destacado, bool Activo, DateTime CreadoEn)
{
    public static ProductoJson De(Producto p) => new(
        p.Id, p.Codigo, p.Nombre, p.CategoriaId, p.Precio, p.Stock, p.StockMinimo,
        p.Descripcion, p.Marca, p.Imagen, p.Destacado, p.Activo, p.CreadoEn);

    public Producto ADominio() => Producto.Rehidratar(
        Id, Codigo, Nombre, CategoriaId, Precio, Stock, StockMinimo,
        Descripcion ?? "", Marca ?? "", Imagen, Destacado, Activo, CreadoEn);
}

internal sealed record CategoriaJson(int Id, string Nombre, string Slug, string Descripcion)
{
    public static CategoriaJson De(Categoria c) => new(c.Id, c.Nombre, c.Slug, c.Descripcion);

    public Categoria ADominio() => Categoria.Rehidratar(Id, Nombre, Slug, Descripcion ?? "");
}

internal sealed record UsuarioJson(
    int Id, string Nombre, string Email, string PasswordHash, string PasswordSalt,
    string Telefono, string Direccion, RolUsuario Rol, bool Activo, DateTime CreadoEn)
{
    public static UsuarioJson De(Usuario u) => new(
        u.Id, u.Nombre, u.Email, u.PasswordHash, u.PasswordSalt,
        u.Telefono, u.Direccion, u.Rol, u.Activo, u.CreadoEn);

    public Usuario ADominio() => Usuario.Rehidratar(
        Id, Nombre, Email, PasswordHash, PasswordSalt,
        Telefono ?? "", Direccion ?? "", Rol, Activo, CreadoEn);
}

internal sealed record LineaPedidoJson(
    int Id, int PedidoId, int? ProductoId, string Codigo, string Descripcion,
    decimal PrecioUnitario, int Cantidad, int UnidadesBonificadas,
    decimal DescuentoPromocion, string? PromocionAplicada)
{
    public static LineaPedidoJson De(LineaPedido l) => new(
        l.Id, l.PedidoId, l.ProductoId, l.Codigo, l.Descripcion, l.PrecioUnitario,
        l.Cantidad, l.UnidadesBonificadas, l.DescuentoPromocion, l.PromocionAplicada);

    public LineaPedido ADominio() => LineaPedido.Rehidratar(
        Id, PedidoId, ProductoId, Codigo ?? "", Descripcion ?? "", PrecioUnitario,
        Cantidad, UnidadesBonificadas, DescuentoPromocion, PromocionAplicada);
}

internal sealed record PedidoJson(
    int Id, string Codigo, int? UsuarioId, string ClienteNombre, string ClienteEmail,
    string ClienteTelefono, string DireccionEnvio, decimal Bruto, decimal DescuentoPromociones,
    string? CuponCodigo, decimal DescuentoCupon, MedioPago Medio, int Cuotas,
    decimal CoeficientePago, decimal AjustePago, string? MarcaTarjeta, decimal Total,
    EstadoPedido Estado, string Observaciones, DateTime CreadoEn, List<LineaPedidoJson> Lineas)
{
    public static PedidoJson De(Pedido p) => new(
        p.Id, p.Codigo, p.UsuarioId, p.ClienteNombre, p.ClienteEmail, p.ClienteTelefono,
        p.DireccionEnvio, p.Bruto, p.DescuentoPromociones, p.CuponCodigo, p.DescuentoCupon,
        p.Medio, p.Cuotas, p.CoeficientePago, p.AjustePago, p.MarcaTarjeta, p.Total,
        p.Estado, p.Observaciones, p.CreadoEn,
        p.Lineas.Select(LineaPedidoJson.De).ToList());

    public Pedido ADominio() => Pedido.Rehidratar(
        Id, Codigo, UsuarioId, ClienteNombre, ClienteEmail, ClienteTelefono ?? "",
        DireccionEnvio ?? "", Bruto, DescuentoPromociones, CuponCodigo, DescuentoCupon,
        Medio, Cuotas, CoeficientePago, AjustePago, MarcaTarjeta, Total, Estado,
        Observaciones ?? "", CreadoEn,
        (Lineas ?? new List<LineaPedidoJson>()).Select(l => l.ADominio()));
}

internal sealed record PromocionJson(
    int Id, string Nombre, TipoPromocion Tipo, AlcancePromocion Alcance, int ObjetivoId,
    int LlevaN, int PagaM, decimal Porcentaje, DateTime Desde, DateTime Hasta, bool Activa)
{
    public static PromocionJson De(Promocion p) => new(
        p.Id, p.Nombre, p.Tipo, p.Alcance, p.ObjetivoId,
        p.LlevaN, p.PagaM, p.Porcentaje, p.Desde, p.Hasta, p.Activa);

    public Promocion ADominio() => Promocion.Rehidratar(
        Id, Nombre, Tipo, Alcance, ObjetivoId, LlevaN, PagaM, Porcentaje, Desde, Hasta, Activa);
}

internal sealed record CuponJson(
    int Id, string Codigo, string Descripcion, TipoCupon Tipo, decimal Valor,
    decimal MinimoCompra, decimal TopeDescuento, DateTime Desde, DateTime Hasta,
    int UsosMaximos, int UsosRealizados, bool Activo)
{
    public static CuponJson De(Cupon c) => new(
        c.Id, c.Codigo, c.Descripcion, c.Tipo, c.Valor, c.MinimoCompra, c.TopeDescuento,
        c.Desde, c.Hasta, c.UsosMaximos, c.UsosRealizados, c.Activo);

    public Cupon ADominio() => Cupon.Rehidratar(
        Id, Codigo, Descripcion ?? "", Tipo, Valor, MinimoCompra, TopeDescuento,
        Desde, Hasta, UsosMaximos, UsosRealizados, Activo);
}

/// <summary>
/// Todo el contenido del archivo, incluidos los contadores de id: sin ellos,
/// al reiniciar el programa los ids arrancarían de nuevo en 1 y se pisarían
/// con los ya guardados.
/// </summary>
internal sealed class RegistroArchivo
{
    public int Version { get; set; } = 1;

    public List<ProductoJson> Productos { get; set; } = new();
    public List<CategoriaJson> Categorias { get; set; } = new();
    public List<UsuarioJson> Usuarios { get; set; } = new();
    public List<PedidoJson> Pedidos { get; set; } = new();
    public List<PromocionJson> Promociones { get; set; } = new();
    public List<CuponJson> Cupones { get; set; } = new();

    public int SiguienteProducto { get; set; } = 1;
    public int SiguienteCategoria { get; set; } = 1;
    public int SiguienteUsuario { get; set; } = 1;
    public int SiguientePedido { get; set; } = 1;
    public int SiguienteLinea { get; set; } = 1;
    public int SiguientePromocion { get; set; } = 1;
    public int SiguienteCupon { get; set; } = 1;

    public static RegistroArchivo Desde(DatosEnMemoria d) => new()
    {
        Productos = d.Productos.Select(ProductoJson.De).ToList(),
        Categorias = d.Categorias.Select(CategoriaJson.De).ToList(),
        Usuarios = d.Usuarios.Select(UsuarioJson.De).ToList(),
        Pedidos = d.Pedidos.Select(PedidoJson.De).ToList(),
        Promociones = d.Promociones.Select(PromocionJson.De).ToList(),
        Cupones = d.Cupones.Select(CuponJson.De).ToList(),
        SiguienteProducto = d.SiguienteProducto,
        SiguienteCategoria = d.SiguienteCategoria,
        SiguienteUsuario = d.SiguienteUsuario,
        SiguientePedido = d.SiguientePedido,
        SiguienteLinea = d.SiguienteLinea,
        SiguientePromocion = d.SiguientePromocion,
        SiguienteCupon = d.SiguienteCupon,
    };

    public DatosEnMemoria AMemoria()
    {
        var d = new DatosEnMemoria();

        d.Productos.AddRange(Productos.Select(p => p.ADominio()));
        d.Categorias.AddRange(Categorias.Select(c => c.ADominio()));
        d.Usuarios.AddRange(Usuarios.Select(u => u.ADominio()));
        d.Pedidos.AddRange(Pedidos.Select(p => p.ADominio()));
        d.Promociones.AddRange(Promociones.Select(p => p.ADominio()));
        d.Cupones.AddRange(Cupones.Select(c => c.ADominio()));

        // Los contadores se recalculan como «el mayor id + 1» además de leerse
        // del archivo: si alguien editó el JSON a mano y dejó los contadores
        // atrasados, así no se generan ids duplicados.
        d.SiguienteProducto = Math.Max(SiguienteProducto, Maximo(d.Productos.Select(p => p.Id)));
        d.SiguienteCategoria = Math.Max(SiguienteCategoria, Maximo(d.Categorias.Select(c => c.Id)));
        d.SiguienteUsuario = Math.Max(SiguienteUsuario, Maximo(d.Usuarios.Select(u => u.Id)));
        d.SiguientePedido = Math.Max(SiguientePedido, Maximo(d.Pedidos.Select(p => p.Id)));
        d.SiguienteLinea = Math.Max(SiguienteLinea, Maximo(d.Pedidos.SelectMany(p => p.Lineas).Select(l => l.Id)));
        d.SiguientePromocion = Math.Max(SiguientePromocion, Maximo(d.Promociones.Select(p => p.Id)));
        d.SiguienteCupon = Math.Max(SiguienteCupon, Maximo(d.Cupones.Select(c => c.Id)));

        return d;
    }

    private static int Maximo(IEnumerable<int> ids)
    {
        var maximo = 0;
        foreach (var id in ids)
        {
            if (id > maximo)
            {
                maximo = id;
            }
        }
        return maximo + 1;
    }
}
