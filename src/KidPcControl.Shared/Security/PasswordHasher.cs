using System.Security.Cryptography;
using System.Text.Json;

namespace KidPcControl.Shared.Security;

public static class PasswordHasher
{
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 100_000;

    public static string Hash(string password)
    {
        password = Normalize(password);
        if (password.Length == 0)
            throw new ArgumentException("Password cannot be empty.", nameof(password));

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"pbkdf2${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    public static bool Verify(string password, string? stored)
    {
        password = Normalize(password);
        if (password.Length == 0 || string.IsNullOrWhiteSpace(stored))
            return false;

        var parts = stored.Trim().Split('$');
        if (parts.Length != 4 || !string.Equals(parts[0], "pbkdf2", StringComparison.Ordinal))
            return false;

        if (!int.TryParse(parts[1], out var iterations) || iterations < 1)
            return false;

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch
        {
            return false;
        }
    }

    public static string Normalize(string? password) => (password ?? string.Empty).Trim();
}

/// <summary>
/// Password lives in a dedicated file so Admin policy push can never wipe it.
/// </summary>
public static class AdminCredentials
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private sealed class CredFile
    {
        public string PasswordHash { get; set; } = string.Empty;
    }

    public static string Path =>
        System.IO.Path.Combine(AppConstants.ProgramDataDir, "admin-credentials.json");

    public static void SetPassword(string password)
    {
        Directory.CreateDirectory(AppConstants.ProgramDataDir);
        var hash = PasswordHasher.Hash(password);
        if (!PasswordHasher.Verify(password, hash))
            throw new InvalidOperationException("Password self-check failed after hash.");

        var json = JsonSerializer.Serialize(new CredFile { PasswordHash = hash }, JsonOptions);
        File.WriteAllText(Path, json);
        TryAllowUsersRead(Path);

        // Keep legacy field in policy in sync for older builds
        try
        {
            var policy = Storage.PolicyStore.LoadOrCreate();
            policy.AdminPasswordHash = hash;
            Storage.PolicyStore.Save(policy);
        }
        catch
        {
            // credentials file is source of truth
        }
    }

    private static void TryAllowUsersRead(string path)
    {
        if (!OperatingSystem.IsWindows())
            return;
        try
        {
            var info = new FileInfo(path);
            var acl = info.GetAccessControl();
            acl.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                new System.Security.Principal.SecurityIdentifier(
                    System.Security.Principal.WellKnownSidType.BuiltinUsersSid, null),
                System.Security.AccessControl.FileSystemRights.ReadAndExecute,
                System.Security.AccessControl.AccessControlType.Allow));
            info.SetAccessControl(acl);
        }
        catch
        {
            // best-effort: tray must still be able to verify password
        }
    }

    public static bool VerifyPassword(string password)
    {
        var hash = ReadHash();
        if (string.IsNullOrWhiteSpace(hash))
            return false;
        return PasswordHasher.Verify(password, hash);
    }

    public static bool HasPassword()
    {
        return !string.IsNullOrWhiteSpace(ReadHash());
    }

    public static string ReadHash()
    {
        try
        {
            if (File.Exists(Path))
            {
                var cred = JsonSerializer.Deserialize<CredFile>(File.ReadAllText(Path), JsonOptions);
                if (!string.IsNullOrWhiteSpace(cred?.PasswordHash))
                    return cred!.PasswordHash;
            }
        }
        catch { /* fall through */ }

        // Fallback: legacy policy field
        try
        {
            var policy = Storage.PolicyStore.LoadOrCreate();
            return policy.AdminPasswordHash ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
