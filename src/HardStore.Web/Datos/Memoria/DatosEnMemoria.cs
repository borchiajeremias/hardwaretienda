using HardStore.Web.Dominio;

namespace HardStore.Web.Datos.Memoria;

/// <summary>
/// Estado en memoria compartido por los seis repositorios: las listas y los
/// contadores que reemplazan al AUTO_INCREMENT de la base.
/// </summary>
public sealed class DatosEnMemoria
{
    public List<Producto> Productos { get; } = new();
    public List<Categoria> Categorias { get; } = new();
    public List<Usuario> Usuarios { get; } = new();
    public List<Pedido> Pedidos { get; } = new();
    public List<Promocion> Promociones { get; } = new();
    public List<Cupon> Cupones { get; } = new();

    public int SiguienteProducto { get; set; } = 1;
    public int SiguienteCategoria { get; set; } = 1;
    public int SiguienteUsuario { get; set; } = 1;
    public int SiguientePedido { get; set; } = 1;
    public int SiguienteLinea { get; set; } = 1;
    public int SiguientePromocion { get; set; } = 1;
    public int SiguienteCupon { get; set; } = 1;
}
