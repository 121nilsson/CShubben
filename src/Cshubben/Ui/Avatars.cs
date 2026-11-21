using Cshubben.Core.Shared;
using Cshubben.Services;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Cshubben.Ui;

/// <summary>
/// A round picture of a GitHub user. WinUI's PersonPicture shows the picture when it loads and the initials until then
/// (and when it never does). Demo data never loads pictures: its names are invented and could match real accounts.
/// </summary>
static class Avatars
{
    /// <param name="imageUrl">A picture link that is already known and checked (for example from Linear); otherwise a GitHub one is derived from the login.</param>
    public static Element Of(string login, double size = 28, string? imageUrl = null)
    {
        var picture = PersonPicture().DisplayName(login).Width(size).Height(size);
        var url = AppServices.Current.ForcedDemo ? null : imageUrl ?? AvatarUrl.ForGitHub(login, size);
        return (url is null ? picture : picture.ProfilePicture(url)).AutomationName(login);
    }
}
