namespace HardStore.Web.Dominio;

public enum MedioPago
{
    Transferencia = 0,
    Efectivo = 1,
    TarjetaDebito = 2,
    TarjetaCredito = 3,
}

/// <summary>
/// CAPA: Dominio · Un plan de pago concreto: el medio más la cantidad de cuotas.
///
/// Modela la realidad del comercio argentino: la transferencia y el efectivo
/// tienen descuento porque el vendedor no paga comisión, el débito sale igual
/// que la lista, y el crédito en cuotas tiene recargo por el costo financiero.
///
/// Los planes son datos, no clases: se declaran una vez acá y tanto el checkout
/// como el pedido guardado usan el mismo coeficiente. Si el coeficiente viviera
/// en la pantalla, el total mostrado y el total cobrado podrían separarse.
///
/// NOTA DE SEGURIDAD: en ninguna parte del sistema se piden ni se guardan
/// números de tarjeta, vencimientos ni códigos de seguridad. El cliente elige
/// marca y cuotas; el cobro real lo haría una pasarela de pago. Guardar datos
/// de tarjeta obliga a cumplir PCI-DSS y no es algo que deba hacer la tienda.
/// </summary>
public sealed record PlanPago(
    MedioPago Medio,
    int Cuotas,
    decimal Coeficiente,
    string Etiqueta,
    string Detalle)
{
    /// <summary>Clave estable para los formularios y para guardar en el pedido.</summary>
    public string Clave => $"{Medio}:{Cuotas}";

    public bool TieneRecargo => Coeficiente > 1m;
    public bool TieneDescuento => Coeficiente < 1m;

    /// <summary>Diferencia porcentual respecto del precio de lista, con signo.</summary>
    public decimal AjustePorcentual => Math.Round((Coeficiente - 1m) * 100m, 2);

    /// <summary>Total a pagar con este plan, a partir del importe de lista.</summary>
    public decimal TotalSobre(decimal importe)
        => Math.Round(importe * Coeficiente, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Valor de cada cuota. La última absorbe el redondeo para que la suma de
    /// las cuotas dé exactamente el total y no un centavo de más o de menos.
    /// </summary>
    public decimal ValorCuota(decimal importe)
    {
        var total = TotalSobre(importe);
        return Cuotas <= 1
            ? total
            : Math.Round(total / Cuotas, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Ajuste de la última cuota para que el plan cierre al centavo.</summary>
    public decimal UltimaCuota(decimal importe)
    {
        if (Cuotas <= 1)
        {
            return TotalSobre(importe);
        }

        var total = TotalSobre(importe);
        return Math.Round(total - ValorCuota(importe) * (Cuotas - 1), 2, MidpointRounding.AwayFromZero);
    }
}

/// <summary>
/// Catálogo de planes de pago disponibles. Es la tabla de coeficientes del
/// negocio, en un solo lugar.
/// </summary>
public static class PlanesPago
{
    /// <summary>Todos los planes ofrecidos, en el orden en que se muestran.</summary>
    public static readonly IReadOnlyList<PlanPago> Todos = new List<PlanPago>
    {
        new(MedioPago.Transferencia, 1, 0.90m,
            "Transferencia bancaria",
            "10 % de descuento. Te enviamos el CBU al confirmar el pedido."),

        new(MedioPago.Efectivo, 1, 0.90m,
            "Efectivo al retirar",
            "10 % de descuento. Se abona en el local al retirar la compra."),

        new(MedioPago.TarjetaDebito, 1, 1.00m,
            "Tarjeta de débito",
            "Precio de lista, en un solo pago."),

        new(MedioPago.TarjetaCredito, 1, 1.00m,
            "Tarjeta de crédito · 1 pago",
            "Precio de lista, sin interés."),

        new(MedioPago.TarjetaCredito, 3, 1.00m,
            "Tarjeta de crédito · 3 cuotas",
            "3 cuotas sin interés."),

        new(MedioPago.TarjetaCredito, 6, 1.15m,
            "Tarjeta de crédito · 6 cuotas",
            "6 cuotas con 15 % de recargo por costo financiero."),

        new(MedioPago.TarjetaCredito, 12, 1.35m,
            "Tarjeta de crédito · 12 cuotas",
            "12 cuotas con 35 % de recargo por costo financiero."),
    };

    /// <summary>Plan por defecto si el cliente no elige nada.</summary>
    public static PlanPago PorDefecto => Todos.First(p => p.Medio == MedioPago.Transferencia);

    /// <summary>Busca por la clave que viaja en el formulario. Null si no existe.</summary>
    public static PlanPago? PorClave(string? clave)
    {
        if (string.IsNullOrWhiteSpace(clave))
        {
            return null;
        }
        return Todos.FirstOrDefault(p =>
            string.Equals(p.Clave, clave.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Busca por medio y cuotas, como se guardó en el pedido.</summary>
    public static PlanPago? Buscar(MedioPago medio, int cuotas)
        => Todos.FirstOrDefault(p => p.Medio == medio && p.Cuotas == cuotas);

    /// <summary>Nombre legible del medio, para los listados del panel.</summary>
    public static string NombreMedio(MedioPago medio) => medio switch
    {
        MedioPago.Transferencia => "Transferencia",
        MedioPago.Efectivo => "Efectivo",
        MedioPago.TarjetaDebito => "Débito",
        MedioPago.TarjetaCredito => "Crédito",
        _ => medio.ToString(),
    };

    /// <summary>¿Este medio requiere elegir una marca de tarjeta?</summary>
    public static bool EsConTarjeta(MedioPago medio)
        => medio is MedioPago.TarjetaCredito or MedioPago.TarjetaDebito;

    /// <summary>Marcas aceptadas. Sólo se guarda el nombre, nunca datos de la tarjeta.</summary>
    public static readonly IReadOnlyList<string> MarcasTarjeta = new[]
    {
        "Visa", "Mastercard", "American Express", "Cabal", "Naranja X",
    };
}
