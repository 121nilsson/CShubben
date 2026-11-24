using Cshubben.Core.Features.Errors;
using Xunit;

namespace Cshubben.Core.Tests.Errors;

public class LogRedactorTests
{
    // Every value below is made up.

    [Fact]
    public void An_email_address_is_replaced()
    {
        var r = LogRedactor.Apply("User anna.svensson@example.com could not sign in");
        Assert.Equal("User <email> could not sign in", r.Text);
        Assert.Equal(1, r.Count);
    }

    [Fact]
    public void Passwords_and_secrets_in_connection_strings_and_json_are_hidden_but_the_key_stays()
    {
        Assert.Equal(
            "Server=db;Password=<redacted>;Database=x",
            LogRedactor.Redact("Server=db;Password=hunter2;Database=x"));

        var json = LogRedactor.Redact("{\"client_secret\":\"abc123xyz\",\"name\":\"ok\"}");
        Assert.DoesNotContain("abc123xyz", json, StringComparison.Ordinal);
        Assert.Contains("client_secret", json, StringComparison.Ordinal);
        Assert.Contains("\"name\":\"ok\"", json, StringComparison.Ordinal);

        Assert.Equal("?signature=<redacted>&se=2026", LogRedactor.Redact("?signature=AbCdEf123%2B&se=2026"));
    }

    [Fact]
    public void Tokens_are_replaced()
    {
        Assert.Equal(
            "token <token> expired",
            LogRedactor.Redact("token eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.abcdefghijk expired"));
        Assert.Equal("Authorization: Bearer <token>", LogRedactor.Redact("Authorization: Bearer abc123def456ghi"));
        Assert.Equal("Authorization: Basic <token>", LogRedactor.Redact("Authorization: Basic dXNlcjpwYXNzd29yZA=="));
    }

    [Fact]
    public void Ordinary_words_that_look_like_a_scheme_are_left_alone()
    {
        const string text = "Basic authentication is required. Bearer authentication scheme is not supported.";
        Assert.Equal(text, LogRedactor.Redact(text));
    }

    [Fact]
    public void Credentials_in_an_address_are_hidden_and_the_rest_of_the_address_stays()
    {
        Assert.Equal(
            "connect to https://<redacted>@db.example.com/x failed",
            LogRedactor.Redact("connect to https://user:s3cret@db.example.com/x failed"));
    }

    [Fact]
    public void Ip_addresses_and_personal_identity_numbers_are_replaced_but_versions_and_guids_are_not()
    {
        Assert.Equal("from <ip> port 443", LogRedactor.Redact("from 10.20.30.40 port 443"));
        Assert.Equal("pnr <personal-id> ok", LogRedactor.Redact("pnr 811218-9876 ok"));
        Assert.Equal("pnr <personal-id> ok", LogRedactor.Redact("pnr 19811218-9876 ok"));

        const string untouched = "Acme, Version=4.0.0.0, id 19345678-1234-4000-8000-000000000001";
        Assert.Equal(untouched, LogRedactor.Redact(untouched));
    }

    [Fact]
    public void A_stack_trace_is_left_as_it_is()
    {
        const string trace = "System.NullReferenceException: Object reference not set.\n   at Acme.Svc.Run(Guid id) in /_/src/Proj/File.cs:line 40";
        var r = LogRedactor.Apply(trace);
        Assert.Equal(trace, r.Text);
        Assert.Equal(0, r.Count);
    }

    [Fact]
    public void Redacting_twice_changes_nothing_and_counts_nothing_the_second_time()
    {
        const string text = "mail a@b.example.com Password=x1 https://u:p@h.example.com Authorization: Bearer abc123def456 from 1.2.3.4";
        var once = LogRedactor.Apply(text);
        Assert.True(once.Count >= 5);

        var twice = LogRedactor.Apply(once.Text);
        Assert.Equal(once.Text, twice.Text);
        Assert.Equal(0, twice.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Nothing_in_nothing_out(string? text)
    {
        var r = LogRedactor.Apply(text);
        Assert.Equal("", r.Text);
        Assert.Equal(0, r.Count);
    }

    [Fact]
    public void The_analysis_prompt_never_contains_what_was_hidden_and_says_how_much_was_hidden()
    {
        var now = new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);
        var group = new ErrorGroup("k", "Failed for anna@example.com", "Password=hunter2", null, ErrorKind.Message, 1,
            now, now, new[] { "pod-1" }, new int[288], null, null, null,
            new[] { new LogEntry(now, "app", "pod-1", "Error", "[2026-11-24T12:00:00Z Error] pod - Logger - t\nLogin of anna@example.com from 10.1.2.3 failed") });

        var prompt = ErrorPrompts.Analyze("app", group, null, now);

        Assert.DoesNotContain("anna@example.com", prompt.UserPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", prompt.UserPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("10.1.2.3", prompt.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("<email>", prompt.UserPrompt, StringComparison.Ordinal);
        Assert.Equal(4, prompt.Redactions);
        Assert.Contains("<redacted>", prompt.SystemPrompt, StringComparison.Ordinal);
    }
}
