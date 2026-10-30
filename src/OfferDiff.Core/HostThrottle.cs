namespace OfferDiff.Core;

// At most one request per minDelay per host; different hosts never wait for each other.
// now and sleep are injectable so tests run on a fake clock without really waiting.
// shortcut: limits per host name, not per IP or site ("www.a.test" and "a.test" count separately), and redirects
// inside one request are not throttled; switch to per-IP if sites share servers and complain
// shortcut: not thread-safe, which matches the sequential HttpPageSource; add a lock when fetching in parallel
public class HostThrottle(TimeSpan minDelay, Func<DateTimeOffset>? now = null, Action<TimeSpan>? sleep = null)
{
    readonly Func<DateTimeOffset> now = now ?? (() => DateTimeOffset.UtcNow);
    readonly Action<TimeSpan> sleep = sleep ?? Thread.Sleep;
    readonly Dictionary<string, DateTimeOffset> lastRequest = new(StringComparer.OrdinalIgnoreCase);

    public DateTimeOffset Now => now();

    public void Sleep(TimeSpan duration) => sleep(duration);

    public void Wait(string host)
    {
        if (lastRequest.TryGetValue(host, out var previous))
        {
            var wait = previous + minDelay - now();
            if (wait > TimeSpan.Zero) sleep(wait);
        }
        lastRequest[host] = now();
    }
}
