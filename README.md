# HardStore — tienda de hardware en C# (ASP.NET Core MVC)

Port a C# de la tienda que antes estaba en PHP + MySQL. Mismo diseño visual y
mismas pantallas, más lo que se pidió agregar:

- **Panel de administración** con cuenta de admin: alta, baja y modificación de
  productos, categorías, promociones, cupones, pedidos y usuarios, todo desde
  la propia página.
- **Carga de imágenes** desde el formulario del producto. Ya no hay que escribir
  ningún `UPDATE productos SET imagen = ...` a mano.
- **Medios de pago**: transferencia, efectivo, débito y crédito en 1, 3, 6 y 12
  cuotas, cada uno con su descuento o recargo.
- **Promociones 3x2 / 2x1** y descuentos porcentuales por producto o categoría.
- **Códigos de descuento** con mínimo de compra, tope y cantidad de usos.

---

## 1. Cómo hacerlo andar

Necesitás el **SDK de .NET 8** ([descarga](https://dotnet.microsoft.com/download/dotnet/8.0)).
Para comprobar que está instalado:

```bash
dotnet --version        # tiene que decir 8.x
```

Desde la carpeta del proyecto:

```bash
dotnet run --project src/HardStore.Web
```

Y abrir en el navegador la dirección que imprime la consola, normalmente:

```
http://localhost:5187
```

No hay que instalar MySQL, ni XAMPP, ni ejecutar ningún script SQL. La primera
vez que arranca, el sistema crea `src/HardStore.Web/App_Data/hardstore.json` con
el catálogo de ejemplo cargado.

### Cuentas de prueba

| Rol | Email | Contraseña |
|---|---|---|
| Administrador | `admin@hardstore.test` | `Admin1234` |
| Cliente | `laura@cliente.test` | `Cliente123` |

El admin, al entrar, va directo al panel (`/admin`). Un cliente que intente
entrar ahí recibe «no tenés permiso»; un visitante sin sesión va al login.

### Para empezar de cero

Borrá `src/HardStore.Web/App_Data/hardstore.json` y volvé a levantar el
proyecto: se siembra de nuevo.

---

## 2. Cómo está organizado

```
HardStore/
├── HardStore.sln
├── NuGet.config                  ← sin fuentes: compila sin internet
├── src/HardStore.Web/
│   ├── Program.cs                ← composition root: acá se elige TODO
│   ├── Dominio/                  ← reglas del negocio, sin dependencias
│   │   ├── Producto.cs  Categoria.cs  Usuario.cs  Pedido.cs
│   │   ├── Promocion.cs  Cupon.cs  MedioPago.cs
│   │   └── Precios/Cotizacion.cs
│   ├── Datos/                    ← persistencia
│   │   ├── Repositorios.cs       ← las 6 interfaces + IAlmacen
│   │   ├── Memoria/              ← repositorios sobre listas en memoria
│   │   ├── Json/                 ← almacén en archivo JSON (por defecto)
│   │   ├── EfCore/               ← almacén con EF Core + SQLite
│   │   └── DatosIniciales.cs     ← el catálogo de ejemplo
│   ├── Servicios/                ← casos de uso
│   │   ├── CotizadorServicio.cs  ← el motor de precios
│   │   ├── PedidoServicio.cs     ← checkout y stock
│   │   ├── CatalogoServicio.cs   UsuarioServicio.cs  ImagenServicio.cs
│   ├── Controllers/              ← Tienda, Cuenta, Checkout, Panel, Api
│   ├── Modelos/VistaModelos.cs   ← modelos de vista (evitan over-posting)
│   ├── Views/                    ← Razor
│   └── wwwroot/                  ← css, js, imágenes
└── tests/HardStore.Pruebas/      ← 52 pruebas, sin dependencias
```

Es la misma separación en capas del otro trabajo práctico (Entidades / Negocio /
Interfaz), con los nombres que se usan habitualmente en .NET:

| Capa | Carpeta | Depende de |
|---|---|---|
| Entidades | `Dominio/` | nada |
| Negocio | `Servicios/` | `Dominio/` y las interfaces de `Datos/` |
| Interfaz | `Controllers/`, `Views/`, `wwwroot/` | `Servicios/` |
| Datos | `Datos/` | `Dominio/` |

Las flechas apuntan siempre hacia adentro: `Dominio` no conoce a nadie.
`Servicios` depende de **interfaces** (`IProductoRepositorio`, `IAlmacen`), no de
una base de datos concreta, y es lo que permite cambiar JSON por SQLite sin
tocar una línea de negocio.

---

## 3. Los dos modos de almacenamiento

El proyecto trae **dos implementaciones de `IAlmacen`**:

| Modo | Dónde guarda | Paquetes NuGet | Estado |
|---|---|---|---|
| **JSON** (por defecto) | `App_Data/hardstore.json` | ninguno | verificado de punta a punta |
| **EF Core + SQLite** | `App_Data/hardstore.db` | 2 paquetes | ver la nota de abajo |

Para levantarlo con EF Core:

```bash
rm NuGet.config                                       # deja que se bajen los paquetes
dotnet run --project src/HardStore.Web -p:ConEfCore=true
```

`ConEfCore=true` define la constante `CON_EF_CORE`, que es la que habilita la
compilación de `Datos/EfCore/` y del bloque correspondiente de `Program.cs`.
Sin esa constante, esos archivos ni se compilan: por eso el modo por defecto no
necesita ningún paquete.

> **Nota honesta sobre el modo EF Core.** Se escribió y se revisó, y compila
> contra la superficie de la API de EF Core, pero **no pude ejecutarlo**: la
> máquina donde armé el proyecto no tiene acceso a NuGet, así que los paquetes
> nunca se bajaron. El modo JSON, en cambio, está verificado corriendo de
> verdad: 52 pruebas del núcleo más 34 comprobaciones sobre el navegador
> (compra completa, panel, subida de imagen, descuento de stock). Si vas a usar
> EF Core, probalo primero.

El esquema relacional que genera EF Core es el mismo que el `schema.sql` de la
versión PHP: tablas `categorias`, `productos`, `usuarios`, `pedidos`,
`detalle_pedidos`, más `promociones` y `cupones`, con las mismas decisiones de
borrado (`RESTRICT` en categoría con productos, `SET NULL` en el usuario de un
pedido, `CASCADE` de pedido a detalle).

---

## 4. Cómo se calcula un precio

Éste es el corazón del sistema (`Servicios/CotizadorServicio.cs`). **El orden
importa**, y es siempre éste:

```
1. Bruto                = precio de lista × cantidad
2. – Promociones        (por línea: la MEJOR que aplique, nunca dos sumadas)
3. – Cupón              (sobre el subtotal ya promocionado)
4. × Coeficiente de pago (descuento o recargo del medio elegido)
   = TOTAL
```

Ejemplo real, verificado por las pruebas — 3 SSD Kingston NV2 de $98.500 con la
promo 3x2, el cupón `BIENVENIDO10` y pago por transferencia:

| Paso | Importe |
|---|---|
| Bruto (3 × 98.500) | $ 295.500,00 |
| Promoción 3x2 (1 unidad sin cargo) | – $ 98.500,00 |
| Subtotal | $ 197.000,00 |
| Cupón BIENVENIDO10 (10 %) | – $ 19.700,00 |
| Base | $ 177.300,00 |
| Transferencia (–10 %) | – $ 17.730,00 |
| **Total** | **$ 159.570,00** |

### Medios de pago

| Medio | Coeficiente | Efecto |
|---|---|---|
| Transferencia bancaria | 0,90 | 10 % de descuento |
| Efectivo al retirar | 0,90 | 10 % de descuento |
| Tarjeta de débito | 1,00 | precio de lista |
| Crédito 1 pago | 1,00 | precio de lista |
| Crédito 3 cuotas | 1,00 | sin interés |
| Crédito 6 cuotas | 1,15 | 15 % de recargo |
| Crédito 12 cuotas | 1,35 | 35 % de recargo |

Las cuotas se redondean de forma que **la suma dé exactamente el total**: el
resto de la división va en la última cuota, no se pierde ni se inventa un
centavo. Hay una prueba para eso.

### Promociones

- **NxM** (`3x2`, `2x1`, `4x3`…): bonifica `N − M` unidades por cada grupo
  completo de `N`. Con 7 unidades y un 3x2 son 2 bonificadas, no 2,33.
- **Porcentual** sobre la línea.
- Alcance por **producto** o por **categoría entera**.
- Si dos promociones aplican a la misma línea, se toma **la más conveniente para
  el cliente**. Nunca se suman: eso es lo que produce descuentos absurdos.

### Cupones

Cada cupón tiene tipo (porcentaje o monto fijo), mínimo de compra, tope de
descuento, vigencia y cantidad máxima de usos. Si el cupón que escribió el
cliente no aplica, **el checkout se detiene y se lo explica** en lugar de
cobrarle de más en silencio.

### Sobre las tarjetas: qué NO se pide

El sistema pide **la marca de la tarjeta y la cantidad de cuotas, nada más**.
No hay ningún campo, ninguna columna ni ninguna línea de código que pida o
guarde número de tarjeta, vencimiento o código de seguridad. Guardar esos datos
exige cumplir PCI-DSS y no tiene sentido en un trabajo así: un cobro real se
delega a una pasarela (Mercado Pago, Stripe), que es la que ve la tarjeta.
Hay una prueba que abre el archivo de datos y verifica que ahí sólo esté la
marca.

---

## 5. Carga de imágenes

En `Panel → Productos → Nuevo/Editar` hay un campo de archivo con vista previa.
`Servicios/ImagenServicio.cs` hace lo siguiente antes de aceptar el archivo:

1. Límite de **4 MB**.
2. Extensión en lista blanca: `.jpg`, `.jpeg`, `.png`, `.gif`, `.webp`.
3. **Revisa los primeros bytes del archivo** para confirmar que el contenido es
   realmente una imagen. Renombrar `virus.exe` a `foto.jpg` no alcanza: el
   contenido no tiene la firma de JPEG/PNG/GIF/WebP y se rechaza.
4. Guarda con un nombre nuevo tipo GUID en `wwwroot/img/productos/`, así nadie
   elige el nombre del archivo que queda en el servidor.
5. Al borrar, comprueba que la ruta esté dentro de esa carpeta (para que un
   `../../` no pueda borrar otra cosa).

El archivo se escribe **después** de validar el producto: si el formulario tiene
un error, no queda una imagen huérfana en el disco.

---

## 6. Seguridad

| Riesgo | Qué se hace |
|---|---|
| Inyección SQL | En modo EF Core, todo va por consultas parametrizadas; en modo JSON no hay SQL. Nunca se concatena entrada del usuario en una consulta. |
| Contraseñas | PBKDF2-SHA256, 210.000 iteraciones, salt de 16 bytes distinto por usuario. Comparación en tiempo constante y hash señuelo para que un email inexistente tarde lo mismo que uno real. |
| CSRF | Token antiforgery en todos los POST, incluidas las llamadas `fetch()` (viaja en el header `RequestVerificationToken`). |
| XSS | Razor escapa por defecto; no se usa `@Html.Raw` con datos del usuario. |
| Redirect abierto | El `volverA` del login pasa por `Url.IsLocalUrl`. |
| Over-posting | Los formularios atan a modelos de vista, no a las entidades: mandar `Stock=9999` en el formulario de edición no hace nada. |
| Precios manipulados | **El carrito de `localStorage` guarda sólo `{id, cantidad}`.** Todos los precios, promociones, cupones y recargos los calcula el servidor y se recalculan al confirmar. Editar el `localStorage` a mano no cambia lo que se cobra. |
| Escalada de privilegios | El panel está protegido con `[Authorize(Roles = "Admin")]`, y el sistema se niega a quitarle el rol al último admin activo (si no, quedaría una tienda sin dueño). |
| Venta doble del último producto | El checkout entero corre bajo un candado y valida el stock **sin recortar** antes de tocar nada: o entra el pedido completo, o no entra nada. |

---

## 7. Las pruebas

```bash
dotnet run --project tests/HardStore.Pruebas
```

52 pruebas del dominio y de los servicios: promociones NxM y porcentuales,
elección de la mejor promoción, cupones (tope, mínimo, usos, vencimiento),
coeficientes y redondeo de cuotas, el orden completo del cálculo, recorte por
stock, atomicidad del checkout, devolución de stock al cancelar, unicidad del
código de pedido, hasheo de contraseñas y persistencia.

No usa xUnit a propósito: el corredor está en `tests/HardStore.Pruebas/Corredor.cs`
y son unas 60 líneas, así que las pruebas corren en cualquier máquina con el SDK,
sin bajar ningún paquete.

Además se verificó la aplicación con un navegador real (Playwright): compra
completa con promo, cupón y transferencia; confirmación del pedido; login del
admin; alta de producto con imagen; creación de promoción y cupón desde el
panel; y descuento efectivo del stock. 34 comprobaciones, todas en verde.

---

## 8. Errores que se encontraron y se corrigieron en el camino

La consigna pedía buscar errores. Éstos son los que aparecieron, con lo que
enseñó cada uno:

| Problema | Causa | Corrección |
|---|---|---|
| El checkout no confirmaba nunca y **no mostraba ningún error** | Con *nullable reference types* activado, ASP.NET Core marca como obligatorio todo `string` no anulable. `Observaciones` (opcional) quedaba requerido, y el resumen de validación mostraba sólo los errores de modelo, así que el mensaje quedaba invisible. | Los campos opcionales pasaron a `string?` y el resumen del checkout ahora muestra **todos** los errores (`asp-validation-summary="All"`). |
| El modelo de vista del checkout se intentaba enlazar entero | `Cotizacion` y `Comparativa` son datos de sólo lectura para la pantalla, pero el enlazador los tomaba como campos del formulario y les generaba errores de validación. | `[BindNever]` y `[ValidateNever]` en todas las propiedades que sólo sirven para mostrar. |
| Los mismos repositorios servían para los dos almacenes, pero se llamaban `...RepositorioJson` | Un renombre que había quedado a medias. | Movidos a `Datos/Memoria/` con el nombre `...RepositorioEnMemoria`, que es lo que son. |
| Avisos del compilador por posibles `null` | Campos de formulario que pueden llegar vacíos. | Normalización en un solo lugar (`DatosComprador`) en vez de `?? ""` desparramado. Compila con 0 advertencias. |

Y estos tres venían de la versión PHP y se evitaron por diseño:

| Bug de la versión PHP | Cómo se evita acá |
|---|---|
| Todas las ventanas modales abiertas a la vez y el botón de cerrar sin efecto (`display:grid` del CSS le ganaba a `[hidden]`) | La regla `[hidden] { display: none !important; }` está en el CSS desde el principio, y las capas se ocultan sin depender del orden. |
| `SQLSTATE[HY093]`: el mismo marcador `:q` repetido en una consulta preparada rompía la búsqueda y el checkout | No hay SQL escrito a mano. En modo EF Core, los parámetros los genera el proveedor. |
| Descuento de stock no atómico: si la tercera línea no tenía stock, las dos primeras quedaban descontadas | El checkout valida **todas** las líneas antes de modificar nada, dentro de un candado, y en modo EF Core además dentro de una transacción. |

---

## 9. Qué queda afuera

Para que quede claro qué es y qué no es este proyecto:

- **No cobra de verdad.** No hay pasarela de pago: al confirmar, el pedido queda
  registrado como pendiente y la pantalla lo dice.
- **No manda mails.** El comprobante se muestra en pantalla.
- El modo JSON guarda todo en un archivo. Alcanza para una tienda chica y para
  una demo; para producción va el modo EF Core (o directamente PostgreSQL /
  MySQL, que es cambiar el `UseSqlite` por el proveedor correspondiente).
