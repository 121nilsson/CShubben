namespace Cshubben.Core.Notes;

/// <summary>Where Cshubben keeps its data on disk.</summary>
public static class AppPaths
{
    /// <summary>%APPDATA%/Cshubben on Windows.</summary>
    public static string DataDirectory
    {
        get
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrEmpty(root)) root = Path.GetTempPath();
            return Path.Combine(root, "Cshubben");
        }
    }

    public static string NotesDirectory => NotesIn(DataDirectory);

    public static string NotesIn(string dataDirectory) => Path.Combine(dataDirectory, "notes");
}
