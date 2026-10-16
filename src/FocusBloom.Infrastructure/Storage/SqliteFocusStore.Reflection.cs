using System.Text.Json;
using FocusBloom.Core.Reflection;

namespace FocusBloom.Infrastructure.Storage;

public sealed partial class SqliteFocusStore
{
    public IReadOnlyList<DailyReflection> GetDailyReflections()
    {
        using var connection = Open(); using var command = connection.CreateCommand(); command.CommandText = "SELECT payload FROM daily_reflections ORDER BY date";
        using var reader = command.ExecuteReader(); var items = new List<DailyReflection>(); while (reader.Read()) items.Add(JsonSerializer.Deserialize<DailyReflection>(reader.GetString(0), CalendarJson)!); return items;
    }
    public void SaveDailyReflection(DailyReflection item)
    {
        item = ReflectionRules.Normalize(item, DateOnly.FromDateTime(DateTime.Today));
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO daily_reflections(date,payload) VALUES($date,$payload) ON CONFLICT(date) DO UPDATE SET payload=$payload";
        command.Parameters.AddWithValue("$date", item.Date.ToString("yyyy-MM-dd")); command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(item, CalendarJson)); command.ExecuteNonQuery();
    }
    public void DeleteDailyReflection(DateOnly date)
    {
        using var connection = Open(); using var command = connection.CreateCommand(); command.CommandText = "DELETE FROM daily_reflections WHERE date=$date"; command.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd")); command.ExecuteNonQuery();
    }
}
