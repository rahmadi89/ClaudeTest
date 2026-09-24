using Microsoft.Data.Sqlite;

namespace AtmMonitor.Agent.Storage;

public sealed record OutboxItem(long Id, string Kind, string Payload);

/// <summary>
/// Durable local state in a single SQLite file:
/// <list type="bullet">
/// <item><b>outbox</b> – messages that could not be delivered while the server was unreachable (FIFO, bounded).</item>
/// <item><b>processed_commands</b> – command ids already executed, so at-least-once redelivery never runs a command twice.</item>
/// <item><b>kv</b> – small settings such as log file offsets.</item>
/// </list>
/// </summary>
public sealed class LocalStore : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly Lock _gate = new();
    private readonly int _maxOutbox;

    public LocalStore(string path, int maxOutboxItems)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _maxOutbox = maxOutboxItems;
        _conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        _conn.Open();
        Exec("""
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=NORMAL;
            CREATE TABLE IF NOT EXISTS outbox (id INTEGER PRIMARY KEY AUTOINCREMENT, kind TEXT NOT NULL, payload TEXT NOT NULL, created_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS processed_commands (id TEXT PRIMARY KEY, processed_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS kv (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            """);
    }

    public void Enqueue(string kind, string payload)
    {
        lock (_gate)
        {
            Exec("INSERT INTO outbox (kind, payload, created_at) VALUES ($k, $p, $t)",
                ("$k", kind), ("$p", payload), ("$t", DateTimeOffset.UtcNow.ToString("O")));
            // Bound the buffer: drop the oldest items if the terminal has been offline for a very long time.
            Exec("DELETE FROM outbox WHERE id <= (SELECT MAX(id) FROM outbox) - $max", ("$max", _maxOutbox));
        }
    }

    public IReadOnlyList<OutboxItem> Peek(int max)
    {
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT id, kind, payload FROM outbox ORDER BY id LIMIT $n";
            cmd.Parameters.AddWithValue("$n", max);
            using var r = cmd.ExecuteReader();
            var list = new List<OutboxItem>();
            while (r.Read())
            {
                list.Add(new OutboxItem(r.GetInt64(0), r.GetString(1), r.GetString(2)));
            }

            return list;
        }
    }

    public void Remove(long id)
    {
        lock (_gate)
        {
            Exec("DELETE FROM outbox WHERE id = $id", ("$id", id));
        }
    }

    public int OutboxCount()
    {
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM outbox";
            return Convert.ToInt32(cmd.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Atomically records a command as processed. Returns <c>false</c> if it was already recorded.</summary>
    public bool TryMarkCommandProcessed(Guid commandId)
    {
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "INSERT OR IGNORE INTO processed_commands (id, processed_at) VALUES ($id, $t)";
            cmd.Parameters.AddWithValue("$id", commandId.ToString("N"));
            cmd.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.ToString("O"));
            var inserted = cmd.ExecuteNonQuery() == 1;
            Exec("DELETE FROM processed_commands WHERE processed_at < $cutoff", ("$cutoff", DateTimeOffset.UtcNow.AddDays(-30).ToString("O")));
            return inserted;
        }
    }

    public string? Get(string key)
    {
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT value FROM kv WHERE key = $k";
            cmd.Parameters.AddWithValue("$k", key);
            return cmd.ExecuteScalar() as string;
        }
    }

    public void Set(string key, string value)
    {
        lock (_gate)
        {
            Exec("INSERT INTO kv (key, value) VALUES ($k, $v) ON CONFLICT(key) DO UPDATE SET value = excluded.value", ("$k", key), ("$v", value));
        }
    }

    public void Dispose() => _conn.Dispose();

    private void Exec(string sql, params (string Name, object Value)[] args)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args)
        {
            cmd.Parameters.AddWithValue(name, value);
        }

        cmd.ExecuteNonQuery();
    }
}
