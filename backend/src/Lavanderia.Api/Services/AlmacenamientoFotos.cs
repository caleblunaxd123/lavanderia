namespace Lavanderia.Api.Services;

/// <summary>
/// Guarda y recupera los archivos de las fotos de pedidos. Hoy escribe en disco local
/// (carpeta configurable, pensada para respaldarse a la nube con Google Drive para Escritorio).
/// Esta abstraccion permite cambiar a un proveedor de nube (S3/Cloudinary) sin tocar los
/// controllers: solo se registra otra implementacion en Program.cs.
/// </summary>
public interface IAlmacenamientoFotos
{
    /// <summary>Guarda los bytes y devuelve el nombre de archivo generado (con extension).</summary>
    Task<string> GuardarAsync(int negocioId, int pedidoId, byte[] datos, string extension, CancellationToken ct = default);
    /// <summary>Abre el archivo para servirlo, o null si ya no existe en disco.</summary>
    Stream? Abrir(int negocioId, int pedidoId, string nombreArchivo);
    void Eliminar(int negocioId, int pedidoId, string nombreArchivo);
}

public class AlmacenamientoFotosLocal : IAlmacenamientoFotos
{
    private readonly string _raiz;

    public AlmacenamientoFotosLocal(IConfiguration config, IWebHostEnvironment env)
    {
        // Carpeta configurable (Fotos:Directorio). En produccion se apunta a la carpeta que
        // Google Drive para Escritorio sincroniza. Si no se define, se usa App_Data local.
        var configurado = config["Fotos:Directorio"];
        _raiz = string.IsNullOrWhiteSpace(configurado)
            ? Path.Combine(env.ContentRootPath, "App_Data", "fotos-pedidos")
            : configurado;
        Directory.CreateDirectory(_raiz);
    }

    private string CarpetaPedido(int negocioId, int pedidoId)
        => Path.Combine(_raiz, negocioId.ToString(), pedidoId.ToString());

    public async Task<string> GuardarAsync(int negocioId, int pedidoId, byte[] datos, string extension, CancellationToken ct = default)
    {
        var carpeta = CarpetaPedido(negocioId, pedidoId);
        Directory.CreateDirectory(carpeta);
        var nombre = $"{Guid.NewGuid():N}{extension}";
        await File.WriteAllBytesAsync(Path.Combine(carpeta, nombre), datos, ct);
        return nombre;
    }

    public Stream? Abrir(int negocioId, int pedidoId, string nombreArchivo)
    {
        // Defensa contra path traversal: el nombre lo generamos nosotros (guid), pero validamos.
        if (nombreArchivo.Contains('/') || nombreArchivo.Contains('\\') || nombreArchivo.Contains(".."))
            return null;
        var ruta = Path.Combine(CarpetaPedido(negocioId, pedidoId), nombreArchivo);
        return File.Exists(ruta) ? File.OpenRead(ruta) : null;
    }

    public void Eliminar(int negocioId, int pedidoId, string nombreArchivo)
    {
        if (nombreArchivo.Contains('/') || nombreArchivo.Contains('\\') || nombreArchivo.Contains(".."))
            return;
        var ruta = Path.Combine(CarpetaPedido(negocioId, pedidoId), nombreArchivo);
        if (File.Exists(ruta)) File.Delete(ruta);
    }
}
