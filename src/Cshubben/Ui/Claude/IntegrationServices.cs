using Cshubben.Services;

namespace Cshubben.Ui.Assist;

/// <summary>
/// Where the Claude, GitHub-action and Linear integrations used by the inspector, the Overview briefing and the Linear page
/// come from. The defaults are the demo implementations, so nothing real is ever sent unless the app assigns real ones at startup.
/// </summary>
static class IntegrationServices
{
    public static IClaudeAssistant Claude { get; set; } = new DemoClaudeData();

    public static IInspectorActions Actions { get; set; } = new DemoInspectorActions();

    public static ILinearData Linear { get; set; } = new DemoLinearData();
}
