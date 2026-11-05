using System.IO;
using FocusBloom.Core.Calendar;
using Xunit;

namespace FocusBloom.Tests;

public sealed class CourseCsvTests
{
    [Fact]
    public void GroupsQuotedMeetingsAndSkipsDuplicates()
    {
        var csv = CourseCsv.Header + "\n统计学,1,1,2,\"1,3-5\",理教101,王老师\n统计学,3,3,4,1-6,理教102,王老师\n";
        var term = SchoolTerm.Default(new DateOnly(2026, 11, 4));
        var first = CourseCsv.Read(new StringReader(csv), term, []);
        var course = Assert.Single(first);
        Assert.Equal(2, course.Meetings!.Count);
        Assert.Equal("1,3,4,5", course.Meetings[0].Weeks);
        Assert.Empty(CourseCsv.Read(new StringReader(csv), term, first));
    }
    [Fact]
    public void RejectsTheWholeFileWhenALaterRowIsInvalid()
    {
        var csv = CourseCsv.Header + "\n统计学,1,1,2,1-4,,\n实验课,8,1,2,1-4,,";
        var term = SchoolTerm.Default(new DateOnly(2026, 11, 4));
        Assert.Contains("第 3 行", Assert.Throws<FormatException>(() => CourseCsv.Read(new StringReader(csv), term, [])).Message);
    }
}
