#if CON_EF_CORE
using Microsoft.EntityFrameworkCore;

namespace HardStore.Web.Datos.EfCore;

/// <summary>
/// CAPA: Datos · Contexto de EF Core sobre SQLite.
///
/// El esquema se declara acá con Fluent API en lugar de atributos, para que
/// las clases de fila sigan siendo POCO sin dependencias de EF.
///
/// Nota sobre SQLite y decimal: SQLite no tiene tipo decimal, así que EF Core
/// avisa (o falla al ordenar/comparar) si no se le dice qué hacer. Acá los
/// importes se guardan como TEXT con conversión explícita, que preserva los
/// centavos exactos — que es lo que importa cuando se trata de plata. Guardar
/// dinero en REAL (punto flotante) es cómo aparecen las diferencias de un
/// centavo en los totales.
/// </summary>
public sealed class HardStoreContext : DbContext
{
    public HardStoreContext(DbContextOptions<HardStoreContext> opciones) : base(opciones) { }

    internal DbSet<ProductoFila> Productos => Set<ProductoFila>();
    internal DbSet<CategoriaFila> Categorias => Set<CategoriaFila>();
    internal DbSet<UsuarioFila> Usuarios => Set<UsuarioFila>();
    internal DbSet<PedidoFila> Pedidos => Set<PedidoFila>();
    internal DbSet<LineaPedidoFila> LineasPedido => Set<LineaPedidoFila>();
    internal DbSet<PromocionFila> Promociones => Set<PromocionFila>();
    internal DbSet<CuponFila> Cupones => Set<CuponFila>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        /* ---------------------------- categorias ------------------------- */
        b.Entity<CategoriaFila>(e =>
        {
            e.ToTable("categorias");
            e.HasKey(x => x.Id);

            // Los ids los asigna el almacén (los contadores en memoria), no la
            // base: así el objeto de dominio ya tiene su id antes del INSERT.
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Nombre).HasMaxLength(80).IsRequired();
            e.Property(x => x.Slug).HasMaxLength(80).IsRequired();
            e.Property(x => x.Descripcion).HasMaxLength(300);
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasIndex(x => x.Nombre).IsUnique();
        });

        /* ----------------------------- productos ------------------------- */
        b.Entity<ProductoFila>(e =>
        {
            e.ToTable("productos");
            e.HasKey(x => x.Id);

            // Los ids los asigna el almacén (los contadores en memoria), no la
            // base: así el objeto de dominio ya tiene su id antes del INSERT.
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Codigo).HasMaxLength(20).IsRequired();
            e.Property(x => x.Nombre).HasMaxLength(150).IsRequired();
            e.Property(x => x.Descripcion).HasMaxLength(1000);
            e.Property(x => x.Marca).HasMaxLength(60);
            e.Property(x => x.Imagen).HasMaxLength(255);
            e.Property(x => x.Precio).HasConversion<string>();

            e.HasIndex(x => x.Codigo).IsUnique();
            e.HasIndex(x => x.CategoriaId);

            // RESTRICT, igual que el schema.sql de la versión PHP: una
            // categoría con productos no se borra de un descuido.
            e.HasOne<CategoriaFila>()
                .WithMany()
                .HasForeignKey(x => x.CategoriaId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        /* ------------------------------ usuarios ------------------------- */
        b.Entity<UsuarioFila>(e =>
        {
            e.ToTable("usuarios");
            e.HasKey(x => x.Id);

            // Los ids los asigna el almacén (los contadores en memoria), no la
            // base: así el objeto de dominio ya tiene su id antes del INSERT.
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Nombre).HasMaxLength(120).IsRequired();
            e.Property(x => x.Email).HasMaxLength(180).IsRequired();
            e.Property(x => x.PasswordHash).HasMaxLength(200).IsRequired();
            e.Property(x => x.PasswordSalt).HasMaxLength(100).IsRequired();
            e.Property(x => x.Telefono).HasMaxLength(40);
            e.Property(x => x.Direccion).HasMaxLength(255);
            e.Property(x => x.Rol).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => x.Email).IsUnique();
        });

        /* ------------------------------- pedidos ------------------------- */
        b.Entity<PedidoFila>(e =>
        {
            e.ToTable("pedidos");
            e.HasKey(x => x.Id);

            // Los ids los asigna el almacén (los contadores en memoria), no la
            // base: así el objeto de dominio ya tiene su id antes del INSERT.
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Codigo).HasMaxLength(30).IsRequired();
            e.Property(x => x.ClienteNombre).HasMaxLength(120).IsRequired();
            e.Property(x => x.ClienteEmail).HasMaxLength(180).IsRequired();
            e.Property(x => x.ClienteTelefono).HasMaxLength(40);
            e.Property(x => x.DireccionEnvio).HasMaxLength(255);
            e.Property(x => x.Observaciones).HasMaxLength(500);
            e.Property(x => x.MarcaTarjeta).HasMaxLength(30);
            e.Property(x => x.Medio).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Estado).HasConversion<string>().HasMaxLength(20);

            foreach (var importe in new[]
                     {
                         nameof(PedidoFila.Bruto), nameof(PedidoFila.DescuentoPromociones),
                         nameof(PedidoFila.DescuentoCupon), nameof(PedidoFila.CoeficientePago),
                         nameof(PedidoFila.AjustePago), nameof(PedidoFila.Total),
                     })
            {
                e.Property<decimal>(importe).HasConversion<string>();
            }

            e.HasIndex(x => x.Codigo).IsUnique();
            e.HasIndex(x => x.UsuarioId);
            e.HasIndex(x => x.CreadoEn);

            // SET NULL: si se borra el usuario, el pedido se conserva como
            // pedido de invitado. La contabilidad no se borra con la cuenta.
            e.HasOne<UsuarioFila>()
                .WithMany()
                .HasForeignKey(x => x.UsuarioId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasMany(x => x.Lineas)
                .WithOne()
                .HasForeignKey(l => l.PedidoId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        /* -------------------------- detalle_pedidos ---------------------- */
        b.Entity<LineaPedidoFila>(e =>
        {
            e.ToTable("detalle_pedidos");
            e.HasKey(x => x.Id);

            // Los ids los asigna el almacén (los contadores en memoria), no la
            // base: así el objeto de dominio ya tiene su id antes del INSERT.
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Codigo).HasMaxLength(20).IsRequired();
            e.Property(x => x.Descripcion).HasMaxLength(150).IsRequired();
            e.Property(x => x.PromocionAplicada).HasMaxLength(60);
            e.Property(x => x.PrecioUnitario).HasConversion<string>();
            e.Property(x => x.DescuentoPromocion).HasConversion<string>();

            e.HasIndex(x => x.PedidoId);
            e.HasIndex(x => x.ProductoId);

            // SET NULL a propósito: el producto puede desaparecer del catálogo,
            // pero la línea guarda código, nombre y precio congelados, así que
            // el comprobante sigue siendo legible años después.
            e.HasOne<ProductoFila>()
                .WithMany()
                .HasForeignKey(x => x.ProductoId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        /* ---------------------------- promociones ------------------------ */
        b.Entity<PromocionFila>(e =>
        {
            e.ToTable("promociones");
            e.HasKey(x => x.Id);

            // Los ids los asigna el almacén (los contadores en memoria), no la
            // base: así el objeto de dominio ya tiene su id antes del INSERT.
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Nombre).HasMaxLength(120).IsRequired();
            e.Property(x => x.Tipo).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Alcance).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Porcentaje).HasConversion<string>();
            e.HasIndex(x => new { x.Alcance, x.ObjetivoId });
        });

        /* ------------------------------- cupones ------------------------- */
        b.Entity<CuponFila>(e =>
        {
            e.ToTable("cupones");
            e.HasKey(x => x.Id);

            // Los ids los asigna el almacén (los contadores en memoria), no la
            // base: así el objeto de dominio ya tiene su id antes del INSERT.
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Codigo).HasMaxLength(30).IsRequired();
            e.Property(x => x.Descripcion).HasMaxLength(200);
            e.Property(x => x.Tipo).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Valor).HasConversion<string>();
            e.Property(x => x.MinimoCompra).HasConversion<string>();
            e.Property(x => x.TopeDescuento).HasConversion<string>();
            e.HasIndex(x => x.Codigo).IsUnique();
        });
    }
}
#endif
