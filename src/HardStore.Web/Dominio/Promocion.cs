namespace HardStore.Web.Dominio;

public enum TipoPromocion
{
    /// <summary>Llevás N y pagás M. El 2x1 es N=2, M=1; el 3x2 es N=3, M=2.</summary>
    LlevaNPagaM = 0,

    /// <summary>Porcentaje de descuento sobre el precio de la línea.</summary>
    PorcentajeSobreLinea = 1,
}

public enum AlcancePromocion
{
    /// <summary>Aplica a un producto puntual.</summary>
    Producto = 0,

    /// <summary>Aplica a todos los productos de una categoría.</summary>
    Categoria = 1,
}

/// <summary>
/// CAPA: Dominio · Una promoción del tipo 2x1 / 3x2, o un porcentaje sobre la línea.
///
/// Por qué N y M en lugar de un enum con «2x1» y «3x2»: modelados así, un 4x3 o
/// un 6x4 salen gratis, sin tocar el código. La regla del negocio no es «existe
/// el 2x1», es «cada N unidades se pagan M».
///
/// La promoción NO decide sola si se aplica: el <c>CotizadorServicio</c> le
/// pregunta. Y las unidades bonificadas se calculan acá, porque es una cuenta
/// del dominio y tiene que dar el mismo número en la tienda, en el checkout y
/// en el pedido guardado.
/// </summary>
public class Promocion
{
    public int Id { get; private set; }

    public string Nombre { get; private set; } = "";
    public TipoPromocion Tipo { get; private set; }
    public AlcancePromocion Alcance { get; private set; }

    /// <summary>Id del producto o de la categoría, según el alcance.</summary>
    public int ObjetivoId { get; private set; }

    /// <summary>Unidades que se llevan (el «3» del 3x2). Sólo para LlevaNPagaM.</summary>
    public int LlevaN { get; private set; }

    /// <summary>Unidades que se pagan (el «2» del 3x2). Sólo para LlevaNPagaM.</summary>
    public int PagaM { get; private set; }

    /// <summary>Porcentaje de 0 a 100. Sólo para PorcentajeSobreLinea.</summary>
    public decimal Porcentaje { get; private set; }

    public DateTime Desde { get; private set; }
    public DateTime Hasta { get; private set; }

    public bool Activa { get; private set; } = true;

    private Promocion() { }

    /// <summary>
    /// Reconstruye la entidad tal como estaba guardada. La usa SÓLO la capa de
    /// datos al leer del archivo o de la base: es la contracara del constructor
    /// público, que valida datos nuevos. Acá los datos ya fueron validados
    /// cuando se crearon, así que se restauran tal cual — incluidas la fecha de
    /// creación y las banderas que no tienen setter público.
    /// </summary>
    internal static Promocion Rehidratar(
        int id, string nombre, TipoPromocion tipo, AlcancePromocion alcance, int objetivoId,
        int llevaN, int pagaM, decimal porcentaje, DateTime desde, DateTime hasta, bool activa)
        => new()
        {
            Id = id,
            Nombre = nombre,
            Tipo = tipo,
            Alcance = alcance,
            ObjetivoId = objetivoId,
            LlevaN = llevaN,
            PagaM = pagaM,
            Porcentaje = porcentaje,
            Desde = desde,
            Hasta = hasta,
            Activa = activa,
        };

    private Promocion(
        string nombre,
        TipoPromocion tipo,
        AlcancePromocion alcance,
        int objetivoId,
        DateTime desde,
        DateTime hasta)
    {
        CambiarNombre(nombre);

        if (objetivoId <= 0)
        {
            throw new ArgumentException("La promoción necesita un producto o una categoría.");
        }
        if (hasta.Date < desde.Date)
        {
            throw new ArgumentException("La fecha de fin no puede ser anterior a la de inicio.");
        }

        Tipo = tipo;
        Alcance = alcance;
        ObjetivoId = objetivoId;
        Desde = desde.Date;
        Hasta = hasta.Date;
    }

    /// <summary>Crea un «lleva N, paga M»: 2x1, 3x2, 4x3…</summary>
    public static Promocion LlevaNPaga(
        string nombre,
        AlcancePromocion alcance,
        int objetivoId,
        int llevaN,
        int pagaM,
        DateTime desde,
        DateTime hasta)
    {
        if (llevaN < 2)
        {
            throw new ArgumentException("Una promoción NxM necesita llevar al menos 2 unidades.");
        }
        if (pagaM < 1 || pagaM >= llevaN)
        {
            throw new ArgumentException(
                "Las unidades que se pagan deben ser al menos 1 y menos que las que se llevan.");
        }

        var promo = new Promocion(nombre, TipoPromocion.LlevaNPagaM, alcance, objetivoId, desde, hasta)
        {
            LlevaN = llevaN,
            PagaM = pagaM,
        };

        return promo;
    }

    /// <summary>Crea un descuento porcentual sobre la línea.</summary>
    public static Promocion Porcentual(
        string nombre,
        AlcancePromocion alcance,
        int objetivoId,
        decimal porcentaje,
        DateTime desde,
        DateTime hasta)
    {
        if (porcentaje <= 0 || porcentaje > 100)
        {
            throw new ArgumentException("El porcentaje de la promoción debe estar entre 0 y 100.");
        }

        var promo = new Promocion(nombre, TipoPromocion.PorcentajeSobreLinea, alcance, objetivoId, desde, hasta)
        {
            Porcentaje = Math.Round(porcentaje, 2),
        };

        return promo;
    }

    public void AsignarId(int id)
    {
        if (Id != 0)
        {
            throw new InvalidOperationException("La promoción ya tiene un id asignado.");
        }
        Id = id;
    }

    public void CambiarNombre(string nombre)
    {
        nombre = (nombre ?? "").Trim();

        if (nombre.Length < 2 || nombre.Length > 80)
        {
            throw new ArgumentException("El nombre de la promoción debe tener entre 2 y 80 caracteres.");
        }

        Nombre = nombre;
    }

    public void CambiarVigencia(DateTime desde, DateTime hasta)
    {
        if (hasta.Date < desde.Date)
        {
            throw new ArgumentException("La fecha de fin no puede ser anterior a la de inicio.");
        }
        Desde = desde.Date;
        Hasta = hasta.Date;
    }

    public void Activar() => Activa = true;
    public void Desactivar() => Activa = false;

    /* ---------------------------- Aplicación ---------------------------- */

    /// <summary>¿Está vigente en la fecha indicada?</summary>
    public bool EstaVigente(DateTime? cuando = null)
    {
        var dia = (cuando ?? DateTime.Now).Date;
        return Activa && dia >= Desde && dia <= Hasta;
    }

    /// <summary>¿Le corresponde a este producto?</summary>
    public bool AplicaA(Producto producto)
    {
        return Alcance switch
        {
            AlcancePromocion.Producto => producto.Id == ObjetivoId,
            AlcancePromocion.Categoria => producto.CategoriaId == ObjetivoId,
            _ => false,
        };
    }

    /// <summary>
    /// Unidades que el cliente NO paga.
    ///
    /// Con un 3x2 y 7 unidades: entran 2 grupos completos de 3 (se bonifican 2)
    /// y sobra 1 que se paga entera. La cuenta es «cantidad / N» grupos por
    /// «N − M» unidades gratis cada uno.
    /// </summary>
    public int UnidadesBonificadas(int cantidad)
    {
        if (Tipo != TipoPromocion.LlevaNPagaM || cantidad <= 0)
        {
            return 0;
        }

        var grupos = cantidad / LlevaN;
        return grupos * (LlevaN - PagaM);
    }

    /// <summary>Descuento en pesos que produce esta promoción sobre una línea.</summary>
    public decimal DescuentoSobre(int cantidad, decimal precioUnitario)
    {
        if (cantidad <= 0 || precioUnitario <= 0)
        {
            return 0m;
        }

        var bruto = cantidad * precioUnitario;

        var descuento = Tipo switch
        {
            TipoPromocion.LlevaNPagaM => UnidadesBonificadas(cantidad) * precioUnitario,
            TipoPromocion.PorcentajeSobreLinea => bruto * Porcentaje / 100m,
            _ => 0m,
        };

        // Nunca puede regalar más que el valor de la línea.
        return Math.Round(Math.Min(descuento, bruto), 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Etiqueta corta para la vidriera: «3x2», «15 % OFF».</summary>
    public string Etiqueta => Tipo switch
    {
        TipoPromocion.LlevaNPagaM => $"{LlevaN}x{PagaM}",
        TipoPromocion.PorcentajeSobreLinea => $"{Porcentaje:0.##} % OFF",
        _ => Nombre,
    };
}
