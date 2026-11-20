using Cshubben;
using Cshubben.Services;
using Cshubben.Ui;
using Cshubben.Ui.Pages;
using Microsoft.UI.Reactor;

var cmdArgs = Environment.GetCommandLineArgs();
AppOptions.Parse(cmdArgs);
var services = AppServices.Initialize(AppOptions.Demo);
IntegrationWiring.Apply(services);
NotesPage.OpenPath = path => Shell.OpenPath(path);
if (!AppOptions.Demo)
    NotesPage.Store = new Cshubben.Core.Features.Notes.FileNoteStore(services.NotesFolder);
else if (AppOptions.StartPage == Page.Notes)
    NotesPage.InitialSelection = AppOptions.SelectedPr;
ReactorApp.Run<AppRoot>("Cshubben", width: 1440, height: 900);
