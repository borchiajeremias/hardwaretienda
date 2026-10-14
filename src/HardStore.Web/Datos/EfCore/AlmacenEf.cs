#if CON_EF_CORE
using HardStore.Web.Datos.Memoria;
using HardStore.Web.Dominio;
using Microsoft.EntityFrameworkCore;

namespace HardStore.Web.Datos.EfCore;

/// <summary>
/// CAPA: Datos · Almacén sobre EF Core + SQLite.
///
/// Implementa exactamente el mismo contrato <see cref="IAlmacen"/> que
/// <see cref="Json.AlmacenJson"/>, así que se puede cambiar uno por otro en
/// Program.cs sin tocar una línea de los servicios ni de los controladores.
/// Esa es toda la razón de que los repositorios sean interfaces.
///
/// Cómo funciona
/// -------------
/// Al iniciar se carga la base entera en memoria y los seis repositorios
/// trabajan sobre esas listas — son los MISMOS repositorios que usa el
/// almacén JSON (ver Datos/Memoria/). Cuando un servicio llama a Confirmar(),
/// se compara el estado en memoria con las filas de la base y se escribe sólo
/// la diferencia, dentro de una transacción.
///
/// ¿Por qué no consultar la base en cada operación, como es habitual con EF?
/// Porque el resto del sistema (el candado del checkout en PedidoServicio, por
/// ejemplo) está escrito contra un almacén en memoria y así las dos
/// implementaciones se comportan igual, con las mismas garantías y los mismos
/// tiempos. Para un catálogo de una tienda chica el costo es despreciable: son
/// unos cientos de filas. Con decenas de miles de productos habría que cambiar
/// a consultas por operación, y entonces sólo cambia este archivo.
/// </summary>
public sealed class AlmacenEf : IAlmacen
{
    private readonly IDbContextFactory<HardStoreContext> _fabrica;
    private readonly object _candado = new();
    private readonly DatosEnMemoria _datos;

    public AlmacenEf(IDbContextFactory<HardStoreContext> fabrica)
    {
        _fabrica = fabrica;
        _datos = Cargar();

        Productos = new ProductoRepositorioEnMemoria(_datos, _candado);
        Categorias = new CategoriaRepositorioEnMemoria(_datos, _candado);
        Usuarios = new UsuarioRepositorioEnMemoria(_datos, _candado);
        Pedidos = new PedidoRepositorioEnMemoria(_datos, _candado);
        Promociones = new PromocionRepositorioEnMemoria(_datos, _candado);
        Cupones = new CuponRepositorioEnMemoria(_datos, _candado);
    }

    public IProductoRepositorio Productos { get; }
    public ICategoriaRepositorio Categorias { get; }
    public IUsuarioRepositorio Usuarios { get; }
    public IPedidoRepositorio Pedidos { get; }
    public IPromocionRepositorio Promociones { get; }
    public ICuponRepositorio Cupones { get; }

    public bool EstaVacio
    {
        get
        {
            lock (_candado)
            {
                return _datos.Productos.Count == 0 && _datos.Usuarios.Count == 0;
            }
        }
    }

    /* =====================================================================
       CARGA
       ===================================================================== */

    private DatosEnMemoria Cargar()
    {
        using var ctx = _fabrica.CreateDbContext();
        ctx.Database.EnsureCreated();

        var d = new DatosEnMemoria();

        d.Categorias.AddRange(ctx.Categorias.AsNoTracking()
            .OrderBy(c => c.Id).ToList().Select(c => c.ADominio()));

        d.Productos.AddRange(ctx.Productos.AsNoTracking()
            .OrderBy(p => p.Id).ToList().Select(p => p.ADominio()));

        d.Usuarios.AddRange(ctx.Usuarios.AsNoTracking()
            .OrderBy(u => u.Id).ToList().Select(u => u.ADominio()));

        d.Promociones.AddRange(ctx.Promociones.AsNoTracking()
            .OrderBy(p => p.Id).ToList().Select(p => p.ADominio()));

        d.Cupones.AddRange(ctx.Cupones.AsNoTracking()
            .OrderBy(c => c.Id).ToList().Select(c => c.ADominio()));

        d.Pedidos.AddRange(ctx.Pedidos.AsNoTracking()
            .Include(p => p.Lineas)
            .OrderBy(p => p.Id).ToList().Select(p => p.ADominio()));

        // Los contadores arrancan en «el mayor id + 1». Sin esto, al reiniciar
        // el servidor se generarían ids ya usados.
        d.SiguienteCategoria = Siguiente(d.Categorias.Select(c => c.Id));
        d.SiguienteProducto = Siguiente(d.Productos.Select(p => p.Id));
        d.SiguienteUsuario = Siguiente(d.Usuarios.Select(u => u.Id));
        d.SiguientePromocion = Siguiente(d.Promociones.Select(p => p.Id));
        d.SiguienteCupon = Siguiente(d.Cupones.Select(c => c.Id));
        d.SiguientePedido = Siguiente(d.Pedidos.Select(p => p.Id));
        d.SiguienteLinea = Siguiente(d.Pedidos.SelectMany(p => p.Lineas).Select(l => l.Id));

        return d;
    }

    private static int Siguiente(IEnumerable<int> ids)
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

    /* =====================================================================
       VOLCADO
       ===================================================================== */

    /// <summary>
    /// Escribe en la base la diferencia entre el estado en memoria y lo que
    /// hay guardado, todo dentro de una transacción: o entra el pedido
    /// completo con su descuento de stock, o no entra nada.
    /// </summary>
    public void Confirmar()
    {
        lock (_candado)
        {
            using var ctx = _fabrica.CreateDbContext();
            using var tx = ctx.Database.BeginTransaction();

            // Orden de escritura: primero las tablas de las que otras dependen.
            Sincronizar(
                ctx.Categorias, _datos.Categorias,
                c => c.Id, CategoriaFila.De, (fila, c) => fila.Copiar(c));

            Sincronizar(
                ctx.Productos, _datos.Productos,
                p => p.Id, ProductoFila.De, (fila, p) => fila.Copiar(p));

            Sincronizar(
                ctx.Usuarios, _datos.Usuarios,
                u => u.Id, UsuarioFila.De, (fila, u) => fila.Copiar(u));

            Sincronizar(
                ctx.Promociones, _datos.Promociones,
                p => p.Id, PromocionFila.De, (fila, p) => fila.Copiar(p));

            Sincronizar(
                ctx.Cupones, _datos.Cupones,
                c => c.Id, CuponFila.De, (fila, c) => fila.Copiar(c));

            // Los pedidos llevan sus líneas: se insertan con el grafo completo
            // y después no se vuelven a tocar más que el estado.
            SincronizarPedidos(ctx);

            ctx.SaveChanges();
            tx.Commit();
        }
    }

    /// <summary>
    /// Alta, modificación y baja de una tabla comparando por id. Genérico para
    /// no repetir cinco veces el mismo bucle.
    /// </summary>
    private static void Sincronizar<TDominio, TFila>(
        DbSet<TFila> tabla,
        IEnumerable<TDominio> enMemoria,
        Func<TDominio, int> idDe,
        Func<TDominio, TFila> aFila,
        Action<TFila, TDominio> copiar)
        where TFila : class, IFilaConId
    {
        var guardadas = tabla.ToList().ToDictionary(f => f.Id);
        var vistos = new HashSet<int>();

        foreach (var entidad in enMemoria)
        {
            var id = idDe(entidad);
            vistos.Add(id);

            if (guardadas.TryGetValue(id, out var fila))
            {
                copiar(fila, entidad);   // EF detecta qué columnas cambiaron
            }
            else
            {
                tabla.Add(aFila(entidad));
            }
        }

        // Lo que ya no está en memoria se borró desde el panel.
        foreach (var (id, fila) in guardadas)
        {
            if (!vistos.Contains(id))
            {
                tabla.Remove(fila);
            }
        }
    }

    /// <summary>
    /// Los pedidos son un caso aparte: nunca se borran (son el registro
    /// contable) y de los existentes sólo cambian el estado y las
    /// observaciones. Los importes y las líneas están congelados.
    /// </summary>
    private void SincronizarPedidos(HardStoreContext ctx)
    {
        var guardados = ctx.Pedidos.Include(p => p.Lineas).ToDictionary(p => p.Id);

        foreach (var pedido in _datos.Pedidos)
        {
            if (guardados.TryGetValue(pedido.Id, out var fila))
            {
                fila.Copiar(pedido);
            }
            else
            {
                ctx.Pedidos.Add(PedidoFila.De(pedido));
            }
        }
    }
}
#endif
