using AtmMonitor.Agent.Connectivity;
using AtmMonitor.Agent.Storage;

namespace AtmMonitor.Agent.Tests;

public class LocalStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"atm-store-{Guid.NewGuid():N}.db");

    [Fact]
    public void Outbox_is_fifo_and_survives_reopen()
    {
        using (var store = new LocalStore(_path, 100))
        {
            store.Enqueue("a", "1");
            store.Enqueue("b", "2");
        }

        using var reopened = new LocalStore(_path, 100);
        var items = reopened.Peek(10);
        Assert.Equal(["1", "2"], items.Select(i => i.Payload));
        reopened.Remove(items[0].Id);
        Assert.Equal("2", Assert.Single(reopened.Peek(10)).Payload);
    }

    [Fact]
    public void Outbox_is_bounded_dropping_oldest()
    {
        using var store = new LocalStore(_path, 100);
        for (var i = 0; i < 150; i++)
        {
            store.Enqueue("k", i.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        Assert.Equal(100, store.OutboxCount());
        Assert.Equal("50", store.Peek(1)[0].Payload);
    }

    [Fact]
    public void Command_ids_are_recorded_once()
    {
        using var store = new LocalStore(_path, 100);
        var id = Guid.NewGuid();
        Assert.True(store.TryMarkCommandProcessed(id));
        Assert.False(store.TryMarkCommandProcessed(id));
    }

    [Fact]
    public void Reconnect_backoff_is_jittered_and_capped()
    {
        for (var attempt = 1; attempt < 50; attempt++)
        {
            var delay = ServerConnection.Backoff(attempt);
            Assert.InRange(delay.TotalSeconds, 1, 60);
        }
    }

    public void Dispose()
    {
        foreach (var f in Directory.GetFiles(Path.GetDirectoryName(_path)!, Path.GetFileName(_path) + "*"))
        {
            File.Delete(f);
        }

        GC.SuppressFinalize(this);
    }
}
