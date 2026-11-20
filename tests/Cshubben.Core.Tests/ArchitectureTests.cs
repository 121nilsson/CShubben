using System.Text.RegularExpressions;

namespace Cshubben.Core.Tests;

/// <summary>
/// Enforces the structure described in CLAUDE.md by scanning the source files:
/// Core never references the UI, and a feature never reaches into another feature.
/// Features may use the shared kernel (Shared, Model, Logic, Data).
/// </summary>
public class ArchitectureTests
{
    private static readonly string Root = FindRoot();
    private static readonly string CoreDir = Path.Combine(Root, "src", "Cshubben.Core");
    private static readonly string FeaturesDir = Path.Combine(CoreDir, "Features");

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Cshubben.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Cshubben.slnx not found above " + AppContext.BaseDirectory);
    }

    private static IEnumerable<string> SourceFiles(string dir) =>
        Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                        && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar));

    private static string Relative(string file) => Path.GetRelativePath(Root, file).Replace('\\', '/');

    [Fact]
    public void Core_has_source_files_and_features()
    {
        Assert.NotEmpty(SourceFiles(CoreDir));
        Assert.True(Directory.Exists(FeaturesDir));
        Assert.NotEmpty(Directory.GetDirectories(FeaturesDir));
    }

    [Fact]
    public void Core_never_references_the_ui()
    {
        var forbidden = new[] { "Microsoft.UI", "Microsoft.Windows", "Cshubben.Ui", "using Windows.", "WinUI" };
        var offenders = new List<string>();
        foreach (var file in SourceFiles(CoreDir))
        {
            var text = File.ReadAllText(file);
            foreach (var word in forbidden)
                if (text.Contains(word, StringComparison.Ordinal)) offenders.Add($"{Relative(file)} mentions '{word}'");
        }

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void Core_project_has_no_ui_package_or_project_references()
    {
        var csproj = File.ReadAllText(Path.Combine(CoreDir, "Cshubben.Core.csproj"));
        Assert.DoesNotContain("WindowsAppSDK", csproj, StringComparison.Ordinal);
        Assert.DoesNotContain("Reactor", csproj, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectReference", csproj, StringComparison.Ordinal);
        Assert.DoesNotContain("windows10", csproj, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Features_do_not_depend_on_each_other()
    {
        var features = Directory.GetDirectories(FeaturesDir).Select(d => Path.GetFileName(d)!).ToArray();
        var offenders = new List<string>();
        foreach (var feature in features)
        {
            foreach (var file in SourceFiles(Path.Combine(FeaturesDir, feature)))
            {
                var text = File.ReadAllText(file);
                foreach (var other in features.Where(f => f != feature))
                {
                    if (Regex.IsMatch(text, @"Cshubben\.Core\.Features\." + Regex.Escape(other) + @"\b"))
                        offenders.Add($"{Relative(file)} references feature '{other}'");
                }
            }
        }

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void Namespaces_follow_folders_for_features()
    {
        var offenders = new List<string>();
        foreach (var file in SourceFiles(FeaturesDir))
        {
            var feature = Path.GetRelativePath(FeaturesDir, file).Split(Path.DirectorySeparatorChar)[0];
            var match = Regex.Match(File.ReadAllText(file), @"^namespace\s+([\w.]+)", RegexOptions.Multiline);
            if (!match.Success) continue;
            var expected = "Cshubben.Core.Features." + feature;
            if (!match.Groups[1].Value.StartsWith(expected, StringComparison.Ordinal))
                offenders.Add($"{Relative(file)} declares {match.Groups[1].Value}, expected {expected}");
        }

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void Shared_kernel_does_not_depend_on_features()
    {
        var offenders = new List<string>();
        foreach (var dir in new[] { "Shared", "Model", "Logic", "Data" })
        {
            var path = Path.Combine(CoreDir, dir);
            if (!Directory.Exists(path)) continue;
            foreach (var file in SourceFiles(path).Where(f => !f.Contains(Path.Combine("Data", "GitHub"))))
                if (File.ReadAllText(file).Contains("Cshubben.Core.Features", StringComparison.Ordinal))
                    offenders.Add(Relative(file));
        }

        Assert.True(offenders.Count == 0, "Shared kernel references a feature: " + string.Join(", ", offenders));
    }

    [Fact]
    public void No_secrets_are_committed_in_source()
    {
        var pattern = new Regex(@"(ghp_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|lin_api_[A-Za-z0-9]{20,}|sk-ant-[A-Za-z0-9\-_]{20,})");
        var offenders = new List<string>();
        foreach (var file in SourceFiles(Root))
            if (pattern.IsMatch(File.ReadAllText(file))) offenders.Add(Relative(file));
        Assert.True(offenders.Count == 0, "Possible secret in: " + string.Join(", ", offenders));
    }
}
