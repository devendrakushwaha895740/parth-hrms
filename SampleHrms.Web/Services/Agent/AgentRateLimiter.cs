using System.Collections.Concurrent;

namespace SampleHrms.Web.Services.Agent;

public sealed class AgentRateLimiter
{
    private const int MessageLimit = 20;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private readonly ConcurrentDictionary<string, UserWindow> _windows = new();
    private readonly TimeProvider _clock;

    public AgentRateLimiter(TimeProvider clock) => _clock = clock;

    public bool TryConsume(string userId)
    {
        var now = _clock.GetUtcNow();
        var window = _windows.GetOrAdd(userId, _ => new UserWindow(now));
        lock (window)
        {
            if (now - window.StartedAt >= Window)
            {
                window.StartedAt = now;
                window.Count = 0;
            }
            if (window.Count >= MessageLimit) return false;
            window.Count++;
            return true;
        }
    }

    private sealed class UserWindow(DateTimeOffset startedAt)
    {
        public DateTimeOffset StartedAt { get; set; } = startedAt;
        public int Count { get; set; }
    }
}
