using System.Runtime.InteropServices;

namespace AimOdometer.Core.Storage;

/// <summary>
/// Minimal source-generated bindings to the native SQLite library (e_sqlite3.dll).
/// Only the handful of functions the app needs; this keeps the tracker NativeAOT-friendly.
/// </summary>
internal static unsafe partial class SqliteNative
{
    private const string Lib = "e_sqlite3";

    public const int Ok = 0;
    public const int Busy = 5;
    public const int Row = 100;
    public const int Done = 101;

    public const int OpenReadOnly = 0x00000001;
    public const int OpenReadWrite = 0x00000002;
    public const int OpenCreate = 0x00000004;
    public const int OpenNoMutex = 0x00008000;

    /// <summary>SQLITE_TRANSIENT: SQLite copies bound strings before the call returns.</summary>
    public static readonly nint Transient = -1;

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int sqlite3_open_v2(string filename, out nint db, int flags, nint vfs);

    [LibraryImport(Lib)]
    public static partial int sqlite3_close_v2(nint db);

    [LibraryImport(Lib)]
    public static partial int sqlite3_busy_timeout(nint db, int milliseconds);

    [LibraryImport(Lib)]
    public static partial nint sqlite3_errmsg(nint db);

    [LibraryImport(Lib)]
    public static partial int sqlite3_extended_errcode(nint db);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int sqlite3_exec(nint db, string sql, nint callback, nint arg, nint errmsg);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int sqlite3_prepare_v2(nint db, string sql, int byteCount, out nint stmt, nint tail);

    [LibraryImport(Lib)]
    public static partial int sqlite3_step(nint stmt);

    [LibraryImport(Lib)]
    public static partial int sqlite3_reset(nint stmt);

    [LibraryImport(Lib)]
    public static partial int sqlite3_clear_bindings(nint stmt);

    [LibraryImport(Lib)]
    public static partial int sqlite3_finalize(nint stmt);

    [LibraryImport(Lib)]
    public static partial int sqlite3_bind_int64(nint stmt, int index, long value);

    [LibraryImport(Lib)]
    public static partial int sqlite3_bind_double(nint stmt, int index, double value);

    [LibraryImport(Lib)]
    public static partial int sqlite3_bind_text(nint stmt, int index, byte* utf8, int byteCount, nint destructor);

    [LibraryImport(Lib)]
    public static partial int sqlite3_bind_null(nint stmt, int index);

    [LibraryImport(Lib)]
    public static partial long sqlite3_column_int64(nint stmt, int column);

    [LibraryImport(Lib)]
    public static partial double sqlite3_column_double(nint stmt, int column);

    [LibraryImport(Lib)]
    public static partial nint sqlite3_column_text(nint stmt, int column);

    [LibraryImport(Lib)]
    public static partial int sqlite3_column_bytes(nint stmt, int column);

    [LibraryImport(Lib)]
    public static partial int sqlite3_column_type(nint stmt, int column);

    [LibraryImport(Lib)]
    public static partial long sqlite3_last_insert_rowid(nint db);

    [LibraryImport(Lib)]
    public static partial int sqlite3_changes(nint db);
}
