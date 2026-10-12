using System.Windows;
using FocusBloom.Desktop.Windows;
using Xunit;

namespace FocusBloom.Tests;

public sealed class WindowLayoutTests
{
    [Fact]
    public void TimerFloatKeepsCompactLayoutAndReturnsToWorkAreaAfterMonitorChange()
    {
        var area = new Rect(-1920, 0, 1920, 1040);
        var saved = new TimerFloatLayout(-480, 700, 360, 260, true, "second-screen");
        Assert.Equal(saved, saved.FitTo(area));
        var recovered = saved.FitTo(new Rect(0, 0, 1280, 680));
        Assert.Equal(8, recovered.Left);
        Assert.Equal(412, recovered.Top);
        Assert.Equal(360, recovered.Width);
        Assert.Equal(260, recovered.Height);
        Assert.True(recovered.Topmost);
        var initial = TimerFloatLayout.CreateDefault(new Rect(0, 0, 1280, 680));
        Assert.Equal(340, initial.Width); Assert.Equal(244, initial.Height);
        Assert.True(initial.Left + initial.Width < 1280);
        Assert.True(initial.Top + initial.Height < 680);
        var compact = (initial with { Width = 100, Height = 100 }).FitTo(new Rect(0, 0, 1280, 680));
        Assert.Equal(240, compact.Width); Assert.Equal(180, compact.Height);
    }

    [Theory]
    [InlineData(1092, 580)]
    [InlineData(1280, 680)]
    [InlineData(1920, 1040)]
    public void FirstWindowFitsWorkAreaWithRoomToReachCaptionAndEdges(double width, double height)
    {
        var area = new Rect(0, 0, width, height);
        var layout = WindowLayout.CreateDefault(area);
        Assert.True(layout.Left >= 16 && layout.Top >= 16);
        Assert.True(layout.Left + layout.Width <= area.Right - 16);
        Assert.True(layout.Top + layout.Height <= area.Bottom - 16);
    }

    [Fact]
    public void LayoutKeepsUserSizeWhenItFitsAndReturnsToVisibleAreaAfterDisplayChange()
    {
        var saved = new WindowLayout(140, 80, 1100, 650, false, true);
        Assert.Equal(saved, saved.FitTo(new Rect(0, 0, 1920, 1040)));
        var smaller = saved with { Left = 2500, Top = -80, Width = 1240, Height = 800 };
        var fitted = smaller.FitTo(new Rect(0, 0, 1092, 580));
        Assert.Equal(1060, fitted.Width);
        Assert.Equal(548, fitted.Height);
        Assert.Equal(16, fitted.Left);
        Assert.Equal(16, fitted.Top);
        Assert.True(fitted.Topmost);
    }
}
