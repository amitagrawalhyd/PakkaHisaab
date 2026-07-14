using PakkaHisaab.Maui.Helpers;
using PakkaHisaab.Maui.Models;
using PakkaHisaab.Maui.Services;
using SQLite;

namespace PakkaHisaab.Maui.Data;

public interface ILocalDatabase
{
    Task<SQLiteAsyncConnection> GetConnectionAsync();
    /// <summary>Swap to the isolated demo database (Demo_PakkaHisaab.db) or back.</summary>
    Task SwitchAsync(bool demo);
    Task WipeCurrentAsync();
}

/// <summary>
/// Offline-first SQLite store (sqlite-net-pcl, WAL mode). All writes land here first for
/// zero-latency UI; the Shiny job drains dirty rows to the API afterwards.
/// Demo mode points at a completely separate file so reviewer data never mixes with real data.
/// </summary>
public sealed class LocalDatabase : ILocalDatabase
{
    readonly ISessionService _session;
    readonly SemaphoreSlim _gate = new(1, 1);
    SQLiteAsyncConnection? _connection;
    string? _openPath;

    public LocalDatabase(ISessionService session) => _session = session;

    public async Task<SQLiteAsyncConnection> GetConnectionAsync()
    {
        var path = Path.Combine(FileSystem.AppDataDirectory,
            _session.IsDemo ? Constants.DemoDbName : Constants.MainDbName);

        if (_connection is not null && _openPath == path)
            return _connection;

        await _gate.WaitAsync();
        try
        {
            if (_connection is not null && _openPath == path)
                return _connection;

            if (_connection is not null)
                await _connection.CloseAsync();

            var conn = new SQLiteAsyncConnection(path,
                SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);
            await conn.EnableWriteAheadLoggingAsync();
            await conn.CreateTableAsync<LocalHelper>();
            await conn.CreateTableAsync<LocalAttendance>();
            await conn.CreateTableAsync<LocalLedgerEntry>();
            await conn.CreateTableAsync<LocalSettlement>();

            _connection = conn;
            _openPath = path;
            return conn;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SwitchAsync(bool demo)
    {
        await _gate.WaitAsync();
        try
        {
            if (_connection is not null)
            {
                await _connection.CloseAsync();
                _connection = null;
                _openPath = null;
            }
        }
        finally
        {
            _gate.Release();
        }
        _session.IsDemo = demo; // next GetConnectionAsync() opens the right file
    }

    public async Task WipeCurrentAsync()
    {
        var conn = await GetConnectionAsync();
        await conn.DeleteAllAsync<LocalHelper>();
        await conn.DeleteAllAsync<LocalAttendance>();
        await conn.DeleteAllAsync<LocalLedgerEntry>();
        await conn.DeleteAllAsync<LocalSettlement>();
    }
}
