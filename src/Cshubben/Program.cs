using Cshubben;
using Cshubben.Services;
using Cshubben.Ui;
using Cshubben.Ui.Pages;
using Microsoft.UI.Reactor;

// Velopack must run first: it handles install/update/uninstall hooks and may exit the process.
Velopack.VelopackApp.Build().Run();

var cmdArgs = Environment.GetCommandLineArgs();
AppOptions.Parse(cmdArgs);
UpdatesHost.Initialize(AppOptions.Demo || AppOptions.Smoke);
var services = AppServices.Initialize(AppOptions.Demo);
if (AppOptions.Smoke)
{
    // Headless: read-only checks, report to <data folder>/smoke-report.txt, then exit. No window, no writes to GitHub or Linear.
    SmokeMode.Run(services);
    return;
}

IntegrationWiring.Apply(services);
NotesPage.OpenPath = path => Shell.OpenPath(path);
NotificationsHub.Initialize(AppOptions.Demo, Cshubben.Core.Features.Notes.AppPaths.DataDirectory, cmdArgs);
if (!AppOptions.Demo)
    NotesPage.Store = new Cshubben.Core.Features.Notes.FileNoteStore(services.NotesFolder);
else if (AppOptions.StartPage == Page.Notes)
    NotesPage.InitialSelection = AppOptions.SelectedPr;
ReactorApp.Run<AppRoot>("Cshubben", width: 1440, height: 900);
