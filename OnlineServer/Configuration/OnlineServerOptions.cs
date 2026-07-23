namespace Dust.OnlineServer.Configuration;

internal sealed class OnlineServerOptions
{
    public string AccountFile { get; set; } = "Data/accounts.json";
    public int PasswordHashIterations { get; set; } = 210_000;
    public int ResumeTokenHours { get; set; } = 24;
    public int DisconnectGraceSeconds { get; set; } = 15;
    public int MaxWebSocketMessageBytes { get; set; } = 80 * 1024;
    public int MaxSnapshotPayloadBytes { get; set; } = 64 * 1024;
    public int MaxInputPayloadBytes { get; set; } = 16 * 1024;
    public int InputMessagesPerSecond { get; set; } = 35;
    public int SnapshotMessagesPerSecond { get; set; } = 15;
    public int PeerSendTimeoutSeconds { get; set; } = 3;
    public string[] AllowedOrigins { get; set; } = [];
}
