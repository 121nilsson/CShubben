using System.Diagnostics;

namespace Cshubben.Services;

/// <summary>Opens files, folders and web links with the operating system.</summary>
static class Shell
{
    /// <summary>Opens a file or folder. Returns false (and never throws) when it could not be opened.</summary>
    public static bool OpenPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Opens an http or https link in the default browser.</summary>
    public static bool OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return false;
        return OpenPath(uri.AbsoluteUri);
    }

    /// <summary>Makes sure a folder exists, then opens it.</summary>
    public static bool OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception)
        {
            return false;
        }

        return OpenPath(path);
    }
}
