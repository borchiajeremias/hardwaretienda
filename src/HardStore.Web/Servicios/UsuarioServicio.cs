using System.Security.Cryptography;
using HardStore.Web.Datos;
using HardStore.Web.Dominio;

namespace HardStore.Web.Servicios;

/// <summary>
/// CAPA: Servicios · Registro y autenticación.
///
/// Las contraseñas se guardan con PBKDF2 (Rfc2898DeriveBytes), que viene en
/// .NET y no necesita ningún paquete: 210.000 iteraciones de SHA-256 con una
/// sal aleatoria de 16 bytes por usuario. La sal por usuario es lo que impide
/// que dos personas con la misma contraseña tengan el mismo hash, y las
/// iteraciones son lo que hace caro probar contraseñas por fuerza bruta.
///
/// La comparación usa CryptographicOperations.FixedTimeEquals: comparar con ==
/// filtra información por el tiempo que tarda en encontrar la primera
/// diferencia.
///
/// Al fallar el login se verifica igual contra un hash señuelo, para que el
/// tiempo de respuesta no revele si el email existe.
/// </summary>
public sealed class UsuarioServicio
{
    private const int TamanoSal = 16;
    private const int TamanoHash = 32;
    private const int Iteraciones = 210_000;

    private static readonly HashAlgorithmName Algoritmo = HashAlgorithmName.SHA256;

    /// <summary>Sal y hash señuelo para igualar tiempos cuando el email no existe.</summary>
    private static readonly string SalSenuelo = Convert.ToBase64String(new byte[TamanoSal]);
    private static readonly string HashSenuelo = Convert.ToBase64String(new byte[TamanoHash]);

    private readonly IAlmacen _almacen;

    public UsuarioServicio(IAlmacen almacen) => _almacen = almacen;

    /* ------------------------------ Registro ---------------------------- */

    public Usuario Registrar(
        string nombre,
        string email,
        string password,
        string telefono = "",
        string direccion = "",
        RolUsuario rol = RolUsuario.Cliente)
    {
        ValidarPassword(password);

        var normalizado = (email ?? "").Trim().ToLowerInvariant();

        if (_almacen.Usuarios.PorEmail(normalizado) is not null)
        {
            throw new ReglaNegocioException("Ese email ya está registrado.");
        }

        var (hash, sal) = Derivar(password);

        Usuario usuario;
        try
        {
            usuario = new Usuario(nombre, normalizado, hash, sal, rol, telefono, direccion);
        }
        catch (ArgumentException ex)
        {
            throw new ReglaNegocioException(ex.Message, ex);
        }

        _almacen.Usuarios.Guardar(usuario);
        _almacen.Confirmar();

        return usuario;
    }

    /* -------------------------------- Login ----------------------------- */

    /// <summary>
    /// Verifica las credenciales. Devuelve el usuario o null.
    /// El mensaje de error lo pone el controlador y es siempre genérico: no
    /// delatamos si falló el email o la contraseña.
    /// </summary>
    public Usuario? Autenticar(string email, string password)
    {
        var usuario = _almacen.Usuarios.PorEmail(email ?? "");

        if (usuario is null)
        {
            // Igualar tiempos: se hace el trabajo criptográfico igual.
            Verificar(password ?? "", HashSenuelo, SalSenuelo);
            return null;
        }

        if (!Verificar(password ?? "", usuario.PasswordHash, usuario.PasswordSalt))
        {
            return null;
        }

        return usuario.Activo ? usuario : null;
    }

    public void CambiarPassword(int usuarioId, string passwordActual, string passwordNueva)
    {
        var usuario = _almacen.Usuarios.PorId(usuarioId)
            ?? throw NoEncontradoException.Usuario(usuarioId);

        if (!Verificar(passwordActual ?? "", usuario.PasswordHash, usuario.PasswordSalt))
        {
            throw new ReglaNegocioException("La contraseña actual no es correcta.");
        }

        ValidarPassword(passwordNueva);

        var (hash, sal) = Derivar(passwordNueva);
        usuario.CambiarCredenciales(hash, sal);

        _almacen.Usuarios.Guardar(usuario);
        _almacen.Confirmar();
    }

    /// <summary>Reinicio de contraseña desde el panel, sin conocer la anterior.</summary>
    public void ReiniciarPassword(int usuarioId, string passwordNueva)
    {
        var usuario = _almacen.Usuarios.PorId(usuarioId)
            ?? throw NoEncontradoException.Usuario(usuarioId);

        ValidarPassword(passwordNueva);

        var (hash, sal) = Derivar(passwordNueva);
        usuario.CambiarCredenciales(hash, sal);

        _almacen.Usuarios.Guardar(usuario);
        _almacen.Confirmar();
    }

    /* ------------------------------- Perfil ----------------------------- */

    public Usuario ActualizarPerfil(int usuarioId, string nombre, string telefono, string direccion)
    {
        var usuario = _almacen.Usuarios.PorId(usuarioId)
            ?? throw NoEncontradoException.Usuario(usuarioId);

        try
        {
            usuario.CambiarNombre(nombre);
            usuario.CambiarTelefono(telefono);
            usuario.CambiarDireccion(direccion);
        }
        catch (ArgumentException ex)
        {
            throw new ReglaNegocioException(ex.Message, ex);
        }

        _almacen.Usuarios.Guardar(usuario);
        _almacen.Confirmar();

        return usuario;
    }

    /* --------------------------- Administración -------------------------- */

    public IReadOnlyList<Usuario> Todos() => _almacen.Usuarios.Todos();

    public Usuario Obtener(int id)
        => _almacen.Usuarios.PorId(id) ?? throw NoEncontradoException.Usuario(id);

    /// <summary>
    /// Cambia el rol de un usuario. No permite que el último administrador se
    /// quite el rol a sí mismo: la tienda quedaría sin nadie que la administre
    /// y habría que editar el archivo de datos a mano para recuperarla.
    /// </summary>
    public Usuario CambiarRol(int usuarioId, RolUsuario nuevoRol)
    {
        var usuario = Obtener(usuarioId);

        if (usuario.EsAdmin && nuevoRol != RolUsuario.Admin)
        {
            var admins = _almacen.Usuarios.Todos().Count(u => u.EsAdmin && u.Activo);
            if (admins <= 1)
            {
                throw new ReglaNegocioException(
                    "Es el único administrador activo: designá otro antes de quitarle el rol.");
            }
        }

        if (nuevoRol == RolUsuario.Admin)
        {
            usuario.AscenderAAdmin();
        }
        else
        {
            usuario.DegradarACliente();
        }

        _almacen.Usuarios.Guardar(usuario);
        _almacen.Confirmar();

        return usuario;
    }

    public bool HayAlgunAdmin() => _almacen.Usuarios.Todos().Any(u => u.EsAdmin && u.Activo);

    /* ----------------------------- Criptografía -------------------------- */

    private static void ValidarPassword(string? password)
    {
        password ??= "";

        if (password.Length < 8)
        {
            throw new ReglaNegocioException("La contraseña debe tener al menos 8 caracteres.");
        }
        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
        {
            throw new ReglaNegocioException("La contraseña debe combinar letras y números.");
        }
    }

    private static (string Hash, string Sal) Derivar(string password)
    {
        var sal = RandomNumberGenerator.GetBytes(TamanoSal);

        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password: password,
            salt: sal,
            iterations: Iteraciones,
            hashAlgorithm: Algoritmo,
            outputLength: TamanoHash);

        return (Convert.ToBase64String(hash), Convert.ToBase64String(sal));
    }

    private static bool Verificar(string password, string hashGuardado, string salGuardada)
    {
        byte[] sal, esperado;

        try
        {
            sal = Convert.FromBase64String(salGuardada);
            esperado = Convert.FromBase64String(hashGuardado);
        }
        catch (FormatException)
        {
            return false;
        }

        if (sal.Length == 0 || esperado.Length == 0)
        {
            return false;
        }

        var calculado = Rfc2898DeriveBytes.Pbkdf2(
            password: password,
            salt: sal,
            iterations: Iteraciones,
            hashAlgorithm: Algoritmo,
            outputLength: esperado.Length);

        // Comparación de tiempo fijo: con == se filtra información por cuánto
        // tarda en encontrar la primera diferencia.
        return CryptographicOperations.FixedTimeEquals(calculado, esperado);
    }
}
