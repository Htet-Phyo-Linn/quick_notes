using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using QuickNote.Core.Models;

namespace QuickNote.Infrastructure.FileSystem
{
    /// <summary>
    /// SQLite and FTS5 powered note metadata index and search service.
    /// Replaces the monolithic notes_index.json to provide sub-millisecond
    /// atomic writes and tokenized full-text search.
    /// </summary>
    public class SqliteNoteIndexService : IDisposable
    {
        private readonly string _connectionString;
        private readonly object _lock = new();

        public SqliteNoteIndexService(string notesRoot)
        {
            var dbPath = Path.Combine(notesRoot, ".quicknote_index.db");
            _connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared
            }.ToString();

            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            lock (_lock)
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var walCmd = connection.CreateCommand();
                walCmd.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;";
                walCmd.ExecuteNonQuery();

                using var tableCmd = connection.CreateCommand();
                tableCmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS notes (
                        id TEXT PRIMARY KEY,
                        relative_path TEXT NOT NULL,
                        title TEXT,
                        snippet TEXT,
                        render_text TEXT,
                        content TEXT,
                        tags TEXT,
                        headings TEXT,
                        last_modified TEXT,
                        file_size INTEGER,
                        last_file_write_utc TEXT,
                        content_hash TEXT,
                        is_disk_synced INTEGER
                    );

                    CREATE INDEX IF NOT EXISTS idx_notes_rel ON notes(relative_path);

                    CREATE VIRTUAL TABLE IF NOT EXISTS notes_fts USING fts5(
                        id UNINDEXED,
                        title,
                        content,
                        tags,
                        tokenize='unicode61'
                    );
                ";
                tableCmd.ExecuteNonQuery();
            }
        }

        public void UpsertNote(NoteIndexEntry entry, string? content = null)
        {
            if (entry == null) return;
            var fullContent = !string.IsNullOrEmpty(content) ? content : (entry.Content ?? string.Empty);

            lock (_lock)
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();
                using var tx = connection.BeginTransaction();

                using var cmd = connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    INSERT INTO notes (
                        id, relative_path, title, snippet, render_text, content, tags, headings,
                        last_modified, file_size, last_file_write_utc, content_hash, is_disk_synced
                    ) VALUES (
                        @id, @relative_path, @title, @snippet, @render_text, @content, @tags, @headings,
                        @last_modified, @file_size, @last_file_write_utc, @content_hash, @is_disk_synced
                    )
                    ON CONFLICT(id) DO UPDATE SET
                        relative_path = excluded.relative_path,
                        title = excluded.title,
                        snippet = excluded.snippet,
                        render_text = excluded.render_text,
                        content = excluded.content,
                        tags = excluded.tags,
                        headings = excluded.headings,
                        last_modified = excluded.last_modified,
                        file_size = excluded.file_size,
                        last_file_write_utc = excluded.last_file_write_utc,
                        content_hash = excluded.content_hash,
                        is_disk_synced = excluded.is_disk_synced;
                ";

                cmd.Parameters.AddWithValue("@id", entry.Id.ToString("D"));
                cmd.Parameters.AddWithValue("@relative_path", entry.RelativePath ?? string.Empty);
                cmd.Parameters.AddWithValue("@title", entry.Title ?? string.Empty);
                cmd.Parameters.AddWithValue("@snippet", entry.Snippet ?? string.Empty);
                cmd.Parameters.AddWithValue("@render_text", entry.RenderText ?? string.Empty);
                cmd.Parameters.AddWithValue("@content", fullContent);
                cmd.Parameters.AddWithValue("@tags", JsonSerializer.Serialize(entry.Tags ?? Array.Empty<string>()));
                cmd.Parameters.AddWithValue("@headings", JsonSerializer.Serialize(entry.Headings ?? Array.Empty<string>()));
                cmd.Parameters.AddWithValue("@last_modified", entry.LastModified.ToString("o"));
                cmd.Parameters.AddWithValue("@file_size", entry.FileSize);
                cmd.Parameters.AddWithValue("@last_file_write_utc", entry.LastFileWriteUtc.ToString("o"));
                cmd.Parameters.AddWithValue("@content_hash", entry.ContentHash ?? string.Empty);
                cmd.Parameters.AddWithValue("@is_disk_synced", entry.IsDiskSynced ? 1 : 0);
                cmd.ExecuteNonQuery();

                // Update FTS
                using var ftsDel = connection.CreateCommand();
                ftsDel.Transaction = tx;
                ftsDel.CommandText = "DELETE FROM notes_fts WHERE id = @id;";
                ftsDel.Parameters.AddWithValue("@id", entry.Id.ToString("D"));
                ftsDel.ExecuteNonQuery();

                using var ftsIns = connection.CreateCommand();
                ftsIns.Transaction = tx;
                ftsIns.CommandText = "INSERT INTO notes_fts(id, title, content, tags) VALUES (@id, @title, @content, @tags);";
                ftsIns.Parameters.AddWithValue("@id", entry.Id.ToString("D"));
                ftsIns.Parameters.AddWithValue("@title", entry.Title ?? string.Empty);
                ftsIns.Parameters.AddWithValue("@content", fullContent);
                ftsIns.Parameters.AddWithValue("@tags", string.Join(" ", entry.Tags ?? Array.Empty<string>()));
                ftsIns.ExecuteNonQuery();

                tx.Commit();
            }
        }

        public void DeleteNote(Guid id)
        {
            var idStr = id.ToString("D");
            lock (_lock)
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();
                using var tx = connection.BeginTransaction();

                using var cmd = connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM notes WHERE id = @id;";
                cmd.Parameters.AddWithValue("@id", idStr);
                cmd.ExecuteNonQuery();

                using var ftsDel = connection.CreateCommand();
                ftsDel.Transaction = tx;
                ftsDel.CommandText = "DELETE FROM notes_fts WHERE id = @id;";
                ftsDel.Parameters.AddWithValue("@id", idStr);
                ftsDel.ExecuteNonQuery();

                tx.Commit();
            }
        }

        public NoteIndexEntry? GetNoteById(Guid id)
        {
            lock (_lock)
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT * FROM notes WHERE id = @id LIMIT 1;";
                cmd.Parameters.AddWithValue("@id", id.ToString("D"));

                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    return ReadEntryFromReader(reader);
                }
                return null;
            }
        }

        public NoteIndexEntry? GetNoteByPath(string relativePath)
        {
            var normalized = relativePath.Replace('\\', '/').TrimStart('/');
            lock (_lock)
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT * FROM notes WHERE relative_path = @rel OR relative_path = @norm LIMIT 1;";
                cmd.Parameters.AddWithValue("@rel", relativePath);
                cmd.Parameters.AddWithValue("@norm", normalized);

                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    return ReadEntryFromReader(reader);
                }
                return null;
            }
        }

        public List<NoteIndexEntry> GetAllNotes()
        {
            var list = new List<NoteIndexEntry>();
            lock (_lock)
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT * FROM notes ORDER BY last_modified DESC;";

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(ReadEntryFromReader(reader));
                }
            }
            return list;
        }

        public List<Guid> SearchFts(string query, int limit = 50)
        {
            var results = new List<Guid>();
            if (string.IsNullOrWhiteSpace(query)) return results;

            // Prepare safe FTS5 query terms
            var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                             .Select(t => t.Trim('"', '*', ':', '-', '^'))
                             .Where(t => !string.IsNullOrEmpty(t))
                             .Select(t => $"{t}*");

            var ftsQuery = string.Join(" ", terms);
            if (string.IsNullOrWhiteSpace(ftsQuery)) return results;

            lock (_lock)
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT id FROM notes_fts WHERE notes_fts MATCH @query LIMIT @limit;";
                cmd.Parameters.AddWithValue("@query", ftsQuery);
                cmd.Parameters.AddWithValue("@limit", limit);

                try
                {
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        var idStr = reader.GetString(0);
                        if (Guid.TryParse(idStr, out var id))
                        {
                            results.Add(id);
                        }
                    }
                }
                catch
                {
                    // Fall back if syntax error in query
                }
            }
            return results;
        }

        public int GetNoteCount()
        {
            lock (_lock)
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM notes;";
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        public void MigrateFromJsonIfEmpty(string jsonPath)
        {
            if (!File.Exists(jsonPath)) return;
            if (GetNoteCount() > 0) return;

            try
            {
                var json = File.ReadAllText(jsonPath);
                var loaded = JsonSerializer.Deserialize<NotesIndexCatalog>(json);
                if (loaded?.Notes != null && loaded.Notes.Count > 0)
                {
                    foreach (var entry in loaded.Notes)
                    {
                        UpsertNote(entry);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SqliteNoteIndexService] Migration from JSON failed: {ex.Message}");
            }
        }

        private static NoteIndexEntry ReadEntryFromReader(SqliteDataReader reader)
        {
            var idStr = reader.GetString(reader.GetOrdinal("id"));
            var relPath = reader.GetString(reader.GetOrdinal("relative_path"));
            var title = reader.IsDBNull(reader.GetOrdinal("title")) ? "" : reader.GetString(reader.GetOrdinal("title"));
            var snippet = reader.IsDBNull(reader.GetOrdinal("snippet")) ? "" : reader.GetString(reader.GetOrdinal("snippet"));
            var renderText = reader.IsDBNull(reader.GetOrdinal("render_text")) ? "" : reader.GetString(reader.GetOrdinal("render_text"));
            var content = reader.IsDBNull(reader.GetOrdinal("content")) ? "" : reader.GetString(reader.GetOrdinal("content"));
            var tagsJson = reader.IsDBNull(reader.GetOrdinal("tags")) ? "[]" : reader.GetString(reader.GetOrdinal("tags"));
            var headingsJson = reader.IsDBNull(reader.GetOrdinal("headings")) ? "[]" : reader.GetString(reader.GetOrdinal("headings"));
            var lastModStr = reader.IsDBNull(reader.GetOrdinal("last_modified")) ? "" : reader.GetString(reader.GetOrdinal("last_modified"));
            var fileSize = reader.IsDBNull(reader.GetOrdinal("file_size")) ? 0 : reader.GetInt64(reader.GetOrdinal("file_size"));
            var lastWriteStr = reader.IsDBNull(reader.GetOrdinal("last_file_write_utc")) ? "" : reader.GetString(reader.GetOrdinal("last_file_write_utc"));
            var contentHash = reader.IsDBNull(reader.GetOrdinal("content_hash")) ? "" : reader.GetString(reader.GetOrdinal("content_hash"));
            var isSynced = !reader.IsDBNull(reader.GetOrdinal("is_disk_synced")) && reader.GetInt32(reader.GetOrdinal("is_disk_synced")) == 1;

            string[] tags = Array.Empty<string>();
            try { tags = JsonSerializer.Deserialize<string[]>(tagsJson) ?? Array.Empty<string>(); } catch { }

            string[] headings = Array.Empty<string>();
            try { headings = JsonSerializer.Deserialize<string[]>(headingsJson) ?? Array.Empty<string>(); } catch { }

            DateTime.TryParse(lastModStr, out var lastMod);
            DateTime.TryParse(lastWriteStr, out var lastWrite);

            return new NoteIndexEntry
            {
                Id = Guid.TryParse(idStr, out var g) ? g : Guid.Empty,
                RelativePath = relPath,
                Title = title,
                Snippet = snippet,
                RenderText = renderText,
                Content = content,
                Tags = tags,
                Headings = headings,
                LastModified = lastMod,
                FileSize = fileSize,
                LastFileWriteUtc = lastWrite,
                ContentHash = contentHash,
                IsDiskSynced = isSynced
            };
        }

        public void Dispose()
        {
            // SQLite connections are pooled and disposed per call
        }
    }
}
