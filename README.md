# HardStore — tienda de hardware en C# (ASP.NET Core MVC)

1. Cómo hacerlo andar

Descargar como un zip y luego extraer.

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

Cuentas de prueba

Rol Email Contraseña 
|---|---|---|
 Administrador  `admin@hardstore.test`  `Admin1234` 
 Cliente  `laura@cliente.test`  `Cliente123` 

El admin, al entrar, va directo al panel (`/admin`). Un cliente que intente
entrar ahí recibe «no tenés permiso»; un visitante sin sesión va al login.

Para empezar de cero

Borrá `src/HardStore.Web/App_Data/hardstore.json` y volvé a levantar el
proyecto: se siembra de nuevo.

---

2. Cómo está organizado

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

