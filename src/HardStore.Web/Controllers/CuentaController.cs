using System.Security.Claims;
using HardStore.Web.Dominio;
using HardStore.Web.Modelos;
using HardStore.Web.Servicios;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HardStore.Web.Controllers;

/// <summary>
/// CAPA: Presentación · Ingreso, registro y cuenta del cliente.
///
/// El rol viaja como claim en la cookie de autenticación. Es lo que permite que
/// el panel se proteja con [Authorize(Roles = "Admin")] sin consultar la base
/// en cada petición.
/// </summary>
public sealed class CuentaController : Controller
{
    private readonly UsuarioServicio _usuarios;
    private readonly PedidoServicio _pedidos;

    public CuentaController(UsuarioServicio usuarios, PedidoServicio pedidos)
    {
        _usuarios = usuarios;
        _pedidos = pedidos;
    }

    /* ------------------------------- Ingresar ---------------------------- */

    [HttpGet]
    public IActionResult Ingresar(string? volverA)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return Redirect(DestinoSeguro(volverA));
        }

        return View(new IngresarVm { VolverA = volverA });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Ingresar(IngresarVm vm)
    {
        if (!ModelState.IsValid)
        {
            return View(vm);
        }

        var usuario = _usuarios.Autenticar(vm.Email, vm.Password);

        if (usuario is null)
        {
            // Mensaje genérico a propósito: no delatamos si falló el email o
            // la contraseña. Es la misma decisión que en la versión PHP.
            ModelState.AddModelError(string.Empty, "Email o contraseña incorrectos.");
            return View(vm);
        }

        await IniciarSesionAsync(usuario, vm.Recordarme);

        // Si es admin y no venía de ningún lado, va derecho al panel.
        if (usuario.EsAdmin && string.IsNullOrWhiteSpace(vm.VolverA))
        {
            return RedirectToAction("Index", "Panel");
        }

        return Redirect(DestinoSeguro(vm.VolverA));
    }

    /* ------------------------------ Registrarse -------------------------- */

    [HttpGet]
    public IActionResult Registrarse()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Tienda");
        }

        return View(new RegistrarseVm());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Registrarse(RegistrarseVm vm)
    {
        if (!ModelState.IsValid)
        {
            return View(vm);
        }

        Usuario usuario;
        try
        {
            usuario = _usuarios.Registrar(
                vm.Nombre, vm.Email, vm.Password, vm.Telefono ?? "", vm.Direccion ?? "");
        }
        catch (ReglaNegocioException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(vm);
        }

        // Registro exitoso: se lo loguea directamente, mejor experiencia.
        await IniciarSesionAsync(usuario, recordarme: true);

        TempData["Exito"] = $"¡Bienvenido, {usuario.PrimerNombre}! Tu cuenta quedó creada.";
        return RedirectToAction("Index", "Tienda");
    }

    /* --------------------------------- Salir ----------------------------- */

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Salir()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        TempData["Info"] = "Cerraste sesión.";
        return RedirectToAction("Index", "Tienda");
    }

    /* ------------------------------ Mis pedidos -------------------------- */

    [HttpGet]
    [Authorize]
    public IActionResult MisPedidos()
    {
        var id = IdUsuarioActual();
        if (id is null)
        {
            return Challenge();
        }

        return View(_pedidos.DeUsuario(id.Value));
    }

    [HttpGet]
    public IActionResult SinPermiso() => View();

    /* -------------------------------- Interno ---------------------------- */

    private async Task IniciarSesionAsync(Usuario usuario, bool recordarme)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, usuario.Id.ToString()),
            new(ClaimTypes.Name, usuario.Nombre),
            new(ClaimTypes.Email, usuario.Email),
            new(ClaimTypes.Role, usuario.Rol.ToString()),
        };

        var identidad = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identidad),
            new AuthenticationProperties
            {
                IsPersistent = recordarme,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(recordarme ? 7 : 1),
            });
    }

    private int? IdUsuarioActual()
        => int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;

    /// <summary>
    /// Sólo se acepta volver a una URL local. Sin este control, un enlace del
    /// tipo /Cuenta/Ingresar?volverA=https://sitio-falso sería un redirect
    /// abierto: el clásico truco de phishing sobre un login legítimo.
    /// </summary>
    private string DestinoSeguro(string? volverA)
        => !string.IsNullOrWhiteSpace(volverA) && Url.IsLocalUrl(volverA)
            ? volverA
            : Url.Action("Index", "Tienda")!;
}
