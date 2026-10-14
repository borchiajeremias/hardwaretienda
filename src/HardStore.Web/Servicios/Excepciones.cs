namespace HardStore.Web.Servicios;

/// <summary>
/// CAPA: Servicios · Se rompió una regla del negocio: vender sin stock, repetir
/// un código, usar un cupón vencido.
///
/// El mensaje está escrito para que lo lea un usuario, porque los controladores
/// lo muestran tal cual. En una API sería un 409.
/// </summary>
public class ReglaNegocioException : Exception
{
    public ReglaNegocioException(string mensaje) : base(mensaje) { }
    public ReglaNegocioException(string mensaje, Exception interna) : base(mensaje, interna) { }
}

/// <summary>
/// CAPA: Servicios · Se pidió algo por id o por código y no existe. En una API
/// sería un 404, y por eso está separada de la anterior: quien la atrapa
/// necesita distinguirlas sin leer el mensaje.
/// </summary>
public class NoEncontradoException : Exception
{
    public NoEncontradoException(string mensaje) : base(mensaje) { }

    public static NoEncontradoException Producto(object clave)
        => new($"No existe el producto «{clave}».");

    public static NoEncontradoException Categoria(object clave)
        => new($"No existe la categoría «{clave}».");

    public static NoEncontradoException Pedido(object clave)
        => new($"No existe el pedido «{clave}».");

    public static NoEncontradoException Promocion(object clave)
        => new($"No existe la promoción «{clave}».");

    public static NoEncontradoException Cupon(object clave)
        => new($"No existe el cupón «{clave}».");

    public static NoEncontradoException Usuario(object clave)
        => new($"No existe el usuario «{clave}».");
}
