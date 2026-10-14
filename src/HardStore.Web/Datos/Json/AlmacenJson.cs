using System.Text.Json;
using System.Text.Json.Serialization;
using HardStore.Web.Datos.Memoria;
using HardStore.Web.Dominio;

namespace HardStore.Web.Datos.Json;

/// <summary>
/// CAPA: Datos · Almacén en un archivo JSON, sin dependencias externas.
///
/// Usa sólo System.Text.Json, que viene con .NET: el proyecto arranca con
/// «dotnet run» sin instalar nada ni configurar una base de datos.
///
/// Cómo funciona: todo se carga en memoria al iniciar y se vuelca al archivo
/// cada vez que un servicio llama a Confirmar(). El acceso está sincronizado
/// con un lock porque un servidor web atiende varios pedidos a la vez, y dos
/// checkouts simultáneos sobre el mismo producto tienen que serializarse.
///
/// Es apto para un trabajo práctico o una demo. Para producción se cambia por
/// la implementación de EF Core, que tiene transacciones reales.
/// </summary>
public sealed class AlmacenJson : IAlmacen
{
    private readonly string _ruta;
    private readonly object _candado = new();
    private readonly DatosEnMemoria _datos;

    private static readonly JsonSerializerOptions Opciones = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        IncludeFields = false,
        Converters = { new JsonStringEnumConverter() },
        // Los campos del dominio tienen setter privado: hay que decirle al
        // serializador que use el constructor sin argumentos y los campos
        // privados vía los atributos del propio tipo. Para simplificar, el
        // volcado usa DTO planos (ver RegistrosJson.cs).
    };

    public AlmacenJson(string rutaArchivo)
    {
        _ruta = rutaArchivo;
        _datos = Cargar();

        Productos = new ProductoRepositorioEnMemoria(_datos, _candado);
        Categorias = new CategoriaRepositorioEnMemoria(_datos, _candado);
        Usuarios = new UsuarioRepositorioEnMemoria(_datos, _candado);
        Pedidos = new PedidoRepositorioEnMemoria(_datos, _candado);
        Promociones = new PromocionRepositorioEnMemoria(_datos, _candado);
        Cupones = new CuponRepositorioEnMemoria(_datos, _candado);
    }

    public IProductoRepositorio Productos { get; }
    public ICategoriaRepositorio Categorias { get; }
    public IUsuarioRepositorio Usuarios { get; }
    public IPedidoRepositorio Pedidos { get; }
    public IPromocionRepositorio Promociones { get; }
    public ICuponRepositorio Cupones { get; }

    /// <summary>¿El almacén está recién creado y sin datos?</summary>
    public bool EstaVacio
    {
        get
        {
            lock (_candado)
            {
                return _datos.Productos.Count == 0 && _datos.Usuarios.Count == 0;
            }
        }
    }

    public void Confirmar()
    {
        lock (_candado)
        {
            Volcar();
        }
    }

    /* =================================================================
       PERSISTENCIA
       ================================================================= */

    private DatosEnMemoria Cargar()
    {
        if (!File.Exists(_ruta))
        {
            return new DatosEnMemoria();
        }

        try
        {
            var json = File.ReadAllText(_ruta);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new DatosEnMemoria();
            }

            var registro = JsonSerializer.Deserialize<RegistroArchivo>(json, Opciones);
            return registro is null ? new DatosEnMemoria() : registro.AMemoria();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Archivo corrupto o ilegible: se arranca vacío y se conserva el
            // original con otro nombre, en lugar de perderlo en silencio.
            try
            {
                var respaldo = _ruta + ".corrupto." + DateTime.Now.ToString("yyyyMMddHHmmss");
                File.Move(_ruta, respaldo, overwrite: true);
            }
            catch (IOException)
            {
                // Si tampoco se puede mover, igual seguimos con datos vacíos.
            }

            return new DatosEnMemoria();
        }
    }

    /// <summary>
    /// Escribe primero en un archivo temporal y después lo reemplaza. Si el
    /// proceso muere en el medio, el archivo bueno sigue intacto.
    /// </summary>
    private void Volcar()
    {
        var directorio = Path.GetDirectoryName(Path.GetFullPath(_ruta));
        if (!string.IsNullOrEmpty(directorio))
        {
            Directory.CreateDirectory(directorio);
        }

        var json = JsonSerializer.Serialize(RegistroArchivo.Desde(_datos), Opciones);
        var temporal = _ruta + ".tmp";

        File.WriteAllText(temporal, json);
        File.Move(temporal, _ruta, overwrite: true);
    }
}
