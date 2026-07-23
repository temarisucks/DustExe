using System.Collections.Concurrent;
using System.Security.Cryptography;
using Dust.OnlineServer.Configuration;
using Dust.OnlineServer.Security;
using Microsoft.Extensions.Options;

namespace Dust.OnlineServer.Networking;

internal sealed record AuthenticationResult(
    AccountIdentity Identity,
    string ResumeToken,
    DateTimeOffset ExpiresAtUtc,
    ClientSession? ReplacedSession = null);

internal sealed class ConnectionHub
{
    private readonly object _authGate = new();
    private readonly ConcurrentDictionary<Guid, ClientSession> _connections = new();
    private readonly Dictionary<Guid, ClientSession> _activePlayers = [];
    private readonly Dictionary<string, ResumeTicket> _tickets =
        new(StringComparer.Ordinal);
    private readonly TimeSpan _tokenLifetime;

    public ConnectionHub(IOptions<OnlineServerOptions> options)
    {
        _tokenLifetime = TimeSpan.FromHours(
            Math.Clamp(options.Value.ResumeTokenHours, 1, 24 * 30));
    }

    public int ConnectedCount => _connections.Count;

    public void Register(ClientSession session) =>
        _connections[session.ConnectionId] = session;

    public void Unregister(ClientSession session)
    {
        _connections.TryRemove(session.ConnectionId, out _);

        var identity = session.Identity;
        if (identity is null)
            return;

        lock (_authGate)
        {
            if (_activePlayers.TryGetValue(identity.PlayerId, out var active) &&
                ReferenceEquals(active, session))
            {
                _activePlayers.Remove(identity.PlayerId);
            }
        }
    }

    public AuthenticationResult Authenticate(
        AccountIdentity identity,
        ClientSession session)
    {
        lock (_authGate)
        {
            ThrowIfAlreadyAuthenticated(session);
            RemoveExpiredTicketsLocked();

            if (_activePlayers.ContainsKey(identity.PlayerId))
            {
                throw new Protocol.ProtocolException(
                    "ALREADY_ONLINE",
                    "That account is already connected.");
            }

            foreach (var token in _tickets
                         .Where(pair => pair.Value.Identity.PlayerId == identity.PlayerId)
                         .Select(pair => pair.Key)
                         .ToArray())
            {
                _tickets.Remove(token);
            }

            var result = IssueTicketLocked(identity);
            _activePlayers[identity.PlayerId] = session;
            session.SetIdentity(identity);
            return result;
        }
    }

    public AuthenticationResult Resume(string token, ClientSession session)
    {
        lock (_authGate)
        {
            ThrowIfAlreadyAuthenticated(session);
            RemoveExpiredTicketsLocked();

            if (!_tickets.TryGetValue(token, out var ticket))
            {
                throw new Protocol.ProtocolException(
                    "INVALID_RESUME_TOKEN",
                    "That resume token is invalid or has expired.");
            }

            _activePlayers.TryGetValue(ticket.Identity.PlayerId, out var replaced);
            _tickets.Remove(token);
            var replacementTicket = IssueTicketLocked(ticket.Identity);
            _activePlayers[ticket.Identity.PlayerId] = session;
            session.SetIdentity(ticket.Identity);
            return new AuthenticationResult(
                ticket.Identity,
                replacementTicket.ResumeToken,
                replacementTicket.ExpiresAtUtc,
                replaced);
        }
    }

    public IReadOnlyList<ClientSession> AuthenticatedConnections() =>
        _connections.Values.Where(connection => connection.Identity is not null).ToArray();

    public bool IsCurrentSession(Guid playerId, ClientSession session)
    {
        lock (_authGate)
        {
            return _activePlayers.TryGetValue(playerId, out var active) &&
                   ReferenceEquals(active, session);
        }
    }

    private AuthenticationResult IssueTicketLocked(AccountIdentity identity)
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        var expiration = DateTimeOffset.UtcNow + _tokenLifetime;
        _tickets[token] = new ResumeTicket(identity, expiration);
        return new AuthenticationResult(identity, token, expiration);
    }

    private void RemoveExpiredTicketsLocked()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var token in _tickets
                     .Where(pair => pair.Value.ExpiresAtUtc <= now)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            _tickets.Remove(token);
        }
    }

    private static void ThrowIfAlreadyAuthenticated(ClientSession session)
    {
        if (session.Identity is not null)
        {
            throw new Protocol.ProtocolException(
                "ALREADY_AUTHENTICATED",
                "This connection is already authenticated.");
        }
    }

    private sealed class ResumeTicket(
        AccountIdentity identity,
        DateTimeOffset expiresAtUtc)
    {
        public AccountIdentity Identity { get; } = identity;
        public DateTimeOffset ExpiresAtUtc { get; set; } = expiresAtUtc;
    }
}
