using Cshubben.Core.Composition;
using Cshubben.Core.Features.Claude;
using Cshubben.Core.Features.Settings;
using Cshubben.Core.Shared;

namespace Cshubben.Services;

/// <summary>Runs prompts through Claude Code. The real one wraps <see cref="ClaudeRunner"/>; <see cref="DemoClaudeData"/> answers from canned text.</summary>
public interface IClaudeAssistant
{
    bool IsDemo { get; }

    /// <summary>False when the claude CLI is not installed.</summary>
    bool IsAvailable { get; }

    Task<ClaudeResult> RunAsync(ClaudeRequest request, CancellationToken cancellationToken);
}

public sealed class RealClaudeAssistant(ClaudeRunner runner, Func<bool> isAvailable, Func<AppSettings>? settings = null) : IClaudeAssistant
{
    public bool IsDemo => false;

    public bool IsAvailable => isAvailable();

    public Task<ClaudeResult> RunAsync(ClaudeRequest request, CancellationToken cancellationToken) =>
        runner.RunAsync(WithProfile(request), cancellationToken);

    /// <summary>
    /// A request that names no model and no effort gets the ones chosen for its function in settings (or that function's default,
    /// or the general Claude setting). A request that sets either keeps what it asked for.
    /// </summary>
    private ClaudeRequest WithProfile(ClaudeRequest request)
    {
        if (settings is null || request.Feature is not { } feature || request.Model is not null || request.Effort is not null) return request;
        var (model, effort) = ClaudeSettings.Resolve(settings(), feature);
        return request with { Model = model, Effort = effort };
    }

    public static RealClaudeAssistant CreateDefault(ISecretStore? secrets = null, Func<AppSettings>? settings = null) =>
        new(ClaudeRunner.CreateDefault(secrets), () => ClaudeLocator.Find() is not null, settings);
}
