namespace HardStore.Web.Dominio.Precios;

/// <summary>
/// CAPA: Dominio · Una línea ya cotizada: qué se lleva, cuánto se paga y por qué.
/// Es un objeto de sólo lectura que produce el cotizador y consumen las vistas.
/// </summary>
public sealed record LineaCotizada(
    int ProductoId,
    string Codigo,
    string Nombre,
    string? Imagen,
    decimal PrecioUnitario,
    int Cantidad,
    int StockDisponible,
    int UnidadesBonificadas,
    decimal DescuentoPromocion,
    string? PromocionNombre,
    string? PromocionEtiqueta)
{
    public decimal Bruto => Math.Round(PrecioUnitario * Cantidad, 2, MidpointRounding.AwayFromZero);

    public decimal Subtotal => Math.Round(Bruto - DescuentoPromocion, 2, MidpointRounding.AwayFromZero);

    public bool TienePromocion => DescuentoPromocion > 0;

    /// <summary>Unidades que efectivamente se pagan.</summary>
    public int UnidadesPagas => Cantidad - UnidadesBonificadas;
}

/// <summary>
/// CAPA: Dominio · El carrito cotizado completo, con todo el desglose resuelto.
///
/// Éste es el contrato que atraviesa el sistema: lo devuelve el cotizador, lo
/// muestran el carrito y el checkout, y de acá salen los importes que se
/// congelan en el pedido. Un solo cálculo, un solo número en todas las pantallas.
///
/// El orden de aplicación es deliberado y está documentado en el cotizador:
///   bruto → promociones de línea → cupón → coeficiente del medio de pago.
/// </summary>
public sealed class Cotizacion
{
    public IReadOnlyList<LineaCotizada> Lineas { get; init; } = Array.Empty<LineaCotizada>();

    /// <summary>Avisos que no impiden cotizar: stock ajustado, cupón rechazado.</summary>
    public IReadOnlyList<string> Avisos { get; init; } = Array.Empty<string>();

    public string? CuponCodigo { get; init; }
    public string? CuponDescripcion { get; init; }
    public decimal DescuentoCupon { get; init; }

    public PlanPago Plan { get; init; } = PlanesPago.PorDefecto;

    /// <summary>Suma de las líneas a precio de lista, sin ningún descuento.</summary>
    public decimal Bruto => Redondear(Lineas.Sum(l => l.Bruto));

    /// <summary>Total ahorrado por promociones 2x1, 3x2 y porcentajes de línea.</summary>
    public decimal DescuentoPromociones => Redondear(Lineas.Sum(l => l.DescuentoPromocion));

    /// <summary>Subtotal después de las promociones y antes del cupón.</summary>
    public decimal SubtotalConPromociones => Redondear(Bruto - DescuentoPromociones);

    /// <summary>Importe al que se le aplica el coeficiente del medio de pago.</summary>
    public decimal BaseFinanciacion => Redondear(SubtotalConPromociones - DescuentoCupon);

    /// <summary>Recargo (positivo) o descuento (negativo) del medio de pago elegido.</summary>
    public decimal AjustePago => Redondear(Plan.TotalSobre(BaseFinanciacion) - BaseFinanciacion);

    /// <summary>Lo que finalmente paga el cliente.</summary>
    public decimal Total => Plan.TotalSobre(BaseFinanciacion);

    /// <summary>Ahorro total respecto del precio de lista, sumando promos y cupón.</summary>
    public decimal AhorroTotal => Redondear(DescuentoPromociones + DescuentoCupon);

    /* --------------------------- Desglose de IVA ------------------------- */

    public decimal Neto => Redondear(Total / 1.21m);
    public decimal Iva => Redondear(Total - Neto);

    /* ------------------------------ Cuotas ------------------------------ */

    public int Cuotas => Plan.Cuotas;
    public decimal ValorCuota => Plan.ValorCuota(BaseFinanciacion);
    public decimal UltimaCuota => Plan.UltimaCuota(BaseFinanciacion);

    /// <summary>¿Las cuotas son todas iguales o la última difiere por redondeo?</summary>
    public bool CuotasParejas => Cuotas <= 1 || ValorCuota == UltimaCuota;

    /* ----------------------------- Consultas ---------------------------- */

    public bool EstaVacia => Lineas.Count == 0;

    public int CantidadDeArticulos => Lineas.Sum(l => l.Cantidad);

    public int UnidadesBonificadas => Lineas.Sum(l => l.UnidadesBonificadas);

    public bool TienePromociones => DescuentoPromociones > 0;

    public bool TieneCupon => DescuentoCupon > 0;

    /// <summary>
    /// Los mismos importes calculados para otro plan de pago, sin volver a
    /// recorrer promociones ni cupón. Sirve para mostrar la tabla comparativa
    /// «cuánto sale con cada medio» sin recotizar siete veces.
    /// </summary>
    public Cotizacion ConPlan(PlanPago plan) => new()
    {
        Lineas = Lineas,
        Avisos = Avisos,
        CuponCodigo = CuponCodigo,
        CuponDescripcion = CuponDescripcion,
        DescuentoCupon = DescuentoCupon,
        Plan = plan,
    };

    private static decimal Redondear(decimal valor)
        => Math.Round(valor, 2, MidpointRounding.AwayFromZero);
}
