using Microsoft.Extensions.Caching.Memory;

namespace SampleHrms.Web.Services.Agent;

public sealed class AgentSessionStore : IAgentSessionStore
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(30);
    private readonly IMemoryCache _cache;
    private readonly object _sync = new();

    public AgentSessionStore(IMemoryCache cache) => _cache = cache;

    public AgentSession GetOrCreate(string sessionId, ActingUser actingUser, string userName)
    {
        var key = NormalizeId(sessionId);
        lock (_sync)
        {
            if (_cache.TryGetValue(key, out AgentSession? existing))
            {
                if (existing!.UserId != actingUser.UserId) throw new UnauthorizedAccessException("This assistant session belongs to another user.");
                existing.UpdateIdentity(actingUser, userName);
                Refresh(key, existing);
                return existing;
            }

            var session = new AgentSession(key, actingUser, userName);
            Refresh(key, session);
            return session;
        }
    }

    public AgentSession? Get(string sessionId, ActingUser actingUser)
    {
        if (!Guid.TryParse(sessionId, out var parsed)) return null;
        var key = parsed.ToString("D");
        lock (_sync)
        {
            if (!_cache.TryGetValue(key, out AgentSession? session) || session!.UserId != actingUser.UserId) return null;
            session.UpdateIdentity(actingUser, session.UserName);
            Refresh(key, session);
            return session;
        }
    }

    private void Refresh(string key, AgentSession session) =>
        _cache.Set(key, session, new MemoryCacheEntryOptions { SlidingExpiration = SessionLifetime });

    private static string NormalizeId(string sessionId)
    {
        if (!Guid.TryParse(sessionId, out var parsed)) throw new ArgumentException("Invalid assistant session id.", nameof(sessionId));
        return parsed.ToString("D");
    }
}
