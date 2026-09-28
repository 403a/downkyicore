namespace DownKyi.Core.Settings;

public enum DanmakuOutputFormat
{
    Ass = 0,
    Xml = 1,
    AssAndXml = 2
}

public static class DanmakuOutputFormatExtensions
{
    public static bool IncludesAss(this DanmakuOutputFormat format) =>
        format is DanmakuOutputFormat.Ass or DanmakuOutputFormat.AssAndXml;

    public static bool IncludesXml(this DanmakuOutputFormat format) =>
        format is DanmakuOutputFormat.Xml or DanmakuOutputFormat.AssAndXml;
}
