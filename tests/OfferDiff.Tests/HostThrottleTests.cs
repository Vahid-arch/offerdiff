using OfferDiff.Core;

namespace OfferDiff.Tests;

// Time only moves when the code "sleeps", and every sleep is recorded instead of really waiting.
class FakeClock
{
    public DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    public readonly List<TimeSpan> Sleeps = [];

    public void Sleep(TimeSpan duration)
    {
        Sleeps.Add(duration);
        Now += duration;
    }

    public HostThrottle Throttle() => new(TimeSpan.FromSeconds(1), () => Now, Sleep);
}

public class HostThrottleTests
{
    readonly FakeClock clock = new();

    [Fact]
    public void Second_request_to_same_host_waits_the_rest_of_the_second()
    {
        var throttle = clock.Throttle();

        throttle.Wait("example-store.test");
        clock.Now += TimeSpan.FromMilliseconds(300);
        throttle.Wait("example-store.test");

        Assert.Equal([TimeSpan.FromMilliseconds(700)], clock.Sleeps);
    }

    [Fact]
    public void Back_to_back_requests_wait_one_second()
    {
        var throttle = clock.Throttle();

        throttle.Wait("example-store.test");
        throttle.Wait("example-store.test");

        Assert.Equal([TimeSpan.FromSeconds(1)], clock.Sleeps);
    }

    [Fact]
    public void Different_hosts_do_not_wait()
    {
        var throttle = clock.Throttle();

        throttle.Wait("example-store.test");
        throttle.Wait("other-store.test");

        Assert.Empty(clock.Sleeps);
    }

    [Fact]
    public void Host_names_differing_only_in_case_share_the_limit()
    {
        var throttle = clock.Throttle();

        throttle.Wait("Example-Store.TEST");
        throttle.Wait("example-store.test");

        Assert.Equal([TimeSpan.FromSeconds(1)], clock.Sleeps);
    }

    [Fact]
    public void No_wait_after_the_interval_has_passed()
    {
        var throttle = clock.Throttle();

        throttle.Wait("example-store.test");
        clock.Now += TimeSpan.FromSeconds(1);
        throttle.Wait("example-store.test");

        Assert.Empty(clock.Sleeps);
    }
}
