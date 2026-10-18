using System.Runtime.InteropServices;

namespace StardewCodexChat.Knowledge;

/// <summary>Use macOS's bundled SQLite (including FTS5), avoiding a second native runtime.</summary>
internal sealed class KnowledgeDatabase : IDisposable
{
    private IntPtr db;
    internal KnowledgeDatabase(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (sqlite3_open_v2(path, out db, 6, IntPtr.Zero) != 0) { Dispose(); throw new IOException("SQLite open failed"); }
        try
        {
        sqlite3_busy_timeout(db, 1500);
        Execute("PRAGMA foreign_keys=ON");
        int version = int.Parse(Query("PRAGMA user_version")[0].Values.First());
        if (version > 2) throw new IOException("Knowledge schema is newer than this Mod");
        if (version == 0)
        {
            Execute("BEGIN IMMEDIATE");
            try
            {
                Execute("CREATE TABLE Pages(Id INTEGER PRIMARY KEY, CanonicalUrl TEXT UNIQUE NOT NULL, OriginalUrl TEXT, Title TEXT, SourceName TEXT, Domain TEXT, Language TEXT, ContentText TEXT, ContentHash TEXT, HttpStatus INTEGER, ETag TEXT, LastModified TEXT, FetchedAt INTEGER, LastCheckedAt INTEGER, ExpiresAt INTEGER, LastAccessedAt INTEGER, AccessCount INTEGER DEFAULT 0, CreatedAt INTEGER, UpdatedAt INTEGER)");
                Execute("CREATE TABLE Chunks(Id INTEGER PRIMARY KEY, PageId INTEGER REFERENCES Pages(Id) ON DELETE CASCADE, ChunkIndex INTEGER, HeadingPath TEXT, Content TEXT, ContentHash TEXT, CharacterCount INTEGER, CreatedAt INTEGER)");
                Execute("CREATE INDEX ChunksPage ON Chunks(PageId)");
                Execute("CREATE VIRTUAL TABLE ChunkSearch USING fts5(Title, Heading, Body, tokenize='unicode61')");
                Execute("CREATE TABLE SearchCache(Query TEXT PRIMARY KEY, Provider TEXT, Keywords TEXT, Urls TEXT, SearchedAt INTEGER, ExpiresAt INTEGER, Status TEXT)");
                Execute("PRAGMA user_version=1"); Execute("COMMIT");
            }
            catch { Execute("ROLLBACK"); throw; }
        }
        if(version<=1)
        {
            Execute("BEGIN IMMEDIATE");
            try { Execute("CREATE INDEX IF NOT EXISTS PagesExpiry ON Pages(ExpiresAt,LastAccessedAt)");
                Execute("CREATE INDEX IF NOT EXISTS SearchExpiry ON SearchCache(ExpiresAt)");Execute("PRAGMA user_version=2");Execute("COMMIT"); }
            catch {Execute("ROLLBACK");throw;}
        }
        // Explicit runtime check, not merely a compile-time assumption.
        Query("SELECT rowid FROM ChunkSearch WHERE ChunkSearch MATCH ? LIMIT 1", "ftsavailabilityprobe");
        } catch { Dispose(); throw; }
    }
    internal List<Dictionary<string,string>> Query(string sql, params object?[] args)
    {
        if (sqlite3_prepare_v2(db, sql, -1, out IntPtr statement, IntPtr.Zero) != 0) throw Error();
        try
        {
            for (int i=0;i<args.Length;i++)
                if (sqlite3_bind_text(statement,i+1,Convert.ToString(args[i],System.Globalization.CultureInfo.InvariantCulture)??"",-1,new IntPtr(-1))!=0) throw Error();
            var rows=new List<Dictionary<string,string>>();int step;
            while ((step=sqlite3_step(statement))==100)
            {
                var row=new Dictionary<string,string>();
                for(int i=0;i<sqlite3_column_count(statement);i++) row[Marshal.PtrToStringUTF8(sqlite3_column_name(statement,i))!]=Marshal.PtrToStringUTF8(sqlite3_column_text(statement,i))??"";
                rows.Add(row);
            }
            if(step!=101)throw Error();return rows;
        }
        finally { sqlite3_finalize(statement); }
    }
    internal void Execute(string sql, params object?[] args) => Query(sql,args);
    private IOException Error()=>new("SQLite: "+Marshal.PtrToStringUTF8(sqlite3_errmsg(db)));
    public void Dispose(){if(db!=IntPtr.Zero){sqlite3_close(db);db=IntPtr.Zero;}}
    private const string Lib="/usr/lib/libsqlite3.dylib";
    [DllImport(Lib)]private static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)]string file,out IntPtr db,int flags,IntPtr vfs);
    [DllImport(Lib)]private static extern int sqlite3_close(IntPtr db);
    [DllImport(Lib)]private static extern int sqlite3_busy_timeout(IntPtr db,int ms);
    [DllImport(Lib)]private static extern IntPtr sqlite3_errmsg(IntPtr db);
    [DllImport(Lib)]private static extern int sqlite3_prepare_v2(IntPtr db,[MarshalAs(UnmanagedType.LPUTF8Str)]string sql,int length,out IntPtr statement,IntPtr tail);
    [DllImport(Lib)]private static extern int sqlite3_bind_text(IntPtr s,int index,[MarshalAs(UnmanagedType.LPUTF8Str)]string value,int length,IntPtr destructor);
    [DllImport(Lib)]private static extern int sqlite3_step(IntPtr s);
    [DllImport(Lib)]private static extern int sqlite3_column_count(IntPtr s);
    [DllImport(Lib)]private static extern IntPtr sqlite3_column_text(IntPtr s,int i);
    [DllImport(Lib)]private static extern IntPtr sqlite3_column_name(IntPtr s,int i);
    [DllImport(Lib)]private static extern int sqlite3_finalize(IntPtr s);
}
