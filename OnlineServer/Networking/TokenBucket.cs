namespace Dust.OnlineServer.Networking;

internal sealed class TokenBucket
{
    private readonly object _gate = new();
    private readonly double _tokensPerSecond;
    private readonly double _capacity;
    private double _tokens;
    private long _lastTick = Environment.TickCount64;

    public TokenBucket(int tokensPerSecond, double burstSeconds = 1.25)
    {
        _tokensPerSecond = Math.Max(1, tokensPerSecond);
        _capacity = Math.Max(1, _tokensPerSecond * burstSeconds);
        _tokens = _capacity;
    }

    public bool TryTake()
    {
        lock (_gate)
        {
            var now = Environment.TickCount64;
            var elapsedSeconds = Math.Max(0, now - _lastTick) / 1000d;
            _lastTick = now;
            _tokens = Math.Min(
                _capacity,
                _tokens + elapsedSeconds * _tokensPerSecond);

            if (_tokens < 1)
                return false;

            _tokens -= 1;
            return true;
        }
    }
}
