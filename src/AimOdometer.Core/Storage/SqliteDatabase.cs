using System.Runtime.InteropServices;
using System.Text;

namespace AimOdometer.Core.Storage;

/// <summary>Thrown when a SQLite call fails.</summary>
public sealed class SqliteException(string message, int errorCode) : Exception(message)
{
    public int ErrorCode { get; } = errorCode;
}

/// <summary>
/// One SQLite connection. Not thread-safe: use it from a single thread
/// (the tracker uses it only on its message-loop thread).
/// </summary>
public sealed class SqliteDatabase : IDisposable
{
    private nint _db;

    private SqliteDatabase(nint db) => _db = db;

    /// <summary>Opens (and creates if needed) a database file in WAL mode.</summary>
    public static SqliteDatabase Open(string path, bool readOnly = false)
    {
        var flags = readOnly
            ? SqliteNative.OpenReadOnly | SqliteNative.OpenNoMutex
            : SqliteNative.OpenReadWrite | SqliteNative.OpenCreate | SqliteNative.OpenNoMutex;

        var rc = SqliteNative.sqlite3_open_v2(path, out var db, flags, 0);
        if (rc != SqliteNative.Ok)
        {
            var message = db != 0 ? Marshal.PtrToStringUTF8(SqliteNative.sqlite3_errmsg(db)) : null;
            _ = SqliteNative.sqlite3_close_v2(db);
            throw new SqliteException($"Cannot open database '{path}': {message ?? rc.ToString(System.Globalization.CultureInfo.InvariantCulture)}", rc);
        }

        var database = new SqliteDatabase(db);
        _ = SqliteNative.sqlite3_busy_timeout(db, 2000);
        if (!readOnly)
        {
            database.Execute("PRAGMA journal_mode=WAL;");
            database.Execute("PRAGMA synchronous=NORMAL;");
        }

        // Small page cache: the tracker has a 15 MB memory budget.
        database.Execute("PRAGMA cache_size=-512;");
        database.Execute("PRAGMA foreign_keys=ON;");
        return database;
    }

    internal nint Handle => _db != 0 ? _db : throw new ObjectDisposedException(nameof(SqliteDatabase));

    public void Execute(string sql)
    {
        var rc = SqliteNative.sqlite3_exec(Handle, sql, 0, 0, 0);
        Check(rc, sql);
    }

    public SqliteStatement Prepare(string sql)
    {
        var rc = SqliteNative.sqlite3_prepare_v2(Handle, sql, -1, out var stmt, 0);
        Check(rc, sql);
        return new SqliteStatement(this, stmt);
    }

    /// <summary>Runs a statement that returns at most one integer (e.g. a PRAGMA or COUNT).</summary>
    public long ScalarInt64(string sql)
    {
        using var statement = Prepare(sql);
        return statement.Step() ? statement.GetInt64(0) : 0;
    }

    public long LastInsertRowId => SqliteNative.sqlite3_last_insert_rowid(Handle);

    public void BeginTransaction() => Execute("BEGIN IMMEDIATE;");

    public void Commit() => Execute("COMMIT;");

    public void Rollback() => Execute("ROLLBACK;");

    internal void Check(int rc, string context)
    {
        if (rc is SqliteNative.Ok or SqliteNative.Row or SqliteNative.Done)
        {
            return;
        }

        var message = Marshal.PtrToStringUTF8(SqliteNative.sqlite3_errmsg(_db)) ?? "unknown error";
        throw new SqliteException($"SQLite error {SqliteNative.sqlite3_extended_errcode(_db)}: {message} ({context})", rc);
    }

    public void Dispose()
    {
        if (_db != 0)
        {
            _ = SqliteNative.sqlite3_close_v2(_db);
            _db = 0;
        }
    }
}

/// <summary>A prepared statement. Reuse it with <see cref="Reset"/> to avoid re-parsing SQL.</summary>
public sealed unsafe class SqliteStatement : IDisposable
{
    private readonly SqliteDatabase _database;
    private nint _stmt;

    internal SqliteStatement(SqliteDatabase database, nint stmt)
    {
        _database = database;
        _stmt = stmt;
    }

    private nint Handle => _stmt != 0 ? _stmt : throw new ObjectDisposedException(nameof(SqliteStatement));

    /// <summary>Binds by 1-based parameter index.</summary>
    public SqliteStatement Bind(int index, long value)
    {
        _database.Check(SqliteNative.sqlite3_bind_int64(Handle, index, value), "bind");
        return this;
    }

    public SqliteStatement Bind(int index, double value)
    {
        _database.Check(SqliteNative.sqlite3_bind_double(Handle, index, value), "bind");
        return this;
    }

    public SqliteStatement Bind(int index, string? value)
    {
        if (value is null)
        {
            _database.Check(SqliteNative.sqlite3_bind_null(Handle, index), "bind");
            return this;
        }

        var byteCount = Encoding.UTF8.GetByteCount(value);

        // One spare byte so an empty string still has a non-null pointer (null would bind SQL NULL).
        Span<byte> buffer = byteCount < 512 ? stackalloc byte[byteCount + 1] : new byte[byteCount + 1];
        Encoding.UTF8.GetBytes(value, buffer);
        fixed (byte* p = buffer)
        {
            _database.Check(SqliteNative.sqlite3_bind_text(Handle, index, p, byteCount, SqliteNative.Transient), "bind");
        }

        return this;
    }

    /// <summary>Advances the statement. Returns true while a row is available.</summary>
    public bool Step()
    {
        var rc = SqliteNative.sqlite3_step(Handle);
        if (rc == SqliteNative.Row)
        {
            return true;
        }

        _database.Check(rc, "step");
        return false;
    }

    /// <summary>Executes a statement that returns no rows.</summary>
    public void Run()
    {
        while (Step())
        {
        }
    }

    public void Reset()
    {
        // sqlite3_reset repeats the error of the last step, which Step() already reported.
        _ = SqliteNative.sqlite3_reset(Handle);
        _ = SqliteNative.sqlite3_clear_bindings(Handle);
    }

    public long GetInt64(int column) => SqliteNative.sqlite3_column_int64(Handle, column);

    public double GetDouble(int column) => SqliteNative.sqlite3_column_double(Handle, column);

    public bool IsNull(int column) => SqliteNative.sqlite3_column_type(Handle, column) == 5;

    public string? GetString(int column)
    {
        var p = SqliteNative.sqlite3_column_text(Handle, column);
        return p == 0 ? null : Marshal.PtrToStringUTF8(p, SqliteNative.sqlite3_column_bytes(Handle, column));
    }

    public void Dispose()
    {
        if (_stmt != 0)
        {
            _ = SqliteNative.sqlite3_finalize(_stmt);
            _stmt = 0;
        }
    }
}
