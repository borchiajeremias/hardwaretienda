using HardStore.Web.Dominio;
using HardStore.Web.Servicios;

namespace HardStore.Web.Datos;

/// <summary>
/// CAPA: Datos · Carga el catálogo de arranque la primera vez que corre la app.
///
/// Siembra a través de las ENTIDADES y del servicio de usuarios, no escribiendo
/// el archivo a mano: así los datos de ejemplo pasan por las mismas
/// validaciones que si los hubieras cargado desde el panel. Si una regla de
/// negocio se rompe, el sembrado falla — y eso es información útil.
/// </summary>
public static class DatosIniciales
{
    public const string EmailAdmin = "admin@hardstore.test";
    public const string PasswordAdmin = "Admin1234";

    public static void Sembrar(IAlmacen almacen, UsuarioServicio usuarios, ILogger log)
    {
        if (almacen.Productos.Contar() > 0 || almacen.Usuarios.Contar() > 0)
        {
            return;   // ya hay datos: no se toca nada
        }

        log.LogInformation("Sembrando el catálogo inicial…");

        /* ---------------------------- Categorías --------------------------- */
        var categorias = new Dictionary<string, int>();

        foreach (var (nombre, descripcion) in new[]
        {
            ("Procesadores", "CPUs AMD e Intel para gaming y workstation"),
            ("Placas de Video", "GPUs dedicadas NVIDIA y AMD"),
            ("Memorias RAM", "Módulos DDR4 y DDR5"),
            ("Almacenamiento", "Discos SSD NVMe, SATA y HDD"),
            ("Periféricos", "Teclados, mouses, headsets y monitores"),
            ("Equipos Armados", "PCs completas listas para usar"),
        })
        {
            var categoria = almacen.Categorias.Guardar(new Categoria(nombre, descripcion: descripcion));
            categorias[nombre] = categoria.Id;
        }

        /* ----------------------------- Productos --------------------------- */
        // codigo, nombre, categoría, marca, precio, stock, mínimo, destacado, imagen, descripción
        var productos = new (string Codigo, string Nombre, string Categoria, string Marca,
            decimal Precio, int Stock, int Minimo, bool Destacado, string Imagen, string Descripcion)[]
        {
            ("CPU-7800X3D", "AMD Ryzen 7 7800X3D", "Procesadores", "AMD",
                489999.00m, 12, 3, true, "img/productos/ryzen-7800x3d.svg",
                "8 núcleos / 16 hilos, 4.2 GHz base y 5.0 GHz boost, 96 MB de caché 3D V-Cache."),

            ("CPU-14600KF", "Intel Core i5-14600KF", "Procesadores", "Intel",
                372000.00m, 2, 4, false, "img/productos/intel-i5-14600kf.svg",
                "14 núcleos (6P + 8E) / 20 hilos, hasta 5.3 GHz. Excelente relación precio-rendimiento."),

            ("GPU-4070S", "NVIDIA GeForce RTX 4070 Super 12GB", "Placas de Video", "ASUS",
                1149000.00m, 5, 2, true, "img/productos/rtx-4070-super.svg",
                "12 GB GDDR6X, 7168 CUDA cores, DLSS 3 y Ray Tracing de 3ra generación. Ideal para 1440p."),

            ("GPU-7800XT", "AMD Radeon RX 7800 XT 16GB", "Placas de Video", "Sapphire",
                985000.00m, 6, 2, false, "img/productos/radeon-7800xt.svg",
                "16 GB GDDR6, excelente rendimiento en 1440p con FSR 3."),

            ("RAM-DDR5-32", "Corsair Vengeance DDR5 32GB (2x16) 6000MHz", "Memorias RAM", "Corsair",
                189500.50m, 24, 6, false, "img/productos/corsair-ddr5.svg",
                "Kit dual channel DDR5 6000 MHz CL30, perfil EXPO/XMP 3.0 y disipador de aluminio."),

            ("SSD-990P-2T", "Samsung 990 PRO 2TB NVMe Gen4", "Almacenamiento", "Samsung",
                265000.00m, 18, 5, true, "img/productos/samsung-990-pro.svg",
                "SSD M.2 2280 PCIe 4.0 x4 con hasta 7450 MB/s de lectura secuencial."),

            ("SSD-NV2-1T", "Kingston NV2 1TB NVMe", "Almacenamiento", "Kingston",
                98500.00m, 30, 8, false, "img/productos/kingston-nv2.svg",
                "SSD M.2 PCIe 4.0, la opción más conveniente para el disco del sistema."),

            ("TEC-GPROX", "Teclado Mecánico Logitech G Pro X TKL", "Periféricos", "Logitech",
                154900.00m, 15, 3, false, "img/productos/logitech-gprox.svg",
                "Teclado mecánico tenkeyless inalámbrico LIGHTSPEED, switches GX intercambiables."),

            ("MOU-GPROX2", "Mouse Logitech G Pro X Superlight 2", "Periféricos", "Logitech",
                189000.00m, 22, 5, false, "img/productos/logitech-mouse.svg",
                "60 gramos, sensor HERO 2 de 32.000 DPI y 95 horas de batería."),

            ("MON-LG27", "Monitor Gamer LG UltraGear 27\" 165Hz", "Periféricos", "LG",
                398000.00m, 8, 2, false, "img/productos/lg-ultragear.svg",
                "Panel IPS QHD 2560x1440, 165 Hz, 1 ms GtG, HDR10 y compatibilidad con G-SYNC."),

            ("PC-R5-4060", "PC Armada Gamer Ryzen 5 + RTX 4060", "Equipos Armados", "HardStore",
                1899000.00m, 3, 1, true, "img/productos/pc-gamer.svg",
                "Ryzen 5 7600, 16 GB DDR5, SSD 1 TB NVMe, RTX 4060 8 GB, fuente 650W 80+ Bronze."),

            ("PC-OFICINA", "PC Oficina Intel i3 + 16GB", "Equipos Armados", "HardStore",
                565000.00m, 7, 2, false, "img/productos/pc-oficina.svg",
                "Intel i3-13100, 16 GB DDR4, SSD 480 GB. Lista para trabajar."),
        };

        foreach (var p in productos)
        {
            var producto = new Producto(
                codigo: p.Codigo,
                nombre: p.Nombre,
                categoriaId: categorias[p.Categoria],
                precio: p.Precio,
                stock: p.Stock,
                stockMinimo: p.Minimo,
                descripcion: p.Descripcion,
                marca: p.Marca,
                destacado: p.Destacado);

            producto.CambiarImagen(p.Imagen);
            almacen.Productos.Guardar(producto);
        }

        /* ---------------------------- Promociones -------------------------- */
        var hoy = DateTime.Today;
        var finDeMes = hoy.AddMonths(2);

        var ssdNv2 = almacen.Productos.PorCodigo("SSD-NV2-1T")!;
        var ram = almacen.Productos.PorCodigo("RAM-DDR5-32")!;

        almacen.Promociones.Guardar(Promocion.LlevaNPaga(
            "3x2 en SSD Kingston NV2", AlcancePromocion.Producto, ssdNv2.Id,
            llevaN: 3, pagaM: 2, desde: hoy, hasta: finDeMes));

        almacen.Promociones.Guardar(Promocion.LlevaNPaga(
            "2x1 en memorias DDR5", AlcancePromocion.Producto, ram.Id,
            llevaN: 2, pagaM: 1, desde: hoy, hasta: finDeMes));

        almacen.Promociones.Guardar(Promocion.Porcentual(
            "15 % OFF en Periféricos", AlcancePromocion.Categoria, categorias["Periféricos"],
            porcentaje: 15m, desde: hoy, hasta: finDeMes));

        /* ------------------------------ Cupones ---------------------------- */
        almacen.Cupones.Guardar(new Cupon(
            codigo: "BIENVENIDO10",
            tipo: TipoCupon.Porcentaje,
            valor: 10m,
            desde: hoy,
            hasta: hoy.AddYears(1),
            minimoCompra: 0m,
            topeDescuento: 150000m,
            usosMaximos: 0,
            descripcion: "10 % para la primera compra, con tope de $150.000"));

        almacen.Cupones.Guardar(new Cupon(
            codigo: "ENVIOGRATIS",
            tipo: TipoCupon.MontoFijo,
            valor: 25000m,
            desde: hoy,
            hasta: hoy.AddMonths(3),
            minimoCompra: 300000m,
            topeDescuento: 0m,
            usosMaximos: 100,
            descripcion: "$25.000 de descuento en compras desde $300.000"));

        almacen.Cupones.Guardar(new Cupon(
            codigo: "GAMER25",
            tipo: TipoCupon.Porcentaje,
            valor: 25m,
            desde: hoy,
            hasta: hoy.AddDays(15),
            minimoCompra: 500000m,
            topeDescuento: 400000m,
            usosMaximos: 20,
            descripcion: "25 % en compras grandes, 20 usos"));

        almacen.Confirmar();

        /* ------------------------------ Usuarios --------------------------- */
        // El administrador se crea por el servicio, así la contraseña queda
        // hasheada con PBKDF2 igual que la de cualquier usuario registrado.
        usuarios.Registrar(
            nombre: "Administrador",
            email: EmailAdmin,
            password: PasswordAdmin,
            telefono: "0800-333-HARD",
            direccion: "Av. Siempreviva 742, CABA",
            rol: RolUsuario.Admin);

        usuarios.Registrar(
            nombre: "Laura Giménez",
            email: "laura@cliente.test",
            password: "Cliente123",
            telefono: "11 4444-5555",
            direccion: "Calle Falsa 123, CABA");

        log.LogInformation(
            "Catálogo sembrado: {Productos} productos, 3 promociones, 3 cupones, 2 usuarios.",
            productos.Length);
    }
}
