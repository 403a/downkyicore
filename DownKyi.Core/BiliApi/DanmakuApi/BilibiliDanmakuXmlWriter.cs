using System.Globalization;
using System.Xml.Linq;
using DownKyi.Core.BiliApi.DanmakuApi.Models;

namespace DownKyi.Core.BiliApi.DanmakuApi;

internal static class BilibiliDanmakuXmlWriter
{
    public static async Task WriteAsync(
        IReadOnlyList<BiliDanmaku> danmakus,
        string filePath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(danmakus);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        cancellationToken.ThrowIfCancellationRequested();

        var root = new XElement("i");
        foreach (var danmaku in danmakus)
        {
            cancellationToken.ThrowIfCancellationRequested();
            root.Add(new XElement(
                "d",
                new XAttribute("p", CreateParameter(danmaku)),
                new XText(danmaku.Content)));
        }

        var document = new XDocument(new XDeclaration("1.0", "utf-8", null), root);
        var output = new FileStream(
            filePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.Asynchronous);
        await using (output.ConfigureAwait(false))
        {
            await document.SaveAsync(output, SaveOptions.DisableFormatting, cancellationToken)
                .ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static string CreateParameter(BiliDanmaku danmaku)
    {
        return string.Join(
            ',',
            (danmaku.Progress / 1000m).ToString("0.000", CultureInfo.InvariantCulture),
            danmaku.Mode.ToString(CultureInfo.InvariantCulture),
            danmaku.Fontsize.ToString(CultureInfo.InvariantCulture),
            danmaku.Color.ToString(CultureInfo.InvariantCulture),
            danmaku.Ctime.ToString(CultureInfo.InvariantCulture),
            danmaku.Pool.ToString(CultureInfo.InvariantCulture),
            danmaku.MidHash,
            danmaku.Id.ToString(CultureInfo.InvariantCulture));
    }
}
