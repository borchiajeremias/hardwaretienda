using System.Globalization;
using HardStore.Web.Datos;
using HardStore.Web.Servicios;
using Microsoft.AspNetCore.Authentication.Cookies;
#if CON_EF_CORE
using Microsoft.EntityFrameworkCore;
#else
using HardStore.Web.Datos.Json;
#endif

// =============================================================================
//  HardStore · Programa principal
//  -----------------------------------------------------------------------------
//  Éste es el «programa principal» de la versión C#: el equivalente al conjunto
//  de index.php + config.php + conexion.php de la versión PHP, pero en un solo
//  archivo y con inyección de dependencias.
//
//  Es también el ÚNICO lugar donde se decide qué implementación concreta usa
//  cada capa (composition root). Cambiar el almacenamiento es cambiar una línea.
// =============================================================================

var builder = WebApplication.CreateBuilder(args);

// -----------------------------------------------------------------------------
// Cultura: los precios se muestran y se parsean en formato argentino
// ($ 1.234,56). Si no se fija, el servidor usa la del sistema operativo y los
// separadores cambian según dónde se despliegue.
// -----------------------------------------------------------------------------
var culturaAr = new CultureInfo("es-AR");
CultureInfo.DefaultThreadCurrentCulture = culturaAr;
CultureInfo.DefaultThreadCurrentUICulture = culturaAr;

// -----------------------------------------------------------------------------
// 1. ALMACENAMIENTO
//
//    Por defecto: archivo JSON con System.Text.Json. Cero paquetes NuGet, así
//    que el proyecto arranca con «dotnet run» sin instalar ni configurar nada.
//
//    Para pasar a EF Core + SQLite:
//       1) borrá NuGet.config (está para que la restauración funcione sin
//          internet; con él puesto no se pueden bajar los paquetes de EF)
//       2) compilá o corré con  -p:ConEfCore=true
//    No hay nada más que cambiar: ni los servicios ni los controladores saben
//    de dónde salen los datos, sólo dependen de IAlmacen.
// -----------------------------------------------------------------------------
var carpetaDatos = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
Directory.CreateDirectory(carpetaDatos);

#if CON_EF_CORE

// --- EF Core + SQLite -------------------------------------------------------
// AddDbContextFactory y no AddDbContext: el almacén es singleton (mantiene el
// estado en memoria y el candado del checkout), así que no puede depender de un
// DbContext scoped — inyectarle uno sería el clásico «captured dependency», con
// un contexto ya liberado en la segunda petición. La fábrica le deja abrir uno
// nuevo, corto, cada vez que necesita leer o escribir.
builder.Services.AddDbContextFactory<HardStore.Web.Datos.EfCore.HardStoreContext>(opciones =>
    opciones.UseSqlite(
        builder.Configuration.GetConnectionString("Sqlite")
        ?? $"Data Source={Path.Combine(carpetaDatos, "hardstore.db")}"));

builder.Services.AddSingleton<IAlmacen, HardStore.Web.Datos.EfCore.AlmacenEf>();

#else

// --- Archivo JSON (modo por defecto) ---------------------------------------
var rutaDatos = builder.Configuration["Almacen:RutaJson"]
    ?? Path.Combine(carpetaDatos, "hardstore.json");

builder.Services.AddSingleton<IAlmacen>(_ => new AlmacenJson(rutaDatos));

#endif

// -----------------------------------------------------------------------------
// 2. SERVICIOS DE NEGOCIO
//
//    Son singleton porque el almacén JSON es singleton y guarda el estado en
//    memoria. Con EF Core pasarían a Scoped, igual que el DbContext.
// -----------------------------------------------------------------------------
builder.Services.AddSingleton<ImagenServicio>();
builder.Services.AddSingleton<UsuarioServicio>();
builder.Services.AddSingleton<CatalogoServicio>();
builder.Services.AddSingleton<CotizadorServicio>();
builder.Services.AddSingleton<PedidoServicio>();

// -----------------------------------------------------------------------------
// 3. AUTENTICACIÓN Y AUTORIZACIÓN
//
//    Cookie de autenticación propia, sin ASP.NET Identity: alcanza para dos
//    roles y no agrega paquetes. El rol viaja como claim, así [Authorize(Roles
//    = "Admin")] funciona igual que con Identity.
// -----------------------------------------------------------------------------
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(opciones =>
    {
        opciones.Cookie.Name = "HardStore.Auth";
        opciones.Cookie.HttpOnly = true;                        // inaccesible desde JS
        opciones.Cookie.SameSite = SameSiteMode.Lax;            // mitiga CSRF
        opciones.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        opciones.ExpireTimeSpan = TimeSpan.FromDays(7);
        opciones.SlidingExpiration = true;
        opciones.LoginPath = "/Cuenta/Ingresar";
        opciones.LogoutPath = "/Cuenta/Salir";
        opciones.AccessDeniedPath = "/Cuenta/SinPermiso";
        opciones.ReturnUrlParameter = "volverA";
    });

builder.Services.AddAuthorization(opciones =>
{
    opciones.AddPolicy("EsAdmin", politica => politica.RequireRole("Admin"));
});

// -----------------------------------------------------------------------------
// 4. MVC + antifalsificación (el equivalente al token CSRF de la versión PHP)
// -----------------------------------------------------------------------------
builder.Services.AddControllersWithViews();

builder.Services.AddAntiforgery(opciones =>
{
    opciones.HeaderName = "RequestVerificationToken";   // para las llamadas con fetch()
    opciones.Cookie.Name = "HardStore.Antiforgery";
    opciones.Cookie.HttpOnly = true;
});

// Límite del cuerpo de las peticiones: el formulario de producto sube una
// imagen, y sin tope alguien podría intentar agotar la memoria del servidor.
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(opciones =>
{
    opciones.MultipartBodyLengthLimit = ImagenServicio.TamanoMaximo + 512 * 1024;
});

var app = builder.Build();

// -----------------------------------------------------------------------------
// 5. SIEMBRA INICIAL
//    Sólo la primera vez: si ya hay datos, no toca nada.
// -----------------------------------------------------------------------------
using (var alcance = app.Services.CreateScope())
{
    var almacen = alcance.ServiceProvider.GetRequiredService<IAlmacen>();
    var usuarios = alcance.ServiceProvider.GetRequiredService<UsuarioServicio>();
    var log = alcance.ServiceProvider.GetRequiredService<ILogger<Program>>();

    DatosIniciales.Sembrar(almacen, usuarios, log);
}

// -----------------------------------------------------------------------------
// 6. CANALIZACIÓN (middleware)
// -----------------------------------------------------------------------------
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Tienda/Error");
    app.UseHsts();
}

// Cabeceras de seguridad, equivalentes a las que la versión PHP ponía en el
// .htaccess. Se aplican a todas las respuestas.
app.Use(async (contexto, siguiente) =>
{
    var cabeceras = contexto.Response.Headers;
    cabeceras["X-Content-Type-Options"] = "nosniff";
    cabeceras["X-Frame-Options"] = "SAMEORIGIN";
    cabeceras["Referrer-Policy"] = "strict-origin-when-cross-origin";
    cabeceras["Permissions-Policy"] = "geolocation=(), camera=(), microphone=()";

    await siguiente();
});

app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// -----------------------------------------------------------------------------
// 7. RUTAS
// -----------------------------------------------------------------------------
// Los controladores del panel usan enrutamiento por atributos ([Route("admin/...")]),
// así que basta con habilitarlo y dejar la ruta convencional para la tienda.
app.MapControllers();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Tienda}/{action=Index}/{id?}");

app.Run();
