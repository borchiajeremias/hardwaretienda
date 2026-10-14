using HardStore.Web.Dominio;

namespace HardStore.Web.Datos;

/// <summary>
/// CAPA: Datos · Contratos de persistencia.
///
/// Los servicios dependen de estas interfaces, nunca de una implementación
/// concreta. Ésa es la costura que permite tener dos almacenes intercambiables:
///
///   · <c>Datos/Json</c>   — System.Text.Json, cero paquetes, es el que corre por defecto.
///   · <c>Datos/EfCore</c> — EF Core + SQLite, se activa con /p:ConEfCore=true.
///
/// Ninguno de los servicios ni de los controladores cambia al pasar de uno al
/// otro: sólo la línea de registro en Program.cs.
/// </summary>
public interface IProductoRepositorio
{
    Producto? PorId(int id);
    Producto? PorCodigo(string codigo);
    IReadOnlyList<Producto> Todos(bool soloActivos = false);
    IReadOnlyList<Producto> PorCategoria(int categoriaId);
    IReadOnlyList<Producto> ConStockBajo();
    Producto Guardar(Producto producto);
    bool Eliminar(int id);
    int Contar();
}

public interface ICategoriaRepositorio
{
    Categoria? PorId(int id);
    Categoria? PorSlug(string slug);
    Categoria? PorNombre(string nombre);
    IReadOnlyList<Categoria> Todas();
    Categoria Guardar(Categoria categoria);
    bool Eliminar(int id);
}

public interface IUsuarioRepositorio
{
    Usuario? PorId(int id);
    Usuario? PorEmail(string email);
    IReadOnlyList<Usuario> Todos();
    Usuario Guardar(Usuario usuario);
    bool Eliminar(int id);
    int Contar();
}

public interface IPedidoRepositorio
{
    Pedido? PorId(int id);
    Pedido? PorCodigo(string codigo);
    IReadOnlyList<Pedido> Todos();
    IReadOnlyList<Pedido> PorUsuario(int usuarioId);
    IReadOnlyList<Pedido> ConProducto(int productoId);
    Pedido Guardar(Pedido pedido);
    int Contar();
    /// <summary>Próximo código correlativo, por ejemplo HS-20260905-0007.</summary>
    string SiguienteCodigo();
}

public interface IPromocionRepositorio
{
    Promocion? PorId(int id);
    IReadOnlyList<Promocion> Todas();
    /// <summary>Sólo las activas y dentro de su período de vigencia.</summary>
    IReadOnlyList<Promocion> Vigentes(DateTime? cuando = null);
    IReadOnlyList<Promocion> ConObjetivo(AlcancePromocion alcance, int objetivoId);
    Promocion Guardar(Promocion promocion);
    bool Eliminar(int id);
}

public interface ICuponRepositorio
{
    Cupon? PorId(int id);
    Cupon? PorCodigo(string codigo);
    IReadOnlyList<Cupon> Todos();
    Cupon Guardar(Cupon cupon);
    bool Eliminar(int id);
}

/// <summary>
/// Agrupa los seis repositorios y ofrece el punto de guardado.
///
/// <see cref="Confirmar"/> existe para que los servicios puedan decir «terminé,
/// persistí todo junto». En la implementación JSON escribe el archivo una sola
/// vez al final; en la de EF Core es el SaveChanges de la transacción. Sin este
/// método, un checkout que toca stock, pedido y cupón haría tres escrituras
/// separadas y podría quedar a medias.
/// </summary>
public interface IAlmacen
{
    IProductoRepositorio Productos { get; }
    ICategoriaRepositorio Categorias { get; }
    IUsuarioRepositorio Usuarios { get; }
    IPedidoRepositorio Pedidos { get; }
    IPromocionRepositorio Promociones { get; }
    ICuponRepositorio Cupones { get; }

    /// <summary>Persiste todo lo modificado desde la última confirmación.</summary>
    void Confirmar();
}
