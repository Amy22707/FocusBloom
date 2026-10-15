using FocusBloom.Core.Goals;

namespace FocusBloom.Infrastructure.Storage;

public sealed partial class SqliteFocusStore
{
    public IReadOnlyList<FocusGoal> GetGoals() => ReadCalendarRows<FocusGoal>("focus_goals");
    public void SaveGoal(FocusGoal goal) => WriteCalendarRow("focus_goals", goal.Id, goal);
    public void DeleteGoal(Guid id) => DeleteCalendarRow("focus_goals", id);
    public IReadOnlyList<Habit> GetHabits(bool includeDeleted = false) => ReadCalendarRows<Habit>("habits").Where(item => includeDeleted || !item.Deleted).ToArray();
    public void SaveHabit(Habit habit) => WriteCalendarRow("habits", habit.Id, habit);
    public void DeleteHabit(Guid id) { var habit = GetHabits().FirstOrDefault(item => item.Id == id); if (habit is not null) SaveHabit(habit with { Deleted = true }); }
    public IReadOnlyList<HabitCheckIn> GetHabitCheckIns()
    {
        using var connection = Open(); using var command = connection.CreateCommand(); command.CommandText = "SELECT habit_id,date FROM habit_checkins ORDER BY date";
        using var reader = command.ExecuteReader(); var items = new List<HabitCheckIn>(); while (reader.Read()) items.Add(new(Guid.Parse(reader.GetString(0)), DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd"))); return items;
    }
    public void SetHabitCheckIn(Guid habitId, DateOnly date, bool completed)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = completed ? "INSERT OR IGNORE INTO habit_checkins(habit_id,date) VALUES($id,$date)" : "DELETE FROM habit_checkins WHERE habit_id=$id AND date=$date";
        command.Parameters.AddWithValue("$id", habitId.ToString()); command.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd")); command.ExecuteNonQuery();
    }
}
