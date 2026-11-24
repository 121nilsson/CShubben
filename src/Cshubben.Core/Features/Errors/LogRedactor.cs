using System.Text.RegularExpressions;

namespace Cshubben.Core.Features.Errors;

/// <summary>Text with sensitive values replaced, and how many were replaced.</summary>
public sealed record Redacted(string Text, int Count);

/// <summary>
/// Removes values that must not leave the machine from log text: credentials, tokens, e-mail addresses, personal identity
/// numbers and IP addresses. Logs can hold customer data, so everything that goes to Claude or into a summary that is pasted
/// somewhere passes through here first. Pure and idempotent: redacting redacted text changes nothing.
/// </summary>
public static class LogRedactor
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(250);

    private const string Hidden = "[text hidden: it could not be checked for sensitive values]";

    private sealed record Rule(Regex Pattern, string Replacement);

    private static Regex Make(string pattern, bool ignoreCase = false) =>
        new(pattern, RegexOptions.CultureInvariant | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None), Timeout);

    // Order matters: the more specific shapes go first so that, for example, the user part of an address with credentials
    // is not taken for an e-mail address.
    private static readonly Rule[] Rules =
    {
        // https://user:secret@host
        new(Make(@"(?<scheme>[A-Za-z][A-Za-z0-9+.\-]*://)[^/\s:@]+:[^/\s@]+@"), "${scheme}<redacted>@"),

        // JSON web tokens: three base64url parts, the first of which is a JSON object (it starts with "eyJ").
        new(Make(@"\beyJ[A-Za-z0-9_\-]{5,}\.[A-Za-z0-9_\-]{5,}\.[A-Za-z0-9_\-]{5,}"), "<token>"),

        // "Bearer" and "Basic" are also ordinary words: a bearer token has a digit or a dot in it, and a Basic credential only
        // counts after an Authorization header.
        new(Make(@"\bBearer\s+(?=[A-Za-z0-9._~+/\-]*(?:\d|\.[A-Za-z0-9]))[A-Za-z0-9._~+/\-]{8,}=*", ignoreCase: true), "Bearer <token>"),
        new(Make(@"(?<=\bAuthorization[""']?\s*[:=]\s*[""']?)Basic\s+[A-Za-z0-9+/]{8,}=*", ignoreCase: true), "Basic <token>"),

        // password=..., "client_secret": "...", AccountKey=..., sig=... (connection strings, query strings, JSON).
        new(
            Make(
                @"(?<key>\b(?:password|passwd|pwd|secret|client_?secret|api_?key|access_?token|refresh_?token|account_?key|shared_?access_?key|sas_?token|signature)\b)(?<sep>[""']?\s*[=:]\s*)(?<val>""[^""]*""|'[^']*'|[^\s;,&""']+)",
                ignoreCase: true),
            "${key}${sep}<redacted>"),

        new(Make(@"\b[A-Za-z0-9._%+\-]+@[A-Za-z0-9\-]+(?:\.[A-Za-z0-9\-]+)*\.[A-Za-z]{2,}\b"), "<email>"),

        // Swedish personal identity number: 811218-9876, 19811218-9876 (always with the dash or plus).
        new(Make(@"(?<![\w\-])(?:19|20)?\d{6}[-+]\d{4}(?![\w\-])"), "<personal-id>"),

        // IPv4 address; assembly versions such as Version=4.0.0.0 are left alone.
        new(
            Make(@"(?<![\d.])(?<!Version=)(?:25[0-5]|2[0-4]\d|1?\d?\d)(?:\.(?:25[0-5]|2[0-4]\d|1?\d?\d)){3}(?![\d.])"),
            "<ip>"),
    };

    /// <summary>The text without sensitive values, and how many there were.</summary>
    public static Redacted Apply(string? text)
    {
        if (string.IsNullOrEmpty(text)) return new Redacted("", 0);

        var count = 0;
        var current = text;
        try
        {
            foreach (var rule in Rules)
            {
                current = rule.Pattern.Replace(current, match =>
                {
                    // Already redacted by an earlier pass.
                    if (match.Value.EndsWith("<redacted>", StringComparison.Ordinal)) return match.Value;
                    count++;
                    return match.Result(rule.Replacement);
                });
            }
        }
        catch (RegexMatchTimeoutException)
        {
            // Better to show nothing than something unchecked.
            return new Redacted(Hidden, 1);
        }

        return new Redacted(current, count);
    }

    /// <summary>The text without sensitive values.</summary>
    public static string Redact(string? text) => Apply(text).Text;
}
