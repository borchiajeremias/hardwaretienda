namespace HardStore.Web.Dominio;

public enum TipoCupon
{
    /// <summary>Porcentaje sobre el subtotal ya promocionado.</summary>
    Porcentaje = 0,

    /// <summary>Monto fijo en pesos.</summary>
    MontoFijo = 1,
}

/// <summary>
/// CAPA: Dominio · Código de descuento.
///
/// El cupón se aplica DESPUÉS de las promociones de línea: primero se resuelve
/// el 2x1 o el 3x2, y el cupón trabaja sobre lo que quedó. Si fuera al revés,
/// un 20 % sobre el precio de lista y después el 3x2 daría un número distinto,
/// y el cliente terminaría discutiendo el total con razón.
///
/// El control de usos vive en la entidad, no en la pantalla: es la única forma
/// de que un cupón de 50 usos no se use 51 veces.
/// </summary>
public class Cupon
{
    public int Id { get; private set; }

    public string Codigo { get; private set; } = "";
    public string Descripcion { get; private set; } = "";

    public TipoCupon Tipo { get; private set; }

    /// <summary>Porcentaje (0-100) o monto fijo, según el tipo.</summary>
    public decimal Valor { get; private set; }

    /// <summary>Compra mínima para que el cupón sea válido. 0 = sin mínimo.</summary>
    public decimal MinimoCompra { get; private set; }

    /// <summary>Tope del descuento en pesos para los cupones porcentuales. 0 = sin tope.</summary>
    public decimal TopeDescuento { get; private set; }

    public DateTime Desde { get; private set; }
    public DateTime Hasta { get; private set; }

    /// <summary>Usos totales permitidos. 0 = ilimitado.</summary>
    public int UsosMaximos { get; private set; }

    public int UsosRealizados { get; private set; }

    public bool Activo { get; private set; } = true;

    private Cupon() { }

    /// <summary>
    /// Reconstruye la entidad tal como estaba guardada. La usa SÓLO la capa de
    /// datos al leer del archivo o de la base: es la contracara del constructor
    /// público, que valida datos nuevos. Acá los datos ya fueron validados
    /// cuando se crearon, así que se restauran tal cual — incluidas la fecha de
    /// creación y las banderas que no tienen setter público.
    /// </summary>
    internal static Cupon Rehidratar(
        int id, string codigo, string descripcion, TipoCupon tipo, decimal valor,
        decimal minimoCompra, decimal topeDescuento, DateTime desde, DateTime hasta,
        int usosMaximos, int usosRealizados, bool activo)
        => new()
        {
            Id = id,
            Codigo = codigo,
            Descripcion = descripcion,
            Tipo = tipo,
            Valor = valor,
            MinimoCompra = minimoCompra,
            TopeDescuento = topeDescuento,
            Desde = desde,
            Hasta = hasta,
            UsosMaximos = usosMaximos,
            UsosRealizados = usosRealizados,
            Activo = activo,
        };

    public Cupon(
        string codigo,
        TipoCupon tipo,
        decimal valor,
        DateTime desde,
        DateTime hasta,
        decimal minimoCompra = 0m,
        decimal topeDescuento = 0m,
        int usosMaximos = 0,
        string descripcion = "")
    {
        CambiarCodigo(codigo);

        if (hasta.Date < desde.Date)
        {
            throw new ArgumentException("La fecha de fin no puede ser anterior a la de inicio.");
        }
        if (valor <= 0)
        {
            throw new ArgumentException("El valor del cupón debe ser mayor a cero.");
        }
        if (tipo == TipoCupon.Porcentaje && valor > 100)
        {
            throw new ArgumentException("Un cupón porcentual no puede superar el 100 %.");
        }
        if (minimoCompra < 0 || topeDescuento < 0 || usosMaximos < 0)
        {
            throw new ArgumentException("Los límites del cupón no pueden ser negativos.");
        }

        Tipo = tipo;
        Valor = Math.Round(valor, 2);
        Desde = desde.Date;
        Hasta = hasta.Date;
        MinimoCompra = Math.Round(minimoCompra, 2);
        TopeDescuento = Math.Round(topeDescuento, 2);
        UsosMaximos = usosMaximos;
        Descripcion = (descripcion ?? "").Trim();
    }

    public void AsignarId(int id)
    {
        if (Id != 0)
        {
            throw new InvalidOperationException("El cupón ya tiene un id asignado.");
        }
        Id = id;
    }

    public void CambiarCodigo(string codigo)
    {
        codigo = (codigo ?? "").Trim().ToUpperInvariant();

        if (codigo.Length < 3 || codigo.Length > 25)
        {
            throw new ArgumentException("El código debe tener entre 3 y 25 caracteres.");
        }
        if (!codigo.All(char.IsAsciiLetterOrDigit))
        {
            throw new ArgumentException("El código admite sólo letras y números, sin espacios.");
        }

        Codigo = codigo;
    }

    public void CambiarDescripcion(string? descripcion) => Descripcion = (descripcion ?? "").Trim();

    public void CambiarVigencia(DateTime desde, DateTime hasta)
    {
        if (hasta.Date < desde.Date)
        {
            throw new ArgumentException("La fecha de fin no puede ser anterior a la de inicio.");
        }
        Desde = desde.Date;
        Hasta = hasta.Date;
    }

    public void Activar() => Activo = true;
    public void Desactivar() => Activo = false;

    /* ---------------------------- Validación ---------------------------- */

    public bool EstaVencido(DateTime? cuando = null)
    {
        var dia = (cuando ?? DateTime.Now).Date;
        return dia < Desde || dia > Hasta;
    }

    public bool AgotoUsos => UsosMaximos > 0 && UsosRealizados >= UsosMaximos;

    /// <summary>
    /// Comprueba si el cupón se puede usar para un subtotal dado.
    /// Devuelve null si es válido, o el motivo del rechazo escrito para el cliente.
    /// </summary>
    public string? MotivoDeRechazo(decimal subtotal, DateTime? cuando = null)
    {
        if (!Activo)
        {
            return $"El cupón {Codigo} no está disponible.";
        }
        if (EstaVencido(cuando))
        {
            return $"El cupón {Codigo} está fuera de su período de validez.";
        }
        if (AgotoUsos)
        {
            return $"El cupón {Codigo} agotó la cantidad de usos disponibles.";
        }
        if (MinimoCompra > 0 && subtotal < MinimoCompra)
        {
            return $"El cupón {Codigo} requiere una compra mínima de {MinimoCompra:N2}.";
        }

        return null;
    }

    public bool EsAplicable(decimal subtotal, DateTime? cuando = null)
        => MotivoDeRechazo(subtotal, cuando) is null;

    /// <summary>Descuento en pesos sobre el subtotal, respetando el tope.</summary>
    public decimal DescuentoSobre(decimal subtotal)
    {
        if (subtotal <= 0)
        {
            return 0m;
        }

        var descuento = Tipo switch
        {
            TipoCupon.Porcentaje => subtotal * Valor / 100m,
            TipoCupon.MontoFijo => Valor,
            _ => 0m,
        };

        if (TopeDescuento > 0 && descuento > TopeDescuento)
        {
            descuento = TopeDescuento;
        }

        // Nunca descuenta más que el propio subtotal: el total no puede ser negativo.
        return Math.Round(Math.Min(descuento, subtotal), 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Registra un uso. Lo llama el servicio de pedidos al confirmar la compra.</summary>
    public void RegistrarUso()
    {
        if (AgotoUsos)
        {
            throw new InvalidOperationException($"El cupón {Codigo} ya agotó sus usos.");
        }
        UsosRealizados++;
    }

    public string Etiqueta => Tipo == TipoCupon.Porcentaje
        ? $"{Valor:0.##} % de descuento"
        : $"{Valor:N2} de descuento";
}
