using Cshubben.Core.Features.Notifications;
using Cshubben.Core.Model;
using Xunit;

namespace Cshubben.Core.Tests.Features.Notifications;

public class QuietHoursTests
{
    [Theory]
    [InlineData(22, 7, 23, true)]
    [InlineData(22, 7, 3, true)]
    [InlineData(22, 7, 7, false)]
    [InlineData(22, 7, 12, false)]
    [InlineData(22, 7, 22, true)]
    [InlineData(9, 17, 12, true)]
    [InlineData(9, 17, 17, false)]
    [InlineData(9, 17, 8, false)]
    [InlineData(8, 8, 8, false)]
    public void Window_wraps_over_midnight_and_excludes_the_end(int startHour, int endHour, int hour, bool expected)
    {
        Assert.Equal(expected, QuietHours.IsQuiet(startHour * 60, endHour * 60, hour * 60));
    }

    [Fact]
    public void Format_and_parse_round_trip()
    {
        Assert.Equal("07:05", QuietHours.Format(7 * 60 + 5));
        Assert.Equal(7 * 60 + 5, QuietHours.Parse("07:05"));
        Assert.Null(QuietHours.Parse("24:00"));
        Assert.Null(QuietHours.Parse("7"));
        Assert.Null(QuietHours.Parse(null));
    }

    [Fact]
    public void Minute_of_day_uses_the_wall_clock_of_the_value()
    {
        Assert.Equal(23 * 60 + 30, QuietHours.MinuteOfDay(new DateTimeOffset(2026, 1, 1, 23, 30, 0, TimeSpan.FromHours(2))));
    }
}

public class NotificationStateTests
{
    private static string TempFile() => Path.Combine(Path.GetTempPath(), "cshubben-tests-" + Guid.NewGuid().ToString("N"), "state.json");

    [Fact]
    public void File_state_round_trips_and_creates_the_folder()
    {
        var path = TempFile();
        try
        {
            var state = new FileNotificationState(path);
            state.SaveSeen(new Dictionary<string, DateTimeOffset> { ["k1"] = N.T0 });
            var loaded = new FileNotificationState(path).LoadSeen();
            Assert.Equal(N.T0, loaded["k1"]);
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Fact]
    public void Damaged_state_file_gives_an_empty_set()
    {
        var path = TempFile();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{ not json");
            Assert.Empty(new FileNotificationState(path).LoadSeen());
            Assert.Empty(new FileNotificationState(path + ".missing").LoadSeen());
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Fact]
    public void Seen_set_drops_old_entries_and_caps_its_size()
    {
        var set = new SeenSet();
        set.Add("old", N.T0.AddDays(-40));
        set.Add("fresh", N.T0.AddDays(-1));
        set.Prune(N.T0);
        Assert.False(set.Contains("old"));
        Assert.True(set.Contains("fresh"));

        var big = new SeenSet();
        for (var i = 0; i < SeenSet.MaxEntries + 10; i++) big.Add("k" + i, N.T0.AddMinutes(i));
        big.Prune(N.T0.AddDays(1));
        Assert.Equal(SeenSet.MaxEntries, big.Count);
        Assert.False(big.Contains("k0"));
    }

    [Fact]
    public void Prefs_file_round_trips_and_sanitizes()
    {
        var path = TempFile();
        try
        {
            var store = new FileNotificationPrefsStore(path);
            store.Save(new NotificationPrefs { QuietHoursEnabled = true, QuietStartMinute = 99999, MaxIndividual = 0, Merged = true, MutedRepos = new[] { "a/b" } });
            var loaded = new FileNotificationPrefsStore(path).Load();
            Assert.True(loaded.QuietHoursEnabled);
            Assert.Equal(22 * 60, loaded.QuietStartMinute);
            Assert.Equal(NotificationPrefs.DefaultMaxIndividual, loaded.MaxIndividual);
            Assert.True(loaded.Merged);
            Assert.Equal(new[] { "a/b" }, loaded.MutedRepos);
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Fact]
    public void Damaged_prefs_file_gives_defaults()
    {
        var path = TempFile();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "[1,2");
            Assert.True(new FileNotificationPrefsStore(path).Load().Enabled);
        }
        finally
        {
            Cleanup(path);
        }
    }

    private static void Cleanup(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (dir is not null && Directory.Exists(dir)) Directory.Delete(dir, true);
    }
}

public class PlatformContractTests
{
    [Fact]
    public void Tray_status_counts_what_needs_the_viewer()
    {
        var reviewed = N.T0.AddHours(-2);
        var snap = N.Snap(
            authored: new[]
            {
                N.Pr("a", number: 1, ci: CiState.Failure),
                N.Pr("b", number: 2, ci: CiState.Success, review: ReviewDecision.Approved),
                N.Pr("c", number: 3, ci: CiState.Success),
            },
            reviews: new[] { N.Review("r1", 10), N.Review("r2", 11) },
            infos: new Dictionary<string, ReviewInfo>
            {
                ["r2"] = new ReviewInfo("r2", true, reviewed, reviewed.AddMinutes(-5), ReviewDecision.Approved),
            });
        var status = TrayStatus.From(snap);
        Assert.Equal(1, status.ReviewsWaiting);
        Assert.Equal(1, status.OwnCiFailing);
        Assert.Equal(1, status.ReadyToMerge);
        Assert.Equal(2, status.NeedsYou);
    }

    [Fact]
    public void Tray_tooltip_is_just_the_name_when_nothing_needs_you()
    {
        Assert.Equal("Cshubben", TrayStatus.Empty.Tooltip("Cshubben"));
        Assert.Equal("Cshubben - 3 waiting for your review - 2 own PRs with failing CI - 1 ready to merge",
            new TrayStatus(3, 2, 1).Tooltip("Cshubben"));
        Assert.Equal("Cshubben - 1 own PR with failing CI", new TrayStatus(0, 1, 0).Tooltip("Cshubben"));
    }

    [Fact]
    public void Tray_tooltip_stays_within_the_shell_limit()
    {
        Assert.True(new TrayStatus(1, 1, 1).Tooltip(new string('x', 200)).Length <= 120);
    }

    [Fact]
    public void Autostart_command_quotes_the_path_and_marks_background_launch()
    {
        var cmd = AutostartCommand.Build(@"C:\Program Files\Cshubben\Cshubben.exe");
        Assert.Equal("\"C:\\Program Files\\Cshubben\\Cshubben.exe\" --background", cmd);
        Assert.Equal(@"C:\Program Files\Cshubben\Cshubben.exe", AutostartCommand.ExePathOf(cmd));
        Assert.True(AutostartCommand.Matches(cmd, @"c:\program files\cshubben\cshubben.exe"));
        Assert.False(AutostartCommand.Matches(cmd, @"C:\other.exe"));
        Assert.Null(AutostartCommand.ExePathOf(""));
        Assert.Equal(@"C:\a.exe", AutostartCommand.ExePathOf(@"C:\a.exe --x"));
    }

    [Fact]
    public void Background_launch_is_detected_from_arguments()
    {
        Assert.True(AutostartCommand.IsBackgroundLaunch(new[] { "x.exe", "--BACKGROUND" }));
        Assert.False(AutostartCommand.IsBackgroundLaunch(new[] { "x.exe", "--demo" }));
    }

    [Fact]
    public void Fakes_remember_what_they_were_told()
    {
        var toast = new RecordingToastNotifier();
        toast.Show(new ToastMessage("t", "b", null, false, 1, NotificationKind.Merged));
        Assert.Single(toast.Shown);

        var tray = new RecordingTrayIcon();
        tray.SetStatus(new TrayStatus(1, 2, 3));
        Assert.Equal(2, tray.Last.OwnCiFailing);

        var auto = new InMemoryAutostart();
        Assert.False(auto.IsEnabled());
        Assert.True(auto.SetEnabled(true));
        Assert.True(auto.IsEnabled());

        Assert.False(new NullToastNotifier().IsAvailable);
        Assert.False(new NullTrayIcon().IsAvailable);
        Assert.False(new NullAutostart().SetEnabled(true));
    }

    [Fact]
    public void Headlines_are_readable_for_every_kind()
    {
        foreach (var kind in Enum.GetValues<NotificationKind>())
        {
            var e = new NotificationEvent("k", kind, "id", "acme/api", 7, "Fix it", "https://x/y", "alice");
            var aboutAPullRequest = kind is not (NotificationKind.MainRed or NotificationKind.DeployFailed
                or NotificationKind.ErrorSpike or NotificationKind.LinearAssigned or NotificationKind.LinearOutOfSync);
            if (aboutAPullRequest) Assert.Contains("acme/api #7", NotificationText.Headline(e));
            else Assert.False(string.IsNullOrWhiteSpace(NotificationText.Headline(e)));
            Assert.False(string.IsNullOrWhiteSpace(NotificationText.Body(e)));
        }
    }
}
