using System.Globalization;
using HardStore.Web.Datos;
using HardStore.Web.Datos.Json;
using HardStore.Web.Dominio;
using HardStore.Web.Servicios;

namespace HardStore.Pruebas;

/// <summary>
/// Pruebas del núcleo: el motor de precios (promociones, cupones, medios de
/// pago) y las reglas de stock al confirmar un pedido.
///
/// Se prueba el dominio y los servicios, no las pantallas: es donde está el
/// dinero. Cada prueba arma su propio almacén en un archivo temporal, así
/// ninguna depende del orden ni de lo que dejó la anterior.
/// </summary>
public static class Programa
{
    public static int Main()
    {
        CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("es-AR");

        var c = new Corredor();

        /* ================================================================
           PROMOCIONES NxM
           ================================================================ */
        c.Grupo("Promociones NxM");

        c.Prueba("3x2 con 3 unidades bonifica 1", () =>
        {
            var p = Promo3x2();
            Afirmar.Igual(1, p.UnidadesBonificadas(3), "unidades bonificadas");
        });

        c.Prueba("3x2 con 2 unidades no bonifica nada", () =>
            Afirmar.Igual(0, Promo3x2().UnidadesBonificadas(2), "no se completa el grupo"));

        c.Prueba("3x2 con 7 unidades bonifica 2 (dos grupos completos)", () =>
            Afirmar.Igual(2, Promo3x2().UnidadesBonificadas(7), "2 grupos de 3 + 1 suelta"));

        c.Prueba("2x1 con 4 unidades bonifica 2", () =>
            Afirmar.Igual(2, Promo2x1().UnidadesBonificadas(4), "dos pares"));

        c.Prueba("el descuento NxM es el precio de las unidades bonificadas", () =>
            Afirmar.Igual(1000m, Promo3x2().DescuentoSobre(3, 1000m), "una unidad gratis de $1000"));

        c.Prueba("una NxM no puede pagar más de lo que lleva", () =>
            Afirmar.Lanza<ArgumentException>(
                () => Promocion.LlevaNPaga("mala", AlcancePromocion.Producto, 1, 2, 2, Hoy, Manana),
                "2x2 no es una promoción"));

        c.Prueba("una NxM necesita al menos 2 unidades", () =>
            Afirmar.Lanza<ArgumentException>(
                () => Promocion.LlevaNPaga("mala", AlcancePromocion.Producto, 1, 1, 1, Hoy, Manana),
                "1x1 no es una promoción"));

        c.Prueba("una promoción vencida no está vigente", () =>
        {
            var p = Promocion.LlevaNPaga("vieja", AlcancePromocion.Producto, 1, 2, 1,
                Hoy.AddDays(-30), Hoy.AddDays(-1));
            Afirmar.Falso(p.EstaVigente(), "ya terminó");
        });

        c.Prueba("una promoción desactivada no está vigente aunque la fecha sirva", () =>
        {
            var p = Promo3x2();
            p.Desactivar();
            Afirmar.Falso(p.EstaVigente(), "está apagada");
        });

        /* ================================================================
           PROMOCIONES PORCENTUALES
           ================================================================ */
        c.Grupo("Promociones porcentuales");

        c.Prueba("15 % sobre 2 unidades de $10.000 descuenta $3.000", () =>
        {
            var p = Promocion.Porcentual("15 off", AlcancePromocion.Categoria, 1, 15m, Hoy, Manana);
            Afirmar.Igual(3000m, p.DescuentoSobre(2, 10000m), "15 % de $20.000");
        });

        c.Prueba("un porcentaje mayor a 100 se rechaza", () =>
            Afirmar.Lanza<ArgumentException>(
                () => Promocion.Porcentual("absurda", AlcancePromocion.Producto, 1, 120m, Hoy, Manana),
                "no se puede descontar más que el total"));

        /* ================================================================
           EL COTIZADOR ELIGE LA MEJOR PROMOCIÓN, NO LAS SUMA
           ================================================================ */
        c.Grupo("El cotizador elige la mejor promoción");

        c.Prueba("con dos promociones aplicables se toma la más conveniente y nunca las dos", () =>
        {
            using var esc = new Escenario();
            var prod = esc.AltaProducto("TEST-1", 10_000m, stock: 20, categoriaId: esc.CategoriaId);

            // 3x2 sobre el producto: con 3 unidades, ahorra $10.000.
            esc.Almacen.Promociones.Guardar(Promocion.LlevaNPaga(
                "3x2", AlcancePromocion.Producto, prod.Id, 3, 2, Hoy, Manana));

            // 20 % sobre la categoría: con 3 unidades, ahorra $6.000.
            esc.Almacen.Promociones.Guardar(Promocion.Porcentual(
                "20 off", AlcancePromocion.Categoria, esc.CategoriaId, 20m, Hoy, Manana));

            var cot = esc.Cotizador.Cotizar(new[] { new ItemPedido(prod.Id, 3) });

            Afirmar.Igual(30_000m, cot.Bruto, "bruto");
            Afirmar.Igual(10_000m, cot.DescuentoPromociones,
                "gana el 3x2 ($10.000), no se suma con el 20 % ($6.000)");
        });

        c.Prueba("si la porcentual conviene más, gana la porcentual", () =>
        {
            using var esc = new Escenario();
            var prod = esc.AltaProducto("TEST-2", 10_000m, stock: 20, categoriaId: esc.CategoriaId);

            esc.Almacen.Promociones.Guardar(Promocion.LlevaNPaga(
                "3x2", AlcancePromocion.Producto, prod.Id, 3, 2, Hoy, Manana));
            esc.Almacen.Promociones.Guardar(Promocion.Porcentual(
                "50 off", AlcancePromocion.Categoria, esc.CategoriaId, 50m, Hoy, Manana));

            var cot = esc.Cotizador.Cotizar(new[] { new ItemPedido(prod.Id, 3) });
            Afirmar.Igual(15_000m, cot.DescuentoPromociones, "50 % de $30.000 supera al 3x2");
        });

        /* ================================================================
           CUPONES
           ================================================================ */
        c.Grupo("Cupones");

        c.Prueba("un cupón porcentual descuenta el porcentaje del subtotal", () =>
        {
            var cup = new Cupon("DIEZ", TipoCupon.Porcentaje, 10m, Hoy, Manana);
            Afirmar.Igual(10_000m, cup.DescuentoSobre(100_000m), "10 % de $100.000");
        });

        c.Prueba("el tope recorta el descuento porcentual", () =>
        {
            var cup = new Cupon("DIEZ", TipoCupon.Porcentaje, 10m, Hoy, Manana, topeDescuento: 5_000m);
            Afirmar.Igual(5_000m, cup.DescuentoSobre(100_000m), "el tope manda");
        });

        c.Prueba("un cupón de monto fijo nunca descuenta más que el subtotal", () =>
        {
            var cup = new Cupon("MIL", TipoCupon.MontoFijo, 50_000m, Hoy, Manana);
            Afirmar.Igual(30_000m, cup.DescuentoSobre(30_000m), "no puede dejar el total en negativo");
        });

        c.Prueba("no aplica si no se llega al mínimo de compra", () =>
        {
            var cup = new Cupon("MIN", TipoCupon.MontoFijo, 10_000m, Hoy, Manana, minimoCompra: 100_000m);
            Afirmar.Falso(cup.EsAplicable(99_999m), "falta un peso");
            Afirmar.Cierto(cup.EsAplicable(100_000m), "justo alcanza");
        });

        c.Prueba("el motivo del rechazo explica el mínimo", () =>
        {
            var cup = new Cupon("MIN", TipoCupon.MontoFijo, 10_000m, Hoy, Manana, minimoCompra: 100_000m);
            Afirmar.Contiene("compra mínima", cup.MotivoDeRechazo(50_000m) ?? "", "hay un motivo legible");
        });

        c.Prueba("un cupón agotado deja de aplicar", () =>
        {
            var cup = new Cupon("UNO", TipoCupon.MontoFijo, 1_000m, Hoy, Manana, usosMaximos: 1);
            cup.RegistrarUso();
            Afirmar.Cierto(cup.AgotoUsos, "gastó su único uso");
            Afirmar.Falso(cup.EsAplicable(50_000m), "ya no aplica");
        });

        c.Prueba("un cupón vencido deja de aplicar", () =>
        {
            var cup = new Cupon("VIEJO", TipoCupon.MontoFijo, 1_000m, Hoy.AddDays(-10), Hoy.AddDays(-1));
            Afirmar.Cierto(cup.EstaVencido(), "venció ayer");
            Afirmar.Falso(cup.EsAplicable(50_000m), "ya no aplica");
        });

        c.Prueba("un cupón que no llega al mínimo se ignora con aviso, no rompe la cotización", () =>
        {
            using var esc = new Escenario();
            var prod = esc.AltaProducto("TEST-3", 10_000m, stock: 5, categoriaId: esc.CategoriaId);
            esc.Almacen.Cupones.Guardar(new Cupon(
                "CARO", TipoCupon.MontoFijo, 5_000m, Hoy, Manana, minimoCompra: 500_000m));

            var cot = esc.Cotizador.Cotizar(new[] { new ItemPedido(prod.Id, 1) }, "CARO");

            Afirmar.Igual(0m, cot.DescuentoCupon, "no se descontó nada");
            Afirmar.Cierto(cot.Avisos.Count > 0, "avisa por qué no se aplicó");
            Afirmar.Igual(10_000m, cot.SubtotalConPromociones, "el subtotal queda intacto");
        });

        /* ================================================================
           MEDIOS DE PAGO
           ================================================================ */
        c.Grupo("Medios de pago");

        c.Prueba("transferencia aplica 10 % de descuento", () =>
        {
            var plan = PlanesPago.PorClave("Transferencia:1")!;
            Afirmar.Igual(90_000m, plan.TotalSobre(100_000m), "coeficiente 0,90");
        });

        c.Prueba("débito cobra el precio de lista", () =>
            Afirmar.Igual(100_000m, PlanesPago.PorClave("TarjetaDebito:1")!.TotalSobre(100_000m),
                "coeficiente 1,00"));

        c.Prueba("3 cuotas es sin interés", () =>
            Afirmar.Igual(100_000m, PlanesPago.PorClave("TarjetaCredito:3")!.TotalSobre(100_000m),
                "coeficiente 1,00"));

        c.Prueba("6 cuotas recarga 15 %", () =>
            Afirmar.Igual(115_000m, PlanesPago.PorClave("TarjetaCredito:6")!.TotalSobre(100_000m),
                "coeficiente 1,15"));

        c.Prueba("12 cuotas recarga 35 %", () =>
            Afirmar.Igual(135_000m, PlanesPago.PorClave("TarjetaCredito:12")!.TotalSobre(100_000m),
                "coeficiente 1,35"));

        c.Prueba("las cuotas suman exactamente el total (el resto va en la última)", () =>
        {
            var plan = PlanesPago.PorClave("TarjetaCredito:3")!;
            var total = plan.TotalSobre(100_000m);
            var suma = plan.ValorCuota(100_000m) * 2 + plan.UltimaCuota(100_000m);
            Afirmar.Igual(total, suma, "no se pierde ni se inventa un centavo");
        });

        c.Prueba("una clave de plan inexistente devuelve null (y el cotizador usa el de por defecto)", () =>
            Afirmar.Cierto(PlanesPago.PorClave("Cripto:1") is null, "no existe ese plan"));

        /* ================================================================
           EL ORDEN DEL CÁLCULO
           ================================================================ */
        c.Grupo("Orden del cálculo: bruto → promos → cupón → medio de pago");

        c.Prueba("3 SSD con 3x2 + cupón 10 % + transferencia da el total esperado", () =>
        {
            using var esc = new Escenario();
            var prod = esc.AltaProducto("SSD", 98_500m, stock: 30, categoriaId: esc.CategoriaId);
            esc.Almacen.Promociones.Guardar(Promocion.LlevaNPaga(
                "3x2", AlcancePromocion.Producto, prod.Id, 3, 2, Hoy, Manana));
            esc.Almacen.Cupones.Guardar(new Cupon(
                "BIENVENIDO10", TipoCupon.Porcentaje, 10m, Hoy, Manana, topeDescuento: 150_000m));

            var cot = esc.Cotizador.Cotizar(
                new[] { new ItemPedido(prod.Id, 3) }, "BIENVENIDO10", "Transferencia:1");

            Afirmar.Igual(295_500m, cot.Bruto, "bruto = 3 × 98.500");
            Afirmar.Igual(98_500m, cot.DescuentoPromociones, "el 3x2 regala una unidad");
            Afirmar.Igual(197_000m, cot.SubtotalConPromociones, "subtotal con promo");
            Afirmar.Igual(19_700m, cot.DescuentoCupon, "el cupón es 10 % del subtotal CON promo");
            Afirmar.Igual(177_300m, cot.BaseFinanciacion, "base antes del medio de pago");
            Afirmar.Igual(159_570m, cot.Total, "y recién al final el 10 % de transferencia");
        });

        c.Prueba("el mismo carrito en 12 cuotas recarga sobre la base ya descontada", () =>
        {
            using var esc = new Escenario();
            var prod = esc.AltaProducto("SSD", 98_500m, stock: 30, categoriaId: esc.CategoriaId);
            esc.Almacen.Promociones.Guardar(Promocion.LlevaNPaga(
                "3x2", AlcancePromocion.Producto, prod.Id, 3, 2, Hoy, Manana));
            esc.Almacen.Cupones.Guardar(new Cupon(
                "BIENVENIDO10", TipoCupon.Porcentaje, 10m, Hoy, Manana, topeDescuento: 150_000m));

            var cot = esc.Cotizador.Cotizar(
                new[] { new ItemPedido(prod.Id, 3) }, "BIENVENIDO10", "TarjetaCredito:12");

            Afirmar.Igual(239_355m, cot.Total, "177.300 × 1,35");
            Afirmar.Igual(19_946.25m, cot.ValorCuota, "239.355 / 12");
        });

        c.Prueba("neto + IVA reconstruyen el total", () =>
        {
            using var esc = new Escenario();
            var prod = esc.AltaProducto("GEN-X", 121_000m, stock: 5, categoriaId: esc.CategoriaId);
            var cot = esc.Cotizador.Cotizar(new[] { new ItemPedido(prod.Id, 1) }, null, "TarjetaDebito:1");
            Afirmar.Igual(cot.Total, cot.Neto + cot.Iva, "el desglose de IVA cierra");
        });

        /* ================================================================
           STOCK Y CARRITO
           ================================================================ */
        c.Grupo("Stock en la cotización");

        c.Prueba("si se piden más unidades que el stock, se recorta y se avisa", () =>
        {
            using var esc = new Escenario();
            var prod = esc.AltaProducto("POCO", 1_000m, stock: 3, categoriaId: esc.CategoriaId);

            var cot = esc.Cotizador.Cotizar(new[] { new ItemPedido(prod.Id, 99) });

            Afirmar.Igual(3, cot.Lineas[0].Cantidad, "se cotizan sólo 3");
            Afirmar.Cierto(cot.Avisos.Count > 0, "el cliente se entera del recorte");
        });

        c.Prueba("un producto inactivo no se cotiza", () =>
        {
            using var esc = new Escenario();
            var prod = esc.AltaProducto("OFF", 1_000m, stock: 5, categoriaId: esc.CategoriaId);
            prod.Desactivar();
            esc.Almacen.Productos.Guardar(prod);

            var cot = esc.Cotizador.Cotizar(new[] { new ItemPedido(prod.Id, 1) });
            Afirmar.Cierto(cot.EstaVacia, "no entra al carrito");
        });

        c.Prueba("dos líneas del mismo producto se consolidan en una", () =>
        {
            using var esc = new Escenario();
            var prod = esc.AltaProducto("DUP", 1_000m, stock: 10, categoriaId: esc.CategoriaId);

            var cot = esc.Cotizador.Cotizar(new[]
            {
                new ItemPedido(prod.Id, 2),
                new ItemPedido(prod.Id, 3),
            });

            Afirmar.Igual(1, cot.Lineas.Count, "una sola línea");
            Afirmar.Igual(5, cot.Lineas[0].Cantidad, "las cantidades se suman");
        });

        /* ================================================================
           CONFIRMACIÓN DE PEDIDOS
           ================================================================ */
        c.Grupo("Confirmación de pedidos");

        c.Prueba("confirmar descuenta el stock exactamente una vez", () =>
        {
            using var esc = new Escenario();
            var prod = esc.AltaProducto("STK", 10_000m, stock: 10, categoriaId: esc.CategoriaId);

            esc.Pedidos.Confirmar(
                new[] { new ItemPedido(prod.Id, 3) }, Comprador, null, "Transferencia:1");

            Afirmar.Igual(7, esc.Almacen.Productos.PorId(prod.Id)!.Stock, "10 - 3");
        });

        c.Prueba("el pedido guarda el total que se le mostró al cliente", () =>
        {
            using var esc = new Escenario();
            var prod = esc.AltaProducto("SSD", 98_500m, stock: 30, categoriaId: esc.CategoriaId);
            esc.Almacen.Promociones.Guardar(Promocion.LlevaNPaga(
                "3x2", AlcancePromocion.Producto, prod.Id, 3, 2, Hoy, Manana));

            var pedido = esc.Pedidos.Confirmar(
                new[] { new ItemPedido(prod.Id, 3) }, Comprador, null, "Transferencia:1");

            Afirmar.Igual(177_300m, pedido.Total, "197.000 × 0,90");
            Afirmar.Igual(1, pedido.Lineas[0].UnidadesBonificadas, "queda registrada la unidad gratis");
        });

        c.Prueba("no se puede confirmar un carrito vacío", () =>
        {
            using var esc = new Escenario();
            Afirmar.Lanza<ReglaNegocioException>(
                () => esc.Pedidos.Confirmar(Array.Empty<ItemPedido>(), Comprador, null, null),
                "carrito vacío");
        });

        c.Prueba("no se puede confirmar más unidades que el stock disponible", () =>
        {
            using var esc = new Escenario();
            var prod = esc.AltaProducto("POCO", 1_000m, stock: 2, categoriaId: esc.CategoriaId);

            Afirmar.Lanza<ReglaNegocioException>(
                () => esc.Pedidos.Confirmar(
                    new[] { new ItemPedido(prod.Id, 5) }, Comprador, null, "Transferencia:1"),
                "al confirmar NO se recorta en silencio: se rechaza");

            Afirmar.Igual(2, esc.Almacen.Productos.PorId(prod.Id)!.Stock, "el stock no se tocó");
        });

        c.Prueba("confirmar registra el uso del cupón", () =>
        {
            using var esc = new Escenario();
            var prod = esc.AltaProducto("GEN-X", 100_000m, stock: 5, categoriaId: esc.CategoriaId);
            esc.Almacen.Cupones.Guardar(new Cupon(
                "USO1", TipoCupon.MontoFijo, 10_000m, Hoy, Manana, usosMaximos: 1));

            esc.Pedidos.Confirmar(
                new[] { new ItemPedido(prod.Id, 1) }, Comprador, "USO1", "TarjetaDebito:1", "Visa");

            var cup = esc.Almacen.Cupones.PorCodigo("USO1")!;
            Afirmar.Igual(1, cup.UsosRealizados, "el uso quedó anotado");
            Afirmar.Cierto(cup.AgotoUsos, "y el cupón se agotó");
        });

        c.Prueba("con el cupón agotado el segundo pedido se rechaza, no se cobra más caro en silencio", () =>
        {
            using var esc = new Escenario();
            var prod = esc.AltaProducto("GEN-X", 100_000m, stock: 5, categoriaId: esc.CategoriaId);
            esc.Almacen.Cupones.Guardar(new Cupon(
                "USO1", TipoCupon.MontoFijo, 10_000m, Hoy, Manana, usosMaximos: 1));

            esc.Pedidos.Confirmar(
                new[] { new ItemPedido(prod.Id, 1) }, Comprador, "USO1", "TarjetaDebito:1", "Visa");

            // El cliente mandó un cupón que ya no sirve. Cobrarle $100.000
            // cuando esperaba $90.000 sería peor que frenarlo y explicárselo.
            Afirmar.Lanza<ReglaNegocioException>(
                () => esc.Pedidos.Confirmar(
                    new[] { new ItemPedido(prod.Id, 1) }, Comprador, "USO1", "TarjetaDebito:1", "Visa"),
                "se avisa que el cupón ya no aplica");

            Afirmar.Igual(4, esc.Almacen.Productos.PorId(prod.Id)!.Stock,
                "y el stock del pedido rechazado no se tocó");
        });

        c.Prueba("cancelar un pedido devuelve el stock", () =>
        {
            using var esc = new Escenario();
            var prod = esc.AltaProducto("STK", 10_000m, stock: 10, categoriaId: esc.CategoriaId);

            var pedido = esc.Pedidos.Confirmar(
                new[] { new ItemPedido(prod.Id, 4) }, Comprador, null, "Transferencia:1");
            Afirmar.Igual(6, esc.Almacen.Productos.PorId(prod.Id)!.Stock, "bajó a 6");

            esc.Pedidos.Cancelar(pedido.Id, "el cliente se arrepintió");
            Afirmar.Igual(10, esc.Almacen.Productos.PorId(prod.Id)!.Stock, "volvió a 10");
        });

        c.Prueba("no se cancela dos veces (el stock no se devuelve por duplicado)", () =>
        {
            using var esc = new Escenario();
            var prod = esc.AltaProducto("STK", 10_000m, stock: 10, categoriaId: esc.CategoriaId);
            var pedido = esc.Pedidos.Confirmar(
                new[] { new ItemPedido(prod.Id, 4) }, Comprador, null, "Transferencia:1");

            esc.Pedidos.Cancelar(pedido.Id, "primera vez");
            Afirmar.Lanza<ReglaNegocioException>(
                () => esc.Pedidos.Cancelar(pedido.Id, "segunda vez"),
                "ya estaba cancelado");
            Afirmar.Igual(10, esc.Almacen.Productos.PorId(prod.Id)!.Stock, "sigue en 10, no en 14");
        });

        c.Prueba("el código de pedido es único", () =>
        {
            using var esc = new Escenario();
            var prod = esc.AltaProducto("GEN-X", 1_000m, stock: 50, categoriaId: esc.CategoriaId);

            var codigos = new HashSet<string>();
            for (var i = 0; i < 5; i++)
            {
                var p = esc.Pedidos.Confirmar(
                    new[] { new ItemPedido(prod.Id, 1) }, Comprador, null, "Transferencia:1");
                Afirmar.Cierto(codigos.Add(p.Codigo), $"código repetido: {p.Codigo}");
            }
        });

        c.Prueba("nunca se guarda ningún dato de tarjeta, sólo la marca", () =>
        {
            using var esc = new Escenario();
            var prod = esc.AltaProducto("GEN-X", 100_000m, stock: 5, categoriaId: esc.CategoriaId);

            var pedido = esc.Pedidos.Confirmar(
                new[] { new ItemPedido(prod.Id, 1) }, Comprador, null, "TarjetaCredito:6", "Visa");

            esc.Almacen.Confirmar();
            var json = File.ReadAllText(esc.Ruta);

            Afirmar.Contiene("Visa", json, "la marca sí se guarda");
            Afirmar.Falso(json.Contains("numeroTarjeta", StringComparison.OrdinalIgnoreCase),
                "no hay campo de número de tarjeta");
            Afirmar.Falso(json.Contains("cvv", StringComparison.OrdinalIgnoreCase),
                "no hay campo de código de seguridad");
            Afirmar.Igual("Visa", pedido.MarcaTarjeta, "en el pedido queda sólo la marca");
        });

        /* ================================================================
           USUARIOS Y CONTRASEÑAS
           ================================================================ */
        c.Grupo("Usuarios y contraseñas");

        c.Prueba("la contraseña nunca se guarda en claro", () =>
        {
            using var esc = new Escenario();
            esc.Usuarios.Registrar("Ana Pérez", "ana@test.com", "Secreta123");
            esc.Almacen.Confirmar();

            var json = File.ReadAllText(esc.Ruta);
            Afirmar.Falso(json.Contains("Secreta123"), "la contraseña en claro no está en el archivo");
            Afirmar.Contiene("passwordHash", json, "sí está el hash");
        });

        c.Prueba("autenticar con la contraseña correcta devuelve el usuario", () =>
        {
            using var esc = new Escenario();
            esc.Usuarios.Registrar("Ana Pérez", "ana@test.com", "Secreta123");
            Afirmar.Cierto(esc.Usuarios.Autenticar("ana@test.com", "Secreta123") is not null, "entra");
        });

        c.Prueba("autenticar con la contraseña incorrecta devuelve null", () =>
        {
            using var esc = new Escenario();
            esc.Usuarios.Registrar("Ana Pérez", "ana@test.com", "Secreta123");
            Afirmar.Cierto(esc.Usuarios.Autenticar("ana@test.com", "otra") is null, "no entra");
        });

        c.Prueba("dos usuarios con la misma contraseña tienen hashes distintos (salt por usuario)", () =>
        {
            using var esc = new Escenario();
            var a = esc.Usuarios.Registrar("Ana", "ana@test.com", "Secreta123");
            var b = esc.Usuarios.Registrar("Beto", "beto@test.com", "Secreta123");
            Afirmar.Falso(a.PasswordHash == b.PasswordHash, "el salt hace que difieran");
        });

        c.Prueba("no se puede registrar dos veces el mismo email", () =>
        {
            using var esc = new Escenario();
            esc.Usuarios.Registrar("Ana", "ana@test.com", "Secreta123");
            Afirmar.Lanza<ReglaNegocioException>(
                () => esc.Usuarios.Registrar("Otra Ana", "ana@test.com", "Secreta123"),
                "email duplicado");
        });

        c.Prueba("no se puede dejar al sistema sin ningún admin", () =>
        {
            using var esc = new Escenario();
            var admin = esc.Usuarios.Registrar(
                "Jefe", "jefe@test.com", "Secreta123", rol: RolUsuario.Admin);

            Afirmar.Lanza<ReglaNegocioException>(
                () => esc.Usuarios.CambiarRol(admin.Id, RolUsuario.Cliente),
                "es el último admin activo");
        });

        /* ================================================================
           PERSISTENCIA
           ================================================================ */
        c.Grupo("Persistencia");

        c.Prueba("lo guardado se vuelve a leer igual desde el archivo", () =>
        {
            var ruta = Path.Combine(Path.GetTempPath(), $"hs-{Guid.NewGuid():N}.json");
            try
            {
                var almacen = new AlmacenJson(ruta);
                var cat = almacen.Categorias.Guardar(new Categoria("Pruebas"));
                almacen.Productos.Guardar(new Producto("PER-1", "Producto persistido", cat.Id, 4_321m, 7));
                almacen.Confirmar();

                var recargado = new AlmacenJson(ruta);
                var prod = recargado.Productos.PorCodigo("PER-1");

                Afirmar.Cierto(prod is not null, "el producto sobrevivió al reinicio");
                Afirmar.Igual(4_321m, prod!.Precio, "precio");
                Afirmar.Igual(7, prod.Stock, "stock");
                Afirmar.Igual("Producto persistido", prod.Nombre, "nombre");
            }
            finally
            {
                if (File.Exists(ruta)) File.Delete(ruta);
            }
        });

        c.Prueba("un archivo corrupto no tira la aplicación: arranca vacío", () =>
        {
            var ruta = Path.Combine(Path.GetTempPath(), $"hs-{Guid.NewGuid():N}.json");
            try
            {
                File.WriteAllText(ruta, "{ esto no es JSON válido ]]]");
                var almacen = new AlmacenJson(ruta);
                Afirmar.Cierto(almacen.EstaVacio, "arranca limpio en lugar de explotar");
            }
            finally
            {
                foreach (var f in Directory.GetFiles(Path.GetTempPath(),
                             Path.GetFileNameWithoutExtension(ruta) + "*"))
                {
                    File.Delete(f);
                }
            }
        });

        return c.Correr();
    }

    /* ====================================================================
       Ayudas
       ==================================================================== */

    private static DateTime Hoy => DateTime.Today;
    private static DateTime Manana => DateTime.Today.AddDays(30);

    private static DatosComprador Comprador =>
        new("Martín Sosa", "martin@prueba.test", "1122334455", "Calle Falsa 123", "");

    private static Promocion Promo3x2() =>
        Promocion.LlevaNPaga("3x2", AlcancePromocion.Producto, 1, 3, 2, Hoy, Manana);

    private static Promocion Promo2x1() =>
        Promocion.LlevaNPaga("2x1", AlcancePromocion.Producto, 1, 2, 1, Hoy, Manana);

    /// <summary>
    /// Un almacén JSON en un archivo temporal, con una categoría lista y los
    /// servicios armados. Al liberarse borra el archivo, así cada prueba
    /// arranca desde cero.
    /// </summary>
    private sealed class Escenario : IDisposable
    {
        public string Ruta { get; }
        public AlmacenJson Almacen { get; }
        public CotizadorServicio Cotizador { get; }
        public PedidoServicio Pedidos { get; }
        public UsuarioServicio Usuarios { get; }
        public int CategoriaId { get; }

        public Escenario()
        {
            Ruta = Path.Combine(Path.GetTempPath(), $"hs-prueba-{Guid.NewGuid():N}.json");
            Almacen = new AlmacenJson(Ruta);
            Cotizador = new CotizadorServicio(Almacen);
            Pedidos = new PedidoServicio(Almacen, Cotizador);
            Usuarios = new UsuarioServicio(Almacen);
            CategoriaId = Almacen.Categorias.Guardar(new Categoria("Componentes")).Id;
        }

        public Producto AltaProducto(string codigo, decimal precio, int stock, int categoriaId) =>
            Almacen.Productos.Guardar(
                new Producto(codigo, $"Producto {codigo}", categoriaId, precio, stock, 1));

        public void Dispose()
        {
            foreach (var f in Directory.GetFiles(
                         Path.GetDirectoryName(Ruta)!,
                         Path.GetFileNameWithoutExtension(Ruta) + "*"))
            {
                try { File.Delete(f); } catch { /* archivo tomado: no importa */ }
            }
        }
    }
}
