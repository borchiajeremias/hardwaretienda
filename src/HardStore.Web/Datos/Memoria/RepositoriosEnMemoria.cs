using HardStore.Web.Dominio;
using HardStore.Web.Datos;

namespace HardStore.Web.Datos.Memoria;

/* ===================================================================
   Implementaciones de los seis contratos sobre listas en memoria.

   Están acá, y no dentro de la carpeta del almacén JSON, porque las usan
   las DOS implementaciones: el almacén JSON y el de EF Core. Ambos
   trabajan sobre el mismo estado en memoria y se diferencian sólo en
   dónde lo persisten cuando alguien llama a Confirmar().

   Todas toman el mismo objeto de candado: un servidor web atiende varias
   peticiones a la vez, y sin sincronizar, dos checkouts simultáneos
   podrían leer el mismo stock y venderlo dos veces.
   =================================================================== */

internal sealed class ProductoRepositorioEnMemoria : IProductoRepositorio
{
    private readonly DatosEnMemoria _d;
    private readonly object _candado;

    public ProductoRepositorioEnMemoria(DatosEnMemoria datos, object candado)
    {
        _d = datos;
        _candado = candado;
    }

    public Producto? PorId(int id)
    {
        lock (_candado)
        {
            return _d.Productos.FirstOrDefault(p => p.Id == id);
        }
    }

    public Producto? PorCodigo(string codigo)
    {
        var buscado = (codigo ?? "").Trim().ToUpperInvariant();
        lock (_candado)
        {
            return _d.Productos.FirstOrDefault(p => p.Codigo == buscado);
        }
    }

    public IReadOnlyList<Producto> Todos(bool soloActivos = false)
    {
        lock (_candado)
        {
            return _d.Productos
                .Where(p => !soloActivos || p.Activo)
                .OrderBy(p => p.Nombre, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
    }

    public IReadOnlyList<Producto> PorCategoria(int categoriaId)
    {
        lock (_candado)
        {
            return _d.Productos
                .Where(p => p.CategoriaId == categoriaId)
                .OrderBy(p => p.Nombre, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
    }

    public IReadOnlyList<Producto> ConStockBajo()
    {
        lock (_candado)
        {
            // Los agotados primero: son los más urgentes.
            return _d.Productos
                .Where(p => p.Activo && p.NecesitaReposicion)
                .OrderBy(p => p.Stock)
                .ThenBy(p => p.Nombre, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
    }

    public Producto Guardar(Producto producto)
    {
        lock (_candado)
        {
            if (producto.Id == 0)
            {
                producto.AsignarId(_d.SiguienteProducto++);
                _d.Productos.Add(producto);
            }
            else if (!_d.Productos.Any(p => ReferenceEquals(p, producto)))
            {
                // Puede venir una instancia distinta con el mismo id (por
                // ejemplo reconstruida por un formulario): se reemplaza.
                var indice = _d.Productos.FindIndex(p => p.Id == producto.Id);
                if (indice >= 0)
                {
                    _d.Productos[indice] = producto;
                }
                else
                {
                    _d.Productos.Add(producto);
                }
            }

            return producto;
        }
    }

    public bool Eliminar(int id)
    {
        lock (_candado)
        {
            return _d.Productos.RemoveAll(p => p.Id == id) > 0;
        }
    }

    public int Contar()
    {
        lock (_candado)
        {
            return _d.Productos.Count;
        }
    }
}

internal sealed class CategoriaRepositorioEnMemoria : ICategoriaRepositorio
{
    private readonly DatosEnMemoria _d;
    private readonly object _candado;

    public CategoriaRepositorioEnMemoria(DatosEnMemoria datos, object candado)
    {
        _d = datos;
        _candado = candado;
    }

    public Categoria? PorId(int id)
    {
        lock (_candado)
        {
            return _d.Categorias.FirstOrDefault(c => c.Id == id);
        }
    }

    public Categoria? PorSlug(string slug)
    {
        var buscado = Categoria.GenerarSlug(slug ?? "");
        lock (_candado)
        {
            return _d.Categorias.FirstOrDefault(c => c.Slug == buscado);
        }
    }

    public Categoria? PorNombre(string nombre)
    {
        var buscado = (nombre ?? "").Trim();
        lock (_candado)
        {
            return _d.Categorias.FirstOrDefault(c =>
                string.Equals(c.Nombre, buscado, StringComparison.CurrentCultureIgnoreCase));
        }
    }

    public IReadOnlyList<Categoria> Todas()
    {
        lock (_candado)
        {
            return _d.Categorias
                .OrderBy(c => c.Nombre, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
    }

    public Categoria Guardar(Categoria categoria)
    {
        lock (_candado)
        {
            if (categoria.Id == 0)
            {
                categoria.AsignarId(_d.SiguienteCategoria++);
                _d.Categorias.Add(categoria);
            }
            else if (!_d.Categorias.Any(c => ReferenceEquals(c, categoria)))
            {
                var indice = _d.Categorias.FindIndex(c => c.Id == categoria.Id);
                if (indice >= 0)
                {
                    _d.Categorias[indice] = categoria;
                }
                else
                {
                    _d.Categorias.Add(categoria);
                }
            }

            return categoria;
        }
    }

    public bool Eliminar(int id)
    {
        lock (_candado)
        {
            return _d.Categorias.RemoveAll(c => c.Id == id) > 0;
        }
    }
}

internal sealed class UsuarioRepositorioEnMemoria : IUsuarioRepositorio
{
    private readonly DatosEnMemoria _d;
    private readonly object _candado;

    public UsuarioRepositorioEnMemoria(DatosEnMemoria datos, object candado)
    {
        _d = datos;
        _candado = candado;
    }

    public Usuario? PorId(int id)
    {
        lock (_candado)
        {
            return _d.Usuarios.FirstOrDefault(u => u.Id == id);
        }
    }

    public Usuario? PorEmail(string email)
    {
        var buscado = (email ?? "").Trim().ToLowerInvariant();
        lock (_candado)
        {
            return _d.Usuarios.FirstOrDefault(u => u.Email == buscado);
        }
    }

    public IReadOnlyList<Usuario> Todos()
    {
        lock (_candado)
        {
            return _d.Usuarios
                .OrderBy(u => u.Nombre, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
    }

    public Usuario Guardar(Usuario usuario)
    {
        lock (_candado)
        {
            if (usuario.Id == 0)
            {
                usuario.AsignarId(_d.SiguienteUsuario++);
                _d.Usuarios.Add(usuario);
            }
            else if (!_d.Usuarios.Any(u => ReferenceEquals(u, usuario)))
            {
                var indice = _d.Usuarios.FindIndex(u => u.Id == usuario.Id);
                if (indice >= 0)
                {
                    _d.Usuarios[indice] = usuario;
                }
                else
                {
                    _d.Usuarios.Add(usuario);
                }
            }

            return usuario;
        }
    }

    public bool Eliminar(int id)
    {
        lock (_candado)
        {
            return _d.Usuarios.RemoveAll(u => u.Id == id) > 0;
        }
    }

    public int Contar()
    {
        lock (_candado)
        {
            return _d.Usuarios.Count;
        }
    }
}

internal sealed class PedidoRepositorioEnMemoria : IPedidoRepositorio
{
    private readonly DatosEnMemoria _d;
    private readonly object _candado;

    public PedidoRepositorioEnMemoria(DatosEnMemoria datos, object candado)
    {
        _d = datos;
        _candado = candado;
    }

    public Pedido? PorId(int id)
    {
        lock (_candado)
        {
            return _d.Pedidos.FirstOrDefault(p => p.Id == id);
        }
    }

    public Pedido? PorCodigo(string codigo)
    {
        var buscado = (codigo ?? "").Trim();
        lock (_candado)
        {
            return _d.Pedidos.FirstOrDefault(p =>
                string.Equals(p.Codigo, buscado, StringComparison.OrdinalIgnoreCase));
        }
    }

    public IReadOnlyList<Pedido> Todos()
    {
        lock (_candado)
        {
            return _d.Pedidos.OrderByDescending(p => p.Id).ToList();
        }
    }

    public IReadOnlyList<Pedido> PorUsuario(int usuarioId)
    {
        lock (_candado)
        {
            return _d.Pedidos
                .Where(p => p.UsuarioId == usuarioId)
                .OrderByDescending(p => p.Id)
                .ToList();
        }
    }

    public IReadOnlyList<Pedido> ConProducto(int productoId)
    {
        lock (_candado)
        {
            return _d.Pedidos
                .Where(p => p.Lineas.Any(l => l.ProductoId == productoId))
                .OrderByDescending(p => p.Id)
                .ToList();
        }
    }

    public Pedido Guardar(Pedido pedido)
    {
        lock (_candado)
        {
            if (pedido.Id == 0)
            {
                pedido.AsignarId(_d.SiguientePedido++);

                foreach (var linea in pedido.Lineas)
                {
                    linea.AsignarId(_d.SiguienteLinea++);
                    linea.AsignarPedido(pedido.Id);
                }

                _d.Pedidos.Add(pedido);
            }
            else if (!_d.Pedidos.Any(p => ReferenceEquals(p, pedido)))
            {
                var indice = _d.Pedidos.FindIndex(p => p.Id == pedido.Id);
                if (indice >= 0)
                {
                    _d.Pedidos[indice] = pedido;
                }
                else
                {
                    _d.Pedidos.Add(pedido);
                }
            }

            return pedido;
        }
    }

    public int Contar()
    {
        lock (_candado)
        {
            return _d.Pedidos.Count;
        }
    }

    public string SiguienteCodigo()
    {
        lock (_candado)
        {
            return $"HS-{DateTime.Now:yyyyMMdd}-{_d.SiguientePedido:0000}";
        }
    }
}

internal sealed class PromocionRepositorioEnMemoria : IPromocionRepositorio
{
    private readonly DatosEnMemoria _d;
    private readonly object _candado;

    public PromocionRepositorioEnMemoria(DatosEnMemoria datos, object candado)
    {
        _d = datos;
        _candado = candado;
    }

    public Promocion? PorId(int id)
    {
        lock (_candado)
        {
            return _d.Promociones.FirstOrDefault(p => p.Id == id);
        }
    }

    public IReadOnlyList<Promocion> Todas()
    {
        lock (_candado)
        {
            return _d.Promociones.OrderByDescending(p => p.Id).ToList();
        }
    }

    public IReadOnlyList<Promocion> Vigentes(DateTime? cuando = null)
    {
        lock (_candado)
        {
            return _d.Promociones.Where(p => p.EstaVigente(cuando)).ToList();
        }
    }

    public IReadOnlyList<Promocion> ConObjetivo(AlcancePromocion alcance, int objetivoId)
    {
        lock (_candado)
        {
            return _d.Promociones
                .Where(p => p.Alcance == alcance && p.ObjetivoId == objetivoId)
                .ToList();
        }
    }

    public Promocion Guardar(Promocion promocion)
    {
        lock (_candado)
        {
            if (promocion.Id == 0)
            {
                promocion.AsignarId(_d.SiguientePromocion++);
                _d.Promociones.Add(promocion);
            }
            else if (!_d.Promociones.Any(p => ReferenceEquals(p, promocion)))
            {
                var indice = _d.Promociones.FindIndex(p => p.Id == promocion.Id);
                if (indice >= 0)
                {
                    _d.Promociones[indice] = promocion;
                }
                else
                {
                    _d.Promociones.Add(promocion);
                }
            }

            return promocion;
        }
    }

    public bool Eliminar(int id)
    {
        lock (_candado)
        {
            return _d.Promociones.RemoveAll(p => p.Id == id) > 0;
        }
    }
}

internal sealed class CuponRepositorioEnMemoria : ICuponRepositorio
{
    private readonly DatosEnMemoria _d;
    private readonly object _candado;

    public CuponRepositorioEnMemoria(DatosEnMemoria datos, object candado)
    {
        _d = datos;
        _candado = candado;
    }

    public Cupon? PorId(int id)
    {
        lock (_candado)
        {
            return _d.Cupones.FirstOrDefault(c => c.Id == id);
        }
    }

    public Cupon? PorCodigo(string codigo)
    {
        var buscado = (codigo ?? "").Trim().ToUpperInvariant();
        lock (_candado)
        {
            return _d.Cupones.FirstOrDefault(c => c.Codigo == buscado);
        }
    }

    public IReadOnlyList<Cupon> Todos()
    {
        lock (_candado)
        {
            return _d.Cupones.OrderBy(c => c.Codigo).ToList();
        }
    }

    public Cupon Guardar(Cupon cupon)
    {
        lock (_candado)
        {
            if (cupon.Id == 0)
            {
                cupon.AsignarId(_d.SiguienteCupon++);
                _d.Cupones.Add(cupon);
            }
            else if (!_d.Cupones.Any(c => ReferenceEquals(c, cupon)))
            {
                var indice = _d.Cupones.FindIndex(c => c.Id == cupon.Id);
                if (indice >= 0)
                {
                    _d.Cupones[indice] = cupon;
                }
                else
                {
                    _d.Cupones.Add(cupon);
                }
            }

            return cupon;
        }
    }

    public bool Eliminar(int id)
    {
        lock (_candado)
        {
            return _d.Cupones.RemoveAll(c => c.Id == id) > 0;
        }
    }
}
