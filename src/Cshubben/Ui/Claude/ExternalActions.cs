using System.Diagnostics;

namespace Cshubben.Ui.Assist;

/// <summary>Opening links and copying text. Both never throw.</summary>
static class ExternalActions
{
    /// <summary>Opens an http(s) URL in the default browser. Returns false for anything else or on failure.</summary>
    public static bool OpenUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return false;
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Puts text on the clipboard. Returns false on failure.</summary>
    public static bool Copy(string text)
    {
        try
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
