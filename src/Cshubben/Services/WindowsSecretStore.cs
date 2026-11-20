using Cshubben.Core.Shared;
using Windows.Security.Credentials;

namespace Cshubben.Services;

/// <summary>
/// Secrets in the Windows Credential Locker (PasswordVault), which works for unpackaged desktop apps and is
/// encrypted per user. If the vault is unavailable, secrets live in memory for this run only, and
/// <see cref="LastError"/> says why. Secrets are never written to files or logs.
/// </summary>
sealed class WindowsSecretStore : ISecretStore
{
    private const string Resource = "Cshubben";

    private readonly InMemorySecretStore _fallback = new();
    private PasswordVault? _vault;
    private bool _vaultFailed;

    /// <summary>The last vault problem, or null. For the Settings page.</summary>
    public string? LastError { get; private set; }

    /// <summary>True while secrets are only kept in memory because the vault failed.</summary>
    public bool UsingFallback => _vaultFailed;

    private PasswordVault? Vault()
    {
        if (_vault is not null) return _vault;
        if (_vaultFailed) return null;
        try
        {
            _vault = new PasswordVault();
            return _vault;
        }
        catch (Exception ex)
        {
            _vaultFailed = true;
            LastError = "Windows credential store unavailable: " + ex.Message;
            return null;
        }
    }

    public string? Get(string key)
    {
        var vault = Vault();
        if (vault is not null)
        {
            try
            {
                var credential = vault.Retrieve(Resource, key);
                credential.RetrievePassword();
                return credential.Password;
            }
            catch (Exception)
            {
                // Retrieve throws when the entry does not exist. That simply means "no secret".
            }
        }

        return _fallback.Get(key);
    }

    public void Set(string key, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);
        var vault = Vault();
        if (vault is not null)
        {
            try
            {
                vault.Add(new PasswordCredential(Resource, key, value));
                _fallback.Delete(key);
                return;
            }
            catch (Exception ex)
            {
                LastError = "Could not save to the Windows credential store: " + ex.Message;
                _vaultFailed = true;
            }
        }

        _fallback.Set(key, value);
    }

    public void Delete(string key)
    {
        _fallback.Delete(key);
        var vault = Vault();
        if (vault is null) return;
        try
        {
            var credential = vault.Retrieve(Resource, key);
            vault.Remove(credential);
        }
        catch (Exception)
        {
            // Nothing stored under that key.
        }
    }
}
