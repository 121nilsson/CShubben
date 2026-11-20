using Cshubben.Core.Integrations.Claude;
using Xunit;

namespace Cshubben.Core.Tests.Integrations;

public class ClaudeLocatorTests
{
    private static readonly string Local = Path.Combine("/local");
    private static readonly string Home = Path.Combine("/home", "u");

    private static LocatorEnvironment Env(string? over = null, string? path = null, bool windows = true) =>
        new(windows, over, path, Local, Home);

    [Fact]
    public void Override_wins_over_path()
    {
        var over = Path.Combine("/custom", "claude.exe");
        var onPath = Path.Combine("/bin", "claude.exe");
        var fs = new FakeFileSystem(over, onPath);
        Assert.Equal(over, ClaudeLocator.Find(Env(over, "/bin"), fs));
    }

    [Fact]
    public void Missing_override_gives_null_even_when_path_has_claude()
    {
        var onPath = Path.Combine("/bin", "claude.exe");
        var fs = new FakeFileSystem(onPath);
        Assert.Null(ClaudeLocator.Find(Env(Path.Combine("/nope", "claude.exe"), "/bin"), fs));
    }

    [Fact]
    public void Finds_claude_on_path()
    {
        var exe = Path.Combine("/b", "claude.exe");
        var fs = new FakeFileSystem(exe);
        var path = "/a" + Path.PathSeparator + "/b";
        Assert.Equal(exe, ClaudeLocator.Find(Env(path: path), fs));
    }

    [Fact]
    public void Non_windows_looks_for_plain_claude()
    {
        var exe = Path.Combine("/usr/local/bin", "claude");
        var fs = new FakeFileSystem(exe);
        Assert.Equal(exe, ClaudeLocator.Find(Env(path: "/usr/local/bin", windows: false), fs));
    }

    [Fact]
    public void Desktop_copy_with_newest_version_is_chosen()
    {
        var older = Path.Combine(Local, "Claude-Data", "app", "claude-code", "2.9.0", "h1", "claude.exe");
        var newer = Path.Combine(Local, "Claude-Data", "app", "claude-code", "2.10.0", "h2", "claude.exe");
        var fs = new FakeFileSystem(older, newer);
        Assert.Equal(newer, ClaudeLocator.Find(Env(), fs));
    }

    [Fact]
    public void Vscode_extension_is_found()
    {
        var exe = Path.Combine(Home, ".vscode", "extensions", "anthropic.claude-code-2.0.1-win32-x64", "resources", "native-binary", "claude.exe");
        var fs = new FakeFileSystem(exe);
        Assert.Equal(exe, ClaudeLocator.Find(Env(), fs));
    }

    [Fact]
    public void Newest_version_wins_across_desktop_and_vscode()
    {
        var desktop = Path.Combine(Local, "Claude-Data", "app", "claude-code", "2.0.0", "h", "claude.exe");
        var vscode = Path.Combine(Home, ".vscode", "extensions", "anthropic.claude-code-2.1.0-win32-x64", "resources", "native-binary", "claude.exe");
        var fs = new FakeFileSystem(desktop, vscode);
        Assert.Equal(vscode, ClaudeLocator.Find(Env(), fs));
    }

    [Fact]
    public void Nothing_found_gives_null()
    {
        Assert.Null(ClaudeLocator.Find(Env(path: "/x"), new FakeFileSystem()));
    }

    [Theory]
    [InlineData("2.0.5-win32-x64", new[] { 2, 0, 5 })]
    [InlineData("10.2", new[] { 10, 2 })]
    [InlineData("none", new int[0])]
    public void ParseVersion_reads_numbers(string text, int[] expected)
    {
        Assert.Equal(expected, ClaudeLocator.ParseVersion(text));
    }
}
