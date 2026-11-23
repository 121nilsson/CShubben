using Cshubben.Core.Features.Settings;
using Cshubben.Core.Logic;
using Cshubben.Core.Shared;
using Cshubben.Services;
using Cshubben.Ui.Assist;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Cshubben.Ui.Pages;

/// <summary>
/// Settings card of the Errors module: the on/off switch (off by default), the Grafana connection, the mapping from
/// application names to repositories, and sharing those mappings with the team. The token goes to the credential store only.
/// </summary>
sealed class ErrorsSettingsCard : Component<PageContext>
{
    public override Element Render()
    {
        var ctx = Props;
        var services = ctx.Services;
        var settings = ctx.Settings;
        var errors = settings.Errors;
        void Save(ErrorsSettings next) => ctx.SaveSettings(settings with { Errors = next });

        var (hasToken, setHasToken) = UseState(false, true);
        var (tokenInput, setTokenInput) = UseState("");
        var (urlInput, setUrlInput) = UseState(errors.GrafanaUrl);
        var (urlError, setUrlError) = UseState("");
        var (rootInput, setRootInput) = UseState(errors.RepoRoot);
        var (appInput, setAppInput) = UseState("");
        var (repoInput, setRepoInput) = UseState("");
        var (prefixInput, setPrefixInput) = UseState("");
        var (nsRepoInput, setNsRepoInput) = UseState("");
        var (bindingError, setBindingError) = UseState("");
        var (importText, setImportText) = UseState("");
        var (importMessage, setImportMessage) = UseState("");
        var (nsInput, setNsInput) = UseState(errors.Namespace);
        var (dsInput, setDsInput) = UseState(errors.LokiDatasource);
        var (testing, setTesting) = UseState(false, true);
        var (testResult, setTestResult) = UseState<ConnectionResult?>(null, true);
        var (mcpInput, setMcpInput) = UseState(errors.McpUrl);
        var (wsInput, setWsInput) = UseState(errors.Workspace);
        var (rgInput, setRgInput) = UseState(errors.ResourceGroup);
        var (azureError, setAzureError) = UseState("");
        var (signedIn, setSignedIn) = UseState(false, true);
        var (signingIn, setSigningIn) = UseState(false, true);
        var (signMessage, setSignMessage) = UseState("", true);

        UseEffect(() =>
        {
            _ = Task.Run(() => setHasToken(services.Secrets.Get(SecretKeys.GrafanaToken) is not null));
            return () => { };
        }, Array.Empty<object>());

        UseEffect(() =>
        {
            var url = errors.McpUrl;
            _ = Task.Run(() => setSignedIn(services.IsSignedInToLogs(url)));
            return () => { };
        }, errors.McpUrl);

        var toggle = ToggleSwitch(errors.Enabled, v => Save(errors with { Enabled = v }), "On", "Off", "Show the Errors page");
        const string help = "Error spikes from your Grafana logs, tied to deploys. Off by default: while it is off there is no Errors page "
            + "and nothing talks to Grafana.";

        if (!errors.Enabled) return Kit.SettingsCard("Errors", help, toggle);

        var knownRepos = services.RepoActivity.Keys.ToList();

        // ---- Grafana connection ----
        void SaveUrl()
        {
            var trimmed = urlInput.Trim();
            if (trimmed.Length == 0)
            {
                Save(errors with { GrafanaUrl = "" });
                return;
            }

            var url = RepoBindings.NormalizeUrl(trimmed);
            if (url is null)
            {
                setUrlError("Enter a full https:// address. Plain http:// is only allowed for localhost.");
                return;
            }

            setUrlInput(url);
            Save(errors with { GrafanaUrl = url });
        }

        var grafanaRows = new Element[]
        {
            Row("Grafana address", FlexRow(
                TextBox(urlInput, v => { setUrlInput(v); setUrlError(""); }, placeholderText: "https://grafana.example.com")
                    .Flex(grow: 1, shrink: 1, basis: 0).MinWidth(120),
                Button("Save", SaveUrl).IsEnabled(urlInput.Trim() != errors.GrafanaUrl).Flex(shrink: 0)) with { ColumnGap = 8 }),
            urlError.Length > 0 ? Kit.Muted(urlError).TextWrapping().Foreground(Kit.Fg(Tone.Danger)) : Empty(),
            Row("Token", Text(hasToken ? "Saved in the Windows credential store" : "No token yet")),
            FlexRow(
                PasswordBox(tokenInput, setTokenInput, placeholderText: "Paste a Grafana service account token")
                    .Flex(grow: 1, shrink: 1, basis: 0).MinWidth(120),
                Button("Save", () =>
                {
                    var token = tokenInput.Trim();
                    if (token.Length == 0) return;
                    services.Secrets.Set(SecretKeys.GrafanaToken, token);
                    setTokenInput("");
                    setHasToken(true);
                }).IsEnabled(tokenInput.Trim().Length > 0).Flex(shrink: 0),
                Button("Clear", () =>
                {
                    services.Secrets.Delete(SecretKeys.GrafanaToken);
                    setHasToken(false);
                }).IsEnabled(hasToken).Flex(shrink: 0)) with { ColumnGap = 8 },
            Kit.Muted("Use a read-only service account. The token is never written to the settings file.").TextWrapping(),
            Row("Namespace", FlexRow(
                TextBox(nsInput, setNsInput, placeholderText: "default").Flex(grow: 1, shrink: 1, basis: 0).MinWidth(120),
                Button("Save", () =>
                {
                    Save(errors with { Namespace = nsInput });
                    setNsInput(RepoBindings.NormalizeApp(nsInput) ?? ErrorsSettings.DefaultNamespace);
                }).IsEnabled(nsInput.Trim() != errors.Namespace).Flex(shrink: 0)) with { ColumnGap = 8 }),
            Row("Loki data source UID", FlexRow(
                TextBox(dsInput, setDsInput, placeholderText: "Empty: use the first Loki data source").Flex(grow: 1, shrink: 1, basis: 0).MinWidth(120),
                Button("Save", () => Save(errors with { LokiDatasource = dsInput.Trim() }))
                    .IsEnabled(dsInput.Trim() != errors.LokiDatasource).Flex(shrink: 0)) with { ColumnGap = 8 }),
        };

        // ---- Azure Monitor through the MCP server ----
        void SaveAzure()
        {
            var url = mcpInput.Trim().Length == 0 ? "" : RepoBindings.NormalizeUrl(mcpInput);
            if (url is null)
            {
                setAzureError("Enter the full https:// address of the MCP server.");
                return;
            }

            if ((wsInput.Trim().Length > 0 && RepoBindings.NormalizeApp(wsInput) is null)
                || (rgInput.Trim().Length > 0 && RepoBindings.NormalizeApp(rgInput) is null))
            {
                setAzureError("The workspace and resource group names may only have letters, digits, dots, dashes and underscores.");
                return;
            }

            setAzureError("");
            setMcpInput(url);
            Save(errors with { McpUrl = url, Workspace = wsInput.Trim(), ResourceGroup = rgInput.Trim() });
        }

        var azureChanged = mcpInput.Trim() != errors.McpUrl || wsInput.Trim() != errors.Workspace || rgInput.Trim() != errors.ResourceGroup;
        var azureRows = new Element[]
        {
            Row("MCP address", TextBox(mcpInput, v => { setMcpInput(v); setAzureError(""); }, placeholderText: "https://…azurecontainerapps.io")
                .HAlign(HorizontalAlignment.Stretch)),
            Row("Workspace", TextBox(wsInput, v => { setWsInput(v); setAzureError(""); }, placeholderText: "Log Analytics workspace name")
                .HAlign(HorizontalAlignment.Stretch)),
            Row("Resource group", TextBox(rgInput, v => { setRgInput(v); setAzureError(""); }, placeholderText: "Usually the same as the workspace")
                .HAlign(HorizontalAlignment.Stretch)),
            HStack(8, Button("Save", SaveAzure).IsEnabled(azureChanged)),
            azureError.Length > 0 ? Kit.Muted(azureError).TextWrapping().Foreground(Kit.Fg(Tone.Danger)) : Empty(),
            Row("Sign-in", Text(signedIn ? "Signed in with your Microsoft account" : "Not signed in")),
            FlexRow(
                Button(signingIn ? "Waiting for the browser…" : signedIn ? "Sign in again" : "Sign in", () =>
                {
                    setSigningIn(true);
                    setSignMessage("");
                    var url = errors.McpUrl;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var result = await services.SignInToLogsAsync(url);
                            setSignedIn(result.Ok);
                            setSignMessage(result.Ok ? "" : result.Message);
                        }
                        finally { setSigningIn(false); }
                    });
                }).IsEnabled(!signingIn && errors.McpUrl.Length > 0 && !azureChanged).Flex(shrink: 0),
                Button("Sign out", () =>
                {
                    services.SignOutOfLogs(errors.McpUrl);
                    setSignedIn(false);
                }).IsEnabled(signedIn).Flex(shrink: 0)) with { ColumnGap = 8 },
            signMessage.Length > 0 ? Kit.Muted(signMessage).TextWrapping().Foreground(Kit.Fg(Tone.Danger)) : Empty(),
            Kit.Muted("Signing in opens your browser. Only a refresh token is kept, in the Windows credential store.").TextWrapping(),
        };

        var sourceRow = Row("Read logs from", ComboBox(
            new[] { "Azure Monitor (through the MCP server)", "Grafana (Loki)" },
            errors.Source == ErrorsSettings.SourceGrafana ? 1 : 0,
            i => Save(errors with { Source = i == 1 ? ErrorsSettings.SourceGrafana : ErrorsSettings.SourceAzure }))
            .HAlign(HorizontalAlignment.Stretch));

        var warningsRow = Row("Include warnings", ToggleSwitch(errors.IncludeWarnings, v => Save(errors with { IncludeWarnings = v }), "On", "Off", "Include warnings in the error counts"));

        var grafanaSource = errors.Source == ErrorsSettings.SourceGrafana;
        var canTest = grafanaSource ? errors.GrafanaUrl.Length > 0 && hasToken : ErrorsServices.IsConfigured(errors) && signedIn && !azureChanged;
        var testRow = FlexRow(
            Button(testing ? "Testing…" : "Test connection", () =>
            {
                setTesting(true);
                setTestResult(null);
                _ = Task.Run(async () =>
                {
                    try { setTestResult(await services.TestGrafanaAsync()); }
                    finally { setTesting(false); }
                });
            }).IsEnabled(!testing && canTest).Flex(shrink: 0),
            testResult is null
                ? Empty()
                : Kit.Muted(testResult.Message).TextWrapping().Foreground(Kit.Fg(testResult.Ok ? Tone.Success : Tone.Danger))
                    .VAlign(VerticalAlignment.Center)) with { ColumnGap = 12 };

        var connectionRows = new Element[] { sourceRow }
            .Concat(grafanaSource ? grafanaRows : azureRows)
            .Concat(new Element[] { warningsRow, testRow })
            .ToArray();

        // ---- Applications -> repositories ----
        void AddBinding()
        {
            if (RepoBindings.NormalizeApp(appInput) is null || RepoBindings.NormalizeRepo(repoInput) is null)
            {
                setBindingError("Use an application name like binders-api and a repository like owner/repo.");
                return;
            }

            Save(errors with { Bindings = RepoBindings.Set(errors.Bindings, appInput, repoInput) });
            setAppInput("");
            setRepoInput("");
            setBindingError("");
        }

        Element BindingRow(RepoBinding b) =>
            Grid(
                columns: [GridSize.Star(), GridSize.Star(), GridSize.Auto],
                rows: [GridSize.Auto],
                BodyStrong(b.App).VAlign(VerticalAlignment.Center).Grid(column: 0),
                Kit.Muted(b.Repo).VAlign(VerticalAlignment.Center).Grid(column: 1),
                Button("Remove", () => Save(errors with { Bindings = RepoBindings.Remove(errors.Bindings, b.App) }))
                    .SubtleButton().AutomationName("Remove the mapping for " + b.App).Grid(column: 2))
            .WithKey(b.App);

        var bindingRows = new Element[]
        {
            BodyStrong("Applications"),
            Kit.Muted("The application name is the AppRoleName (Azure) or appname (Grafana) in the logs. Binding it to a repository is what ties a spike to the right deploys and code.").TextWrapping(),
            errors.Bindings.Count == 0
                ? Kit.Muted("No applications are bound yet.")
                : VStack(4, errors.Bindings.Select(BindingRow).ToArray()),
            FlexRow(
                TextBox(appInput, v =>
                    {
                        setAppInput(v);
                        setBindingError("");
                        if (repoInput.Trim().Length == 0 && RepoBindings.Suggest(v, knownRepos) is { } suggestion) setRepoInput(suggestion);
                    }, placeholderText: "Application, e.g. binders-api")
                    .Flex(grow: 1, shrink: 1, basis: 0).MinWidth(120),
                TextBox(repoInput, v => { setRepoInput(v); setBindingError(""); }, placeholderText: "owner/repository")
                    .Flex(grow: 1, shrink: 1, basis: 0).MinWidth(120),
                Button("Add", AddBinding)
                    .IsEnabled(appInput.Trim().Length > 0 && repoInput.Trim().Length > 0).Flex(shrink: 0)) with { ColumnGap = 8 },
            bindingError.Length > 0 ? Kit.Muted(bindingError).TextWrapping().Foreground(Kit.Fg(Tone.Danger)) : Empty(),
        };

        // ---- Namespaces -> repositories ----
        void AddNamespace()
        {
            if (RepoBindings.NormalizePrefix(prefixInput) is null || RepoBindings.NormalizeRepo(nsRepoInput) is null)
            {
                setBindingError("Use a namespace like Acme.BuildingBlocks and a repository like owner/repo.");
                return;
            }

            Save(errors with { Namespaces = RepoBindings.SetNamespace(errors.Namespaces, prefixInput, nsRepoInput) });
            setPrefixInput("");
            setNsRepoInput("");
            setBindingError("");
        }

        Element NamespaceRow(NamespaceBinding n) =>
            Grid(
                columns: [GridSize.Star(), GridSize.Star(), GridSize.Auto],
                rows: [GridSize.Auto],
                BodyStrong(n.Prefix).VAlign(VerticalAlignment.Center).Grid(column: 0),
                Kit.Muted(n.Repo).VAlign(VerticalAlignment.Center).Grid(column: 1),
                Button("Remove", () => Save(errors with { Namespaces = RepoBindings.RemoveNamespace(errors.Namespaces, n.Prefix) }))
                    .SubtleButton().AutomationName("Remove the mapping for " + n.Prefix).Grid(column: 2))
            .WithKey(n.Prefix);

        var namespaceRows = new Element[]
        {
            BodyStrong("Shared libraries"),
            Kit.Muted("Stack traces name the namespace of the failing code. Bind a namespace prefix to the repository of a shared library, so an error in it points there and not at the app that happened to call it.").TextWrapping(),
            errors.Namespaces.Count == 0
                ? Kit.Muted("No namespaces are bound yet.")
                : VStack(4, errors.Namespaces.Select(NamespaceRow).ToArray()),
            FlexRow(
                TextBox(prefixInput, v => { setPrefixInput(v); setBindingError(""); }, placeholderText: "Namespace, e.g. Acme.BuildingBlocks")
                    .Flex(grow: 1, shrink: 1, basis: 0).MinWidth(120),
                TextBox(nsRepoInput, v => { setNsRepoInput(v); setBindingError(""); }, placeholderText: "owner/repository")
                    .Flex(grow: 1, shrink: 1, basis: 0).MinWidth(120),
                Button("Add", AddNamespace)
                    .IsEnabled(prefixInput.Trim().Length > 0 && nsRepoInput.Trim().Length > 0).Flex(shrink: 0)) with { ColumnGap = 8 },
        };

        // ---- Local clones and sharing ----
        var rootRows = new Element[]
        {
            BodyStrong("Local clones"),
            FlexRow(
                TextBox(rootInput, setRootInput, placeholderText: "Folder with your clones, e.g. D:\\src")
                    .Flex(grow: 1, shrink: 1, basis: 0).MinWidth(120),
                Button("Save", () => Save(errors with { RepoRoot = rootInput.Trim() }))
                    .IsEnabled(rootInput.Trim() != errors.RepoRoot).Flex(shrink: 0)) with { ColumnGap = 8 },
            Kit.Muted("Optional. One sub folder per repository. Used later for deeper analysis; nothing is read from it yet.").TextWrapping(),
        };

        var shareRows = new Element[]
        {
            BodyStrong("Share with the team"),
            Kit.Muted("Copy the mappings (applications and namespaces, not the address, token or folders) and paste them on another machine.").TextWrapping(),
            HStack(8, CopyLinkButton.For(RepoBindings.ToJson(errors), "Copy mappings")),
            FlexRow(
                TextBox(importText, setImportText, placeholderText: "Paste copied mappings here")
                    .Flex(grow: 1, shrink: 1, basis: 0).MinWidth(120),
                Button("Import", () =>
                {
                    var result = RepoBindings.Import(errors, importText);
                    setImportMessage(result.Message);
                    if (!result.Ok) return;
                    Save(result.Settings);
                    setImportText("");
                }).IsEnabled(importText.Trim().Length > 0).Flex(shrink: 0)) with { ColumnGap = 8 },
            importMessage.Length > 0 ? Kit.Muted(importMessage).TextWrapping() : Empty(),
        };

        return Kit.SettingsCard("Errors", help,
            new Element[] { toggle }
                .Concat(connectionRows)
                .Concat(bindingRows)
                .Concat(namespaceRows)
                .Concat(rootRows)
                .Concat(shareRows)
                .ToArray());
    }

    private static Element Row(string label, Element value) =>
        Grid(
            columns: [GridSize.Px(130), GridSize.Star()],
            rows: [GridSize.Auto],
            Kit.Muted(label).VAlign(VerticalAlignment.Center).Grid(column: 0),
            value.VAlign(VerticalAlignment.Center).Grid(column: 1));

    private static TextBlockElement Text(string text) => TextBlock(text).TextWrapping();
}
