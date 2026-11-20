using Cshubben.Core.Features.Claude;
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

public sealed class RealClaudeAssistant(ClaudeRunner runner, Func<bool> isAvailable) : IClaudeAssistant
{
    public bool IsDemo => false;

    public bool IsAvailable => isAvailable();

    public Task<ClaudeResult> RunAsync(ClaudeRequest request, CancellationToken cancellationToken) =>
        runner.RunAsync(request, cancellationToken);

    public static RealClaudeAssistant CreateDefault(ISecretStore? secrets = null) =>
        new(ClaudeRunner.CreateDefault(secrets), () => ClaudeLocator.Find() is not null);
}
