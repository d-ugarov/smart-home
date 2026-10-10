namespace SmartHome.Tests.Infrastructure;

/// <summary> A fresh directory for files of a test (databases, backups). </summary>
public static class TempDirectory
{
    public static string Create()
    {
        var path = Path.Combine(Path.GetTempPath(), "smarthome-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static void Delete(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // A file still open by a server that has not quite stopped: the temp directory of the OS gets it later.
        }
    }
}
