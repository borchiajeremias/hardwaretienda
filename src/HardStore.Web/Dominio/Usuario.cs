namespace HardStore.Web.Dominio;

public enum RolUsuario
{
    Cliente = 0,
    Admin = 1,
}

/// <summary>
/// CAPA: Dominio · Usuario de la tienda.
///
/// La contraseña NUNCA se guarda en texto plano ni se puede leer desde afuera:
/// la entidad sólo expone el hash y la sal, y sólo <see cref="UsuarioServicio"/>
/// sabe cómo derivarlos y verificarlos.
///
/// El rol vive acá y no en una tabla aparte porque en esta tienda hay
/// exactamente dos: quien compra y quien administra.
/// </summary>
public class Usuario
{
    public int Id { get; private set; }

    public string Nombre { get; private set; } = "";
    public string Email { get; private set; } = "";

    public string PasswordHash { get; private set; } = "";
    public string PasswordSalt { get; private set; } = "";

    public string Telefono { get; private set; } = "";
    public string Direccion { get; private set; } = "";

    public RolUsuario Rol { get; private set; } = RolUsuario.Cliente;
    public bool Activo { get; private set; } = true;

    public DateTime CreadoEn { get; private set; } = DateTime.Now;

    private Usuario() { }

    /// <summary>
    /// Reconstruye la entidad tal como estaba guardada. La usa SÓLO la capa de
    /// datos al leer del archivo o de la base: es la contracara del constructor
    /// público, que valida datos nuevos. Acá los datos ya fueron validados
    /// cuando se crearon, así que se restauran tal cual — incluidas la fecha de
    /// creación y las banderas que no tienen setter público.
    /// </summary>
    internal static Usuario Rehidratar(
        int id, string nombre, string email, string passwordHash, string passwordSalt,
        string telefono, string direccion, RolUsuario rol, bool activo, DateTime creadoEn)
        => new()
        {
            Id = id,
            Nombre = nombre,
            Email = email,
            PasswordHash = passwordHash,
            PasswordSalt = passwordSalt,
            Telefono = telefono,
            Direccion = direccion,
            Rol = rol,
            Activo = activo,
            CreadoEn = creadoEn,
        };

    public Usuario(
        string nombre,
        string email,
        string passwordHash,
        string passwordSalt,
        RolUsuario rol = RolUsuario.Cliente,
        string telefono = "",
        string direccion = "")
    {
        CambiarNombre(nombre);
        CambiarEmail(email);

        if (string.IsNullOrWhiteSpace(passwordHash) || string.IsNullOrWhiteSpace(passwordSalt))
        {
            throw new ArgumentException("El usuario necesita hash y sal de contraseña.");
        }

        PasswordHash = passwordHash;
        PasswordSalt = passwordSalt;
        Rol = rol;
        Telefono = (telefono ?? "").Trim();
        Direccion = (direccion ?? "").Trim();
    }

    public void AsignarId(int id)
    {
        if (Id != 0)
        {
            throw new InvalidOperationException("El usuario ya tiene un id asignado.");
        }
        Id = id;
    }

    public bool EsAdmin => Rol == RolUsuario.Admin;

    /// <summary>Primer nombre, para saludar en el encabezado.</summary>
    public string PrimerNombre => Nombre.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? Nombre;

    public void CambiarNombre(string nombre)
    {
        nombre = (nombre ?? "").Trim();

        if (nombre.Length < 2 || nombre.Length > 100)
        {
            throw new ArgumentException("El nombre debe tener entre 2 y 100 caracteres.");
        }

        Nombre = nombre;
    }

    public void CambiarEmail(string email)
    {
        email = (email ?? "").Trim().ToLowerInvariant();

        if (email.Length > 150 || !EsEmailValido(email))
        {
            throw new ArgumentException("El email no es válido.");
        }

        Email = email;
    }

    public void CambiarTelefono(string? telefono) => Telefono = (telefono ?? "").Trim();
    public void CambiarDireccion(string? direccion) => Direccion = (direccion ?? "").Trim();

    /// <summary>Reemplaza las credenciales. Sólo lo llama el servicio de usuarios.</summary>
    public void CambiarCredenciales(string hash, string salt)
    {
        if (string.IsNullOrWhiteSpace(hash) || string.IsNullOrWhiteSpace(salt))
        {
            throw new ArgumentException("Credenciales inválidas.");
        }
        PasswordHash = hash;
        PasswordSalt = salt;
    }

    public void AscenderAAdmin() => Rol = RolUsuario.Admin;
    public void DegradarACliente() => Rol = RolUsuario.Cliente;

    public void Activar() => Activo = true;
    public void Desactivar() => Activo = false;

    /// <summary>
    /// Validación de email sin expresiones regulares gigantes: alcanza con que
    /// tenga una arroba en el medio y un punto en el dominio.
    /// </summary>
    public static bool EsEmailValido(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        var partes = email.Split('@');
        if (partes.Length != 2)
        {
            return false;
        }

        var (local, dominio) = (partes[0], partes[1]);

        return local.Length > 0
            && dominio.Length >= 3
            && dominio.Contains('.')
            && !dominio.StartsWith('.')
            && !dominio.EndsWith('.')
            && !email.Contains(' ');
    }
}
