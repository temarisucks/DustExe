using Dust.OnlineServer.Configuration;
using Dust.OnlineServer.Lobbies;
using Dust.OnlineServer.Networking;
using Dust.OnlineServer.Security;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

var platformPortText = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(platformPortText))
{
    if (!int.TryParse(platformPortText, out var platformPort) ||
        platformPort is < 1 or > 65535)
    {
        throw new InvalidOperationException(
            $"PORT must be a number from 1 through 65535, but was '{platformPortText}'.");
    }

    // Railway and Cloud Run terminate TLS at their edge and inject the port
    // the container must bind. Local runs continue to use appsettings.json.
    builder.WebHost.UseUrls($"http://0.0.0.0:{platformPort}");
}

builder.Services.Configure<OnlineServerOptions>(
    builder.Configuration.GetSection("OnlineServer"));
builder.Services.AddSingleton<AccountStore>();
builder.Services.AddSingleton<ConnectionHub>();
builder.Services.AddSingleton<LobbyManager>();

var app = builder.Build();
var startedAtUtc = DateTimeOffset.UtcNow;

var accountStore = app.Services.GetRequiredService<AccountStore>();
await accountStore.InitializeAsync(CancellationToken.None);

app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(20)
});

app.MapGet("/", () => Results.Ok(new
{
    service = "Dust Online Server",
    protocol = 1,
    websocket = "/ws",
    health = "/health"
}));

app.MapGet("/health", (ConnectionHub connections, LobbyManager lobbies) =>
    Results.Ok(new
    {
        status = "ok",
        protocol = 1,
        startedAtUtc,
        uptimeSeconds = (long)(DateTimeOffset.UtcNow - startedAtUtc).TotalSeconds,
        connections = connections.ConnectedCount,
        lobbies = lobbies.LobbyCount,
        openLobbies = lobbies.OpenLobbyCount
    }));

app.Map("/ws", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new
        {
            error = "A WebSocket upgrade is required."
        });
        return;
    }

    var options = context.RequestServices
        .GetRequiredService<IOptions<OnlineServerOptions>>()
        .Value;
    var origin = context.Request.Headers.Origin.ToString();
    if (options.AllowedOrigins.Length > 0 &&
        origin.Length > 0 &&
        !options.AllowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }

    var socket = await context.WebSockets.AcceptWebSocketAsync();
    var session = ActivatorUtilities.CreateInstance<ClientSession>(
        context.RequestServices,
        socket);
    await session.RunAsync(context.RequestAborted);
});

await app.RunAsync();
