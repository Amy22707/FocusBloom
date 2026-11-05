using System.Text.Json;
using FocusBloom.Core.Calendar;
using Microsoft.Data.Sqlite;

namespace FocusBloom.Infrastructure.Storage;

public sealed partial class SqliteFocusStore
{
    private const string ActiveSemesterKey = "school.semester.active";
    private const string SemesterArchivePrefix = "school.semester.archive.";
    private sealed record SemesterIdentity(Guid Id, string Name);
    private sealed record SemesterRow(string Id, string Payload);
    private sealed record SemesterArchive(SemesterIdentity Identity, SchoolTerm Term, SemesterRow[] Courses, SemesterRow[] Changes);

    public IReadOnlyList<SchoolSemester> GetSchoolSemesters()
    {
        using var connection = Open(); using var transaction = connection.BeginTransaction(deferred: true);
        var active = ReadSemesterIdentity(connection, transaction);
        var term = ReadActiveTerm(connection, transaction);
        var list = ReadSemesterArchives(connection, transaction)
            .Select(archive => new SchoolSemester(archive.Identity.Id, archive.Identity.Name, archive.Term.Start, false))
            .OrderByDescending(item => item.Start).Prepend(new(active.Id, active.Name, term.Start, true)).ToArray();
        transaction.Commit(); return list;
    }

    public void CreateSchoolSemester(string name, DateOnly start) =>
        ChangeSemester((connection, transaction) =>
        {
            name = ValidateSemesterName(connection, transaction, name);
            return new(new(Guid.NewGuid(), name), SchoolTerm.Default(start), [], []);
        });

    public void SelectSchoolSemester(Guid id)
    {
        ChangeSemester((connection, transaction) => ReadSemesterIdentity(connection, transaction).Id == id
            ? null
            : ReadSemesterArchives(connection, transaction).SingleOrDefault(item => item.Identity.Id == id)
                ?? throw new InvalidOperationException("找不到所选学期。"));
    }

    public void RenameSchoolSemester(string name)
    {
        using var connection = Open(); using var transaction = connection.BeginTransaction();
        var identity = ReadSemesterIdentity(connection, transaction);
        name = name.Trim();
        if (!string.Equals(name, identity.Name, StringComparison.OrdinalIgnoreCase))
            name = ValidateSemesterName(connection, transaction, name);
        WriteSemesterSetting(connection, transaction, ActiveSemesterKey, identity with { Name = name });
        transaction.Commit();
    }

    private void ChangeSemester(Func<SqliteConnection, SqliteTransaction, SemesterArchive?> next)
    {
        using var connection = Open(); using var transaction = connection.BeginTransaction();
        var target = next(connection, transaction);
        if (target is not null)
        {
            var identity = ReadSemesterIdentity(connection, transaction);
            var current = new SemesterArchive(identity, ReadActiveTerm(connection, transaction), ReadRows("school_courses"), ReadRows("course_changes"));
            WriteSemesterSetting(connection, transaction, SemesterArchivePrefix + identity.Id, current);
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "DELETE FROM course_changes; DELETE FROM school_courses; DELETE FROM settings WHERE key=$archive;";
            command.Parameters.AddWithValue("$archive", SemesterArchivePrefix + target.Identity.Id);
            command.ExecuteNonQuery();
            InsertRows("school_courses", target.Courses);
            InsertRows("course_changes", target.Changes);
            WriteSemesterSetting(connection, transaction, ActiveSemesterKey, target.Identity);
            WriteSemesterSetting(connection, transaction, "school.term", target.Term);
        }
        transaction.Commit();

        SemesterRow[] ReadRows(string table)
        {
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = $"SELECT id,payload FROM {table} ORDER BY rowid";
            using var reader = command.ExecuteReader();
            var rows = new List<SemesterRow>();
            while (reader.Read()) rows.Add(new(reader.GetString(0), reader.GetString(1)));
            return rows.ToArray();
        }
        void InsertRows(string table, SemesterRow[] rows)
        {
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = $"INSERT INTO {table}(id,payload) VALUES($id,$payload)";
            var id = command.Parameters.Add("$id", SqliteType.Text);
            var payload = command.Parameters.Add("$payload", SqliteType.Text);
            foreach (var row in rows)
            { id.Value = row.Id; payload.Value = row.Payload; command.ExecuteNonQuery(); }
        }
    }

    private static string ValidateSemesterName(SqliteConnection connection, SqliteTransaction transaction, string name)
    {
        name = name.Trim();
        if (name.Length == 0) throw new ArgumentException("请填写学期名称。");
        if (string.Equals(name, ReadSemesterIdentity(connection, transaction).Name, StringComparison.OrdinalIgnoreCase)
            || ReadSemesterArchives(connection, transaction).Any(item => string.Equals(item.Identity.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("已有同名学期。");
        return name;
    }
    private static SemesterIdentity ReadSemesterIdentity(SqliteConnection connection, SqliteTransaction transaction) =>
        ReadSemesterSetting<SemesterIdentity>(connection, transaction, ActiveSemesterKey) ?? new(Guid.Empty, "原有学期");
    private static SchoolTerm ReadActiveTerm(SqliteConnection connection, SqliteTransaction transaction) =>
        ReadSemesterSetting<SchoolTerm>(connection, transaction, "school.term") ?? SchoolTerm.Default(DateOnly.FromDateTime(DateTime.Today));

    private static T? ReadSemesterSetting<T>(SqliteConnection connection, SqliteTransaction transaction, string key) where T : class
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT value FROM settings WHERE key=$key";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() is string value ? JsonSerializer.Deserialize<T>(value, CalendarJson) : null;
    }
    private static IReadOnlyList<SemesterArchive> ReadSemesterArchives(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT value FROM settings WHERE key GLOB 'school.semester.archive.*'";
        using var reader = command.ExecuteReader(); var archives = new List<SemesterArchive>();
        while (reader.Read()) archives.Add(JsonSerializer.Deserialize<SemesterArchive>(reader.GetString(0), CalendarJson)!);
        return archives;
    }
    private static void WriteSemesterSetting<T>(SqliteConnection connection, SqliteTransaction transaction, string key, T value)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "INSERT INTO settings(key,value) VALUES($key,$value) ON CONFLICT(key) DO UPDATE SET value=excluded.value";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", JsonSerializer.Serialize(value, CalendarJson));
        command.ExecuteNonQuery();
    }
}
