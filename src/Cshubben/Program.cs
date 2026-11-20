using Cshubben;
using Cshubben.Ui;
using Microsoft.UI.Reactor;

AppOptions.Parse(Environment.GetCommandLineArgs());
ReactorApp.Run<AppRoot>("Cshubben", width: 1440, height: 900);
