namespace HVTradingBot.App.Core.IO;

/// <summary>The few file operations the app needs, behind an interface so tests do not touch the disk.</summary>
public interface IFileSystem
{
    bool FileExists(string path);

    string ReadAllText(string path);

    /// <summary>Creates a new file readable and writable only by the current user (mode 600); fails if it exists.</summary>
    void CreatePrivateFile(string path, string contents);
}

public sealed class PhysicalFileSystem : IFileSystem
{
    public bool FileExists(string path) => File.Exists(path);

    public string ReadAllText(string path) => File.ReadAllText(path);

    public void CreatePrivateFile(string path, string contents)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        using var writer = new StreamWriter(path, options);
        writer.Write(contents);
    }
}
