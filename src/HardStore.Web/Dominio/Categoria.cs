namespace HardStore.Web.Dominio;

/// <summary>
/// CAPA: Dominio · Categoría del catálogo.
/// El slug es lo que viaja en la URL y en los filtros.
/// </summary>
public class Categoria
{
    public int Id { get; private set; }
    public string Nombre { get; private set; } = "";
    public string Slug { get; private set; } = "";
    public string Descripcion { get; private set; } = "";

    private Categoria() { }

    /// <summary>
    /// Reconstruye la entidad tal como estaba guardada. La usa SÓLO la capa de
    /// datos al leer del archivo o de la base: es la contracara del constructor
    /// público, que valida datos nuevos. Acá los datos ya fueron validados
    /// cuando se crearon, así que se restauran tal cual — incluidas la fecha de
    /// creación y las banderas que no tienen setter público.
    /// </summary>
    internal static Categoria Rehidratar(int id, string nombre, string slug, string descripcion)
        => new() { Id = id, Nombre = nombre, Slug = slug, Descripcion = descripcion };

    public Categoria(string nombre, string? slug = null, string descripcion = "")
    {
        CambiarNombre(nombre);
        CambiarSlug(slug ?? GenerarSlug(nombre));
        Descripcion = (descripcion ?? "").Trim();
    }

    public void AsignarId(int id)
    {
        if (Id != 0)
        {
            throw new InvalidOperationException("La categoría ya tiene un id asignado.");
        }
        Id = id;
    }

    public void CambiarNombre(string nombre)
    {
        nombre = (nombre ?? "").Trim();

        if (nombre.Length < 2 || nombre.Length > 60)
        {
            throw new ArgumentException("El nombre de la categoría debe tener entre 2 y 60 caracteres.");
        }

        Nombre = nombre;
    }

    public void CambiarSlug(string slug)
    {
        slug = GenerarSlug(slug);

        if (slug.Length < 2)
        {
            throw new ArgumentException("El slug de la categoría es inválido.");
        }

        Slug = slug;
    }

    public void CambiarDescripcion(string? descripcion) => Descripcion = (descripcion ?? "").Trim();

    /// <summary>
    /// «Placas de Video» → «placas-de-video». Saca acentos y todo lo que no sea
    /// apto para una URL, para que el slug se pueda escribir a mano sin sorpresas.
    /// </summary>
    public static string GenerarSlug(string texto)
    {
        texto = (texto ?? "").Trim().ToLowerInvariant();

        var sinAcentos = texto
            .Replace('á', 'a').Replace('é', 'e').Replace('í', 'i')
            .Replace('ó', 'o').Replace('ú', 'u').Replace('ü', 'u')
            .Replace('ñ', 'n');

        var limpio = new System.Text.StringBuilder();
        foreach (var c in sinAcentos)
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                limpio.Append(c);
            }
            else if (c is ' ' or '-' or '_' or '.')
            {
                limpio.Append('-');
            }
        }

        // Colapsa guiones repetidos y limpia los extremos.
        var resultado = limpio.ToString();
        while (resultado.Contains("--"))
        {
            resultado = resultado.Replace("--", "-");
        }

        return resultado.Trim('-');
    }
}
