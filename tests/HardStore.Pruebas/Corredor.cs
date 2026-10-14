namespace HardStore.Pruebas;

/// <summary>
/// Corredor de pruebas mínimo, sin dependencias externas.
///
/// Cada prueba es un nombre y una acción. Si la acción no lanza nada, pasa.
/// Los helpers de aserción lanzan <see cref="AserciónFallida"/> con un mensaje
/// que dice qué se esperaba y qué llegó, que es lo único que uno quiere leer
/// cuando algo se rompe.
/// </summary>
public sealed class AserciónFallida : Exception
{
    public AserciónFallida(string mensaje) : base(mensaje) { }
}

public static class Afirmar
{
    public static void Cierto(bool condicion, string mensaje)
    {
        if (!condicion)
        {
            throw new AserciónFallida(mensaje);
        }
    }

    public static void Falso(bool condicion, string mensaje) => Cierto(!condicion, mensaje);

    public static void Igual<T>(T esperado, T obtenido, string mensaje)
    {
        if (!EqualityComparer<T>.Default.Equals(esperado, obtenido))
        {
            throw new AserciónFallida($"{mensaje} — esperado <{esperado}>, obtenido <{obtenido}>");
        }
    }

    public static void Contiene(string aguja, string pajar, string mensaje)
    {
        if (!pajar.Contains(aguja, StringComparison.OrdinalIgnoreCase))
        {
            throw new AserciónFallida($"{mensaje} — no se encontró «{aguja}» en «{Recortar(pajar)}»");
        }
    }

    /// <summary>Verifica que la acción lance la excepción esperada.</summary>
    public static void Lanza<TExcepcion>(Action accion, string mensaje)
        where TExcepcion : Exception
    {
        try
        {
            accion();
        }
        catch (TExcepcion)
        {
            return;
        }
        catch (Exception otra)
        {
            throw new AserciónFallida(
                $"{mensaje} — se esperaba {typeof(TExcepcion).Name} pero salió {otra.GetType().Name}: {otra.Message}");
        }

        throw new AserciónFallida($"{mensaje} — no lanzó {typeof(TExcepcion).Name}");
    }

    private static string Recortar(string s) => s.Length <= 120 ? s : s[..120] + "…";
}

public sealed class Corredor
{
    private readonly List<(string Grupo, string Nombre, Action Accion)> _pruebas = new();
    private string _grupo = "General";

    public void Grupo(string nombre) => _grupo = nombre;

    public void Prueba(string nombre, Action accion) => _pruebas.Add((_grupo, nombre, accion));

    /// <summary>Corre todo y devuelve el código de salida (0 = todo verde).</summary>
    public int Correr()
    {
        var fallos = new List<string>();
        var grupoActual = "";
        var pasadas = 0;

        foreach (var (grupo, nombre, accion) in _pruebas)
        {
            if (grupo != grupoActual)
            {
                grupoActual = grupo;
                Console.WriteLine();
                Console.WriteLine($"── {grupo} " + new string('─', Math.Max(0, 60 - grupo.Length)));
            }

            try
            {
                accion();
                pasadas++;
                Console.WriteLine($"  ok   {nombre}");
            }
            catch (AserciónFallida ex)
            {
                fallos.Add($"{grupo} · {nombre}: {ex.Message}");
                Console.WriteLine($"  FALLA {nombre}");
                Console.WriteLine($"         {ex.Message}");
            }
            catch (Exception ex)
            {
                fallos.Add($"{grupo} · {nombre}: {ex.GetType().Name}: {ex.Message}");
                Console.WriteLine($"  ERROR {nombre}");
                Console.WriteLine($"         {ex.GetType().Name}: {ex.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine(new string('═', 62));

        if (fallos.Count == 0)
        {
            Console.WriteLine($"  {pasadas}/{_pruebas.Count} pruebas OK");
            return 0;
        }

        Console.WriteLine($"  {pasadas}/{_pruebas.Count} OK · {fallos.Count} con problemas:");
        foreach (var f in fallos)
        {
            Console.WriteLine($"   · {f}");
        }

        return 1;
    }
}
