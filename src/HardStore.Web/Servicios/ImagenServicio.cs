namespace HardStore.Web.Servicios;

/// <summary>
/// CAPA: Servicios · Guarda la imagen que el administrador sube por el
/// formulario del producto.
///
/// Esto es lo que reemplaza al «UPDATE productos SET imagen = ...» que había
/// que escribir a mano en la versión PHP: el administrador elige el archivo, y
/// el sistema lo guarda, le pone un nombre y actualiza el producto solo.
///
/// VALIDACIONES, y por qué cada una:
///
///  · Tamaño máximo — un archivo de 200 MB llenaría el disco del servidor.
///  · Extensión permitida — evita subir .aspx, .php o .exe a una carpeta pública.
///  · NÚMEROS MÁGICOS del contenido — es la validación que importa de verdad.
///    Cualquiera puede renombrar «virus.exe» a «foto.jpg»; lo que no puede
///    falsificar tan fácil son los primeros bytes del archivo. Se comprueba que
///    el contenido sea realmente JPEG, PNG, GIF o WebP.
///  · Nombre generado por el sistema — el nombre que manda el navegador nunca
///    se usa: podría venir con «../../» y escribir fuera de la carpeta
///    (path traversal). Se genera un GUID y se le pega la extensión correcta.
///
/// Lo que NO hace: reprocesar la imagen para quitarle metadatos o payloads
/// embebidos. Eso requiere una biblioteca de imágenes (ImageSharp o SkiaSharp)
/// y está anotado como mejora en el README.
/// </summary>
public sealed class ImagenServicio
{
    /// <summary>Carpeta pública donde se guardan, relativa a wwwroot.</summary>
    public const string CarpetaRelativa = "img/productos";

    /// <summary>4 MB alcanzan de sobra para una foto de producto.</summary>
    public const long TamanoMaximo = 4 * 1024 * 1024;

    private static readonly string[] ExtensionesPermitidas = { ".jpg", ".jpeg", ".png", ".webp", ".gif" };

    private readonly string _rutaWwwRoot;
    private readonly ILogger<ImagenServicio> _log;

    public ImagenServicio(IWebHostEnvironment entorno, ILogger<ImagenServicio> log)
    {
        _rutaWwwRoot = entorno.WebRootPath
            ?? Path.Combine(entorno.ContentRootPath, "wwwroot");
        _log = log;
    }

    /// <summary>
    /// Guarda el archivo subido y devuelve la ruta relativa para el producto
    /// (por ejemplo «img/productos/a1b2c3.jpg»).
    /// </summary>
    /// <exception cref="ReglaNegocioException">Si el archivo no pasa las validaciones.</exception>
    public async Task<string> GuardarAsync(IFormFile archivo, CancellationToken ct = default)
    {
        if (archivo is null || archivo.Length == 0)
        {
            throw new ReglaNegocioException("No se recibió ningún archivo.");
        }
        if (archivo.Length > TamanoMaximo)
        {
            throw new ReglaNegocioException(
                $"La imagen pesa {archivo.Length / 1024 / 1024.0:0.#} MB y el máximo es " +
                $"{TamanoMaximo / 1024 / 1024} MB.");
        }

        var extensionDeclarada = Path.GetExtension(archivo.FileName ?? "").ToLowerInvariant();

        if (!ExtensionesPermitidas.Contains(extensionDeclarada))
        {
            throw new ReglaNegocioException(
                "Formato no permitido. Se aceptan JPG, PNG, WebP y GIF.");
        }

        // Se lee el contenido a memoria (son 4 MB como máximo) para poder
        // inspeccionar los primeros bytes antes de escribir nada en disco.
        using var memoria = new MemoryStream();
        await archivo.CopyToAsync(memoria, ct);
        var bytes = memoria.ToArray();

        var extensionReal = DetectarFormato(bytes)
            ?? throw new ReglaNegocioException(
                "El archivo no parece ser una imagen válida, más allá de su extensión.");

        // El nombre lo elige el sistema, nunca el cliente.
        var nombre = $"{Guid.NewGuid():N}{extensionReal}";

        var carpetaAbsoluta = Path.Combine(_rutaWwwRoot, CarpetaRelativa.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(carpetaAbsoluta);

        var destino = Path.Combine(carpetaAbsoluta, nombre);
        await File.WriteAllBytesAsync(destino, bytes, ct);

        _log.LogInformation("Imagen guardada: {Nombre} ({Bytes} bytes)", nombre, bytes.Length);

        return $"{CarpetaRelativa}/{nombre}";
    }

    /// <summary>
    /// Borra una imagen subida antes. Se usa al reemplazar la foto de un
    /// producto, para no dejar archivos huérfanos acumulándose.
    ///
    /// Sólo borra dentro de la carpeta de productos: si la ruta apunta a
    /// cualquier otro lugar, no hace nada. Es la defensa contra que un valor
    /// manipulado en la base termine borrando archivos del sitio.
    /// </summary>
    public void Borrar(string? rutaRelativa)
    {
        if (string.IsNullOrWhiteSpace(rutaRelativa))
        {
            return;
        }

        var limpia = rutaRelativa.Replace('\\', '/').TrimStart('/');

        if (!limpia.StartsWith(CarpetaRelativa + "/", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var nombre = Path.GetFileName(limpia);
        if (string.IsNullOrWhiteSpace(nombre))
        {
            return;
        }

        var carpetaAbsoluta = Path.GetFullPath(
            Path.Combine(_rutaWwwRoot, CarpetaRelativa.Replace('/', Path.DirectorySeparatorChar)));
        var destino = Path.GetFullPath(Path.Combine(carpetaAbsoluta, nombre));

        // Confirmación final de que el archivo está realmente dentro de la
        // carpeta esperada, ya resueltos los «..».
        if (!destino.StartsWith(carpetaAbsoluta, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            if (File.Exists(destino))
            {
                File.Delete(destino);
                _log.LogInformation("Imagen borrada: {Nombre}", nombre);
            }
        }
        catch (IOException ex)
        {
            // Que no se pueda borrar un archivo viejo no debe frenar la
            // edición del producto: se registra y se sigue.
            _log.LogWarning(ex, "No se pudo borrar la imagen {Nombre}", nombre);
        }
    }

    /// <summary>
    /// Detecta el formato real mirando los primeros bytes (números mágicos).
    /// Devuelve la extensión correspondiente, o null si no es una imagen conocida.
    /// </summary>
    private static string? DetectarFormato(byte[] bytes)
    {
        if (bytes.Length < 12)
        {
            return null;
        }

        // JPEG: FF D8 FF
        if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return ".jpg";
        }

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
            && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
        {
            return ".png";
        }

        // GIF: "GIF87a" o "GIF89a"
        if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x38)
        {
            return ".gif";
        }

        // WebP: "RIFF" .... "WEBP"
        if (bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46
            && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
        {
            return ".webp";
        }

        return null;
    }
}
