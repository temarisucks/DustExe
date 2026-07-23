using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dust.OnlineServer.Configuration;
using Microsoft.Extensions.Options;

namespace Dust.OnlineServer.Security;

internal sealed record AccountIdentity(Guid PlayerId, string Username);

internal sealed partial class AccountStore
{
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path;
    private readonly int _iterations;
    private readonly ILogger<AccountStore> _logger;
    private AccountDatabase _database = new();

    public AccountStore(
        IOptions<OnlineServerOptions> options,
        IWebHostEnvironment environment,
        ILogger<AccountStore> logger)
    {
        _logger = logger;
        _iterations = Math.Max(100_000, options.Value.PasswordHashIterations);
        _path = Path.GetFullPath(
            Path.IsPathRooted(options.Value.AccountFile)
                ? options.Value.AccountFile
                : Path.Combine(environment.ContentRootPath, options.Value.AccountFile));
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            if (!File.Exists(_path))
            {
                await PersistLockedAsync(cancellationToken);
                return;
            }

            await using var stream = new FileStream(
                _path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            _database = await JsonSerializer.DeserializeAsync<AccountDatabase>(
                    stream,
                    cancellationToken: cancellationToken)
                ?? new AccountDatabase();

            _database.Accounts ??= [];
            _logger.LogInformation(
                "Loaded {AccountCount} account records from {AccountFile}.",
                _database.Accounts.Count,
                _path);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"The account database at '{_path}' is not valid JSON. " +
                "It was not overwritten; restore it from backup or repair it.",
                exception);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AccountIdentity> SignupAsync(
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        ValidateCredentials(username, password);
        var normalized = username.ToUpperInvariant();

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_database.Accounts.Any(
                    account => account.NormalizedUsername.Equals(
                        normalized,
                        StringComparison.Ordinal)))
            {
                throw new Protocol.ProtocolException(
                    "USERNAME_TAKEN",
                    "That username is already registered.");
            }

            var salt = RandomNumberGenerator.GetBytes(SaltBytes);
            var hash = HashPassword(password, salt, _iterations);
            var record = new AccountRecord
            {
                PlayerId = Guid.NewGuid(),
                Username = username,
                NormalizedUsername = normalized,
                Salt = Convert.ToBase64String(salt),
                PasswordHash = Convert.ToBase64String(hash),
                Iterations = _iterations,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };

            _database.Accounts.Add(record);
            try
            {
                await PersistLockedAsync(cancellationToken);
            }
            catch
            {
                _database.Accounts.Remove(record);
                throw;
            }

            return new AccountIdentity(record.PlayerId, record.Username);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AccountIdentity> LoginAsync(
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        // Match the signup limits before doing any expensive hashing.
        ValidateCredentials(username, password);
        var normalized = username.ToUpperInvariant();

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var record = _database.Accounts.FirstOrDefault(
                account => account.NormalizedUsername.Equals(
                    normalized,
                    StringComparison.Ordinal));

            // Perform a dummy hash for unknown users so the common failure paths
            // have comparable cost.
            var salt = record is null
                ? new byte[SaltBytes]
                : Convert.FromBase64String(record.Salt);
            var iterations = record?.Iterations ?? _iterations;
            var actual = HashPassword(password, salt, iterations);
            var expected = record is null
                ? new byte[HashBytes]
                : Convert.FromBase64String(record.PasswordHash);

            if (record is null ||
                actual.Length != expected.Length ||
                !CryptographicOperations.FixedTimeEquals(actual, expected))
            {
                throw new Protocol.ProtocolException(
                    "INVALID_CREDENTIALS",
                    "The username or password is incorrect.");
            }

            return new AccountIdentity(record.PlayerId, record.Username);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static void ValidateCredentials(string username, string password)
    {
        if (!UsernamePattern().IsMatch(username))
        {
            throw new Protocol.ProtocolException(
                "INVALID_USERNAME",
                "Usernames must be 3-20 characters using letters, numbers, '_' or '-'.");
        }

        if (password.Length is < 8 or > 128)
        {
            throw new Protocol.ProtocolException(
                "INVALID_PASSWORD",
                "Passwords must contain 8-128 characters.");
        }
    }

    private static byte[] HashPassword(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            HashBytes);

    private async Task PersistLockedAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("Account file has no parent directory.");
        var temporary = Path.Combine(
            directory,
            $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                             temporary,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             16 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    _database,
                    cancellationToken: cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(_path))
            {
                try
                {
                    File.Replace(temporary, _path, destinationBackupFileName: null);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Move(temporary, _path, overwrite: true);
                }
            }
            else
            {
                File.Move(temporary, _path);
            }
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{3,20}$", RegexOptions.CultureInvariant)]
    private static partial Regex UsernamePattern();

    private sealed class AccountDatabase
    {
        public int Version { get; set; } = 1;
        public List<AccountRecord> Accounts { get; set; } = [];
    }

    private sealed class AccountRecord
    {
        public Guid PlayerId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string NormalizedUsername { get; set; } = string.Empty;
        public string Salt { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public int Iterations { get; set; }
        public DateTimeOffset CreatedAtUtc { get; set; }
    }
}
