using System.Security.Claims;
using System.Text.Json;
using HardStore.Web.Dominio;
using HardStore.Web.Modelos;
using HardStore.Web.Servicios;
using Microsoft.AspNetCore.Mvc;

namespace HardStore.Web.Controllers;

/// <summary>
/// CAPA: Presentación · Checkout.
///
/// El carrito viaja desde localStorage en un campo oculto con la forma
/// [{"id":1,"cantidad":2}] — sólo ids y cantidades. Los precios los pone el
/// servidor. Si alguien edita ese campo para poner precio 1, no cambia nada:
/// el precio ni siquiera se lee de ahí.
/// </summary>
public sealed class CheckoutController : Controller
{
    private readonly CotizadorServicio _cotizador;
    private readonly PedidoServicio _pedidos;
    private readonly UsuarioServicio _usuarios;

    private static readonly JsonSerializerOptions OpcionesJson = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public CheckoutController(
        CotizadorServicio cotizador,
        PedidoServicio pedidos,
        UsuarioServicio usuarios)
    {
        _cotizador = cotizador;
        _pedidos = pedidos;
        _usuarios = usuarios;
    }

    /// <summary>Pantalla de checkout. El carrito llega por querystring o lo carga el JS.</summary>
    [HttpGet]
    public IActionResult Index(string? items, string? cupon, string? plan)
    {
        var lista = LeerItems(items);
        var cotizacion = _cotizador.Cotizar(lista, cupon, plan);

        var vm = new CheckoutVm
        {
            Cotizacion = cotizacion,
            Comparativa = _cotizador.Comparativa(cotizacion),
            PlanPago = cotizacion.Plan.Clave,
            Cupon = cotizacion.CuponCodigo ?? cupon,
            ItemsJson = JsonSerializer.Serialize(
                lista.Select(i => new { id = i.ProductoId, cantidad = i.Cantidad })),
        };

        // Si está logueado, se precargan sus datos.
        if (IdUsuarioActual() is { } id)
        {
            try
            {
                var usuario = _usuarios.Obtener(id);
                vm.Nombre = usuario.Nombre;
                vm.Email = usuario.Email;
                vm.Telefono = usuario.Telefono;
                vm.Direccion = usuario.Direccion;
            }
            catch (NoEncontradoException)
            {
                // Cookie de un usuario que ya no existe: se ignora.
            }
        }

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Confirmar(CheckoutVm vm)
    {
        var lista = LeerItems(vm.ItemsJson);

        // Se recotiza siempre para volver a pintar la pantalla si algo falla.
        var cotizacion = _cotizador.Cotizar(lista, vm.Cupon, vm.PlanPago);
        vm.Cotizacion = cotizacion;
        vm.Comparativa = _cotizador.Comparativa(cotizacion);

        if (!ModelState.IsValid)
        {
            return View(nameof(Index), vm);
        }

        if (cotizacion.EstaVacia)
        {
            ModelState.AddModelError(string.Empty, "Tu carrito está vacío.");
            return View(nameof(Index), vm);
        }

        Pedido pedido;
        try
        {
            pedido = _pedidos.Confirmar(
                items: lista,
                comprador: new DatosComprador(
                    vm.Nombre, vm.Email,
                    vm.Telefono, vm.Direccion, vm.Observaciones),
                codigoCupon: vm.Cupon,
                clavePlanPago: vm.PlanPago,
                marcaTarjeta: vm.MarcaTarjeta,
                usuarioId: IdUsuarioActual());
        }
        catch (ReglaNegocioException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(nameof(Index), vm);
        }

        return RedirectToAction(nameof(Gracias), new { codigo = pedido.Codigo });
    }

    /// <summary>Comprobante del pedido recién hecho.</summary>
    [HttpGet]
    public IActionResult Gracias(string codigo)
    {
        try
        {
            return View(_pedidos.ObtenerPorCodigo(codigo));
        }
        catch (NoEncontradoException)
        {
            return NotFound();
        }
    }

    /* -------------------------------- Interno ---------------------------- */

    private static List<ItemPedido> LeerItems(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<ItemPedido>();
        }

        try
        {
            var crudos = JsonSerializer.Deserialize<List<ItemCrudo>>(json, OpcionesJson);
            return (crudos ?? new List<ItemCrudo>())
                .Where(i => i.Id > 0 && i.Cantidad > 0)
                .Select(i => new ItemPedido(i.Id, i.Cantidad))
                .ToList();
        }
        catch (JsonException)
        {
            // Campo manipulado o corrupto: se trata como carrito vacío.
            return new List<ItemPedido>();
        }
    }

    private sealed record ItemCrudo(int Id, int Cantidad);

    private int? IdUsuarioActual()
        => int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;
}
