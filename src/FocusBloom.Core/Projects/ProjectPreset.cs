namespace FocusBloom.Core.Projects;

public sealed record PresetTask(string Title, int Minutes);

public sealed record ProjectPreset(string Name, string Goal, IReadOnlyList<ProjectMilestone> Milestones, IReadOnlyList<PresetTask> Tasks)
{
    public static IReadOnlyList<ProjectPreset> All { get; } =
    [
        new("空白项目", "", [], []),
        new("阅读计划", "读完并留下可复习的笔记", [new("读完正文"), new("整理读书笔记")],
            [new("浏览目录并制定计划", 15), new("阅读正文并摘录", 60), new("整理读书笔记", 30)]),
        new("备考复习", "完成知识梳理与错题复盘", [new("梳理知识点"), new("完成模拟练习")],
            [new("列出复习范围", 20), new("梳理重点章节", 60), new("完成模拟练习", 60), new("复盘错题", 30)]),
        new("论文写作", "完成一篇可提交的稿件", [new("确定提纲"), new("完成初稿"), new("校对与提交")],
            [new("查找并整理参考资料", 60), new("拟定提纲", 35), new("撰写初稿", 90), new("校对并提交", 45)])
    ];
}
