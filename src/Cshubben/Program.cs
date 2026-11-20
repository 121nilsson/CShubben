using Cshubben;
using Cshubben.Ui;
using Cshubben.Ui.Pages;
using Microsoft.UI.Reactor;

var cmdArgs = Environment.GetCommandLineArgs();
AppOptions.Parse(cmdArgs);
if (!cmdArgs.Contains("--demo"))
    NotesPage.Store = new Cshubben.Core.Notes.FileNoteStore(Cshubben.Core.Notes.AppPaths.NotesDirectory);
else if (AppOptions.StartPage == Page.Notes)
    NotesPage.InitialSelection = AppOptions.SelectedPr;
ReactorApp.Run<AppRoot>("Cshubben", width: 1440, height: 900);
