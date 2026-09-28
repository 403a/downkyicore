using System.Collections.Immutable;
using DownKyi.Core.Danmaku2Ass;
using DownKyi.Core.Settings;

namespace DownKyi.Core.Tests;

public sealed class CustomDanmakuFilterTests
{
    [Fact]
    public void EmojiOnlyDanmakuIsRemovedAfterCleanup()
    {
        var result = CreateFilter(removeEmoji: true).DoFilter([CreateDanmaku("😂😂😂")]);

        Assert.Empty(result);
    }

    [Fact]
    public void TextAndEmojiKeepsTheTextWithoutMutatingTheInput()
    {
        var input = CreateDanmaku("太好笑了😂😂");

        var result = CreateFilter(removeEmoji: true).DoFilter([input]);

        Assert.Equal("太好笑了", Assert.Single(result).Content);
        Assert.Equal("太好笑了😂😂", input.Content);
    }

    [Fact]
    public void NormalChineseEnglishNumbersAndPunctuationArePreserved()
    {
        const string content = "中文 English 123，。！？,.!?-_:;()[]";

        var result = CreateFilter(removeEmoji: true).DoFilter([CreateDanmaku(content)]);

        Assert.Equal(content, Assert.Single(result).Content);
    }

    [Fact]
    public void ExactKeywordMatchIsRemoved()
    {
        var result = CreateFilter(blockedKeywords: ["劇透"]).DoFilter([CreateDanmaku("劇透")]);

        Assert.Empty(result);
    }

    [Fact]
    public void KeywordContainedInSentenceIsRemoved()
    {
        var result = CreateFilter(blockedKeywords: ["劇透"]).DoFilter([CreateDanmaku("前方劇透注意")]);

        Assert.Empty(result);
    }

    [Fact]
    public void UnrelatedTextIsPreservedAndWhitespaceKeywordsAreIgnored()
    {
        var danmaku = CreateDanmaku("這集很好看");

        var result = CreateFilter(blockedKeywords: [" ", "\t", "劇透"]).DoFilter([danmaku]);

        Assert.Same(danmaku, Assert.Single(result));
    }

    [Fact]
    public void KeywordRuleDoesNotApplyCleanupSemanticsWhenCleanupIsDisabled()
    {
        var danmaku = CreateDanmaku("   ");

        var result = CreateFilter(blockedKeywords: ["劇透"]).DoFilter([danmaku]);

        Assert.Same(danmaku, Assert.Single(result));
    }

    [Fact]
    public void BlockedCommenterIsRemovedAndOtherCommentersArePreserved()
    {
        var kept = CreateDanmaku("保留", commenter: "def456");

        var result = CreateFilter(blockedCommenters: ["abc123"]).DoFilter(
        [
            CreateDanmaku("刪除", commenter: "ABC123"),
            kept
        ]);

        Assert.Same(kept, Assert.Single(result));
    }

    [Fact]
    public void MultipleRulesAreAppliedTogether()
    {
        var result = CreateFilter(
            removeEmoji: true,
            blockedKeywords: ["劇透"],
            blockedCommenters: ["blocked"]).DoFilter(
        [
            CreateDanmaku("😂😂"),
            CreateDanmaku("前方劇透😂"),
            CreateDanmaku("發送者命中", commenter: "blocked"),
            CreateDanmaku("正常😂")
        ]);

        Assert.Equal("正常", Assert.Single(result).Content);
    }

    [Fact]
    public void DisabledCustomRulesLeaveTheExistingProducerOutputAndReportUnchanged()
    {
        var input = new[] { CreateDanmaku("原樣😂", style: "scroll") };
        var producer = new Producer(
            DisabledTypeFilters(),
            input,
            CreateFilter(blockedKeywords: [" "]));

        producer.StartHandle();

        Assert.Same(input[0], Assert.Single(producer.KeepedDanmakus));
        Assert.Equal("原樣😂", producer.KeepedDanmakus[0].Content);
        Assert.DoesNotContain("custom_filter", producer.Report().Keys);
    }

    [Theory]
    [InlineData("top_filter", "top")]
    [InlineData("bottom_filter", "bottom")]
    [InlineData("scroll_filter", "scroll")]
    public void ExistingTypeFiltersStillRemoveOnlyTheirOwnedStyle(string filterName, string blockedStyle)
    {
        var config = DisabledTypeFilters();
        config[filterName] = true;
        var kept = CreateDanmaku("保留", style: "none");
        var producer = new Producer(config, [CreateDanmaku("刪除", style: blockedStyle), kept]);

        producer.StartHandle();

        Assert.Same(kept, Assert.Single(producer.KeepedDanmakus));
        Assert.Equal(1, producer.Report()[filterName]);
    }

    [Fact]
    public void CustomFilterRunsAfterTheExistingTypeFilters()
    {
        var config = DisabledTypeFilters();
        config["top_filter"] = true;
        var producer = new Producer(
            config,
            [
                CreateDanmaku("劇透", style: "top"),
                CreateDanmaku("劇透", style: "scroll")
            ],
            CreateFilter(blockedKeywords: ["劇透"]));

        producer.StartHandle();
        var report = producer.Report();

        Assert.Empty(producer.KeepedDanmakus);
        Assert.Equal(1, report["top_filter"]);
        Assert.Equal(1, report["custom_filter"]);
    }

    [Fact]
    public async Task DanmakuBlacklistSettingsAreTrimmedDeduplicatedAndPersisted()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"downkyi-danmaku-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var settingsPath = Path.Combine(directory, "settings.json");
        try
        {
            using (var store = new SettingsStore(settingsPath))
            {
                store.Update(settings => settings with
                {
                    Danmaku = settings.Danmaku with
                    {
                        BlockedKeywords = ImmutableArray.Create(" 劇透 ", "", "劇透", "廣告"),
                        BlockedSenderUids = ImmutableArray.Create(123L, 0L, 123L, 456L)
                    }
                });
                await store.FlushAsync(TestContext.Current.CancellationToken);

                Assert.Equal(["劇透", "廣告"], store.Current.Danmaku.BlockedKeywords);
                Assert.Equal([123L, 456L], store.Current.Danmaku.BlockedSenderUids);
            }

            using var reopened = new SettingsStore(settingsPath);
            Assert.Equal(["劇透", "廣告"], reopened.Current.Danmaku.BlockedKeywords);
            Assert.Equal([123L, 456L], reopened.Current.Danmaku.BlockedSenderUids);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static CustomDanmakuFilter CreateFilter(
        bool removeEmoji = false,
        IEnumerable<string>? blockedKeywords = null,
        IEnumerable<string>? blockedCommenters = null)
    {
        return new CustomDanmakuFilter(
            removeEmoji,
            blockedKeywords ?? [],
            blockedCommenters ?? []);
    }

    private static Dictionary<string, bool> DisabledTypeFilters()
    {
        return new Dictionary<string, bool>
        {
            ["top_filter"] = false,
            ["bottom_filter"] = false,
            ["scroll_filter"] = false
        };
    }

    private static Danmaku CreateDanmaku(
        string content,
        string commenter = "commenter",
        string style = "scroll")
    {
        return new Danmaku
        {
            Start = 1,
            Style = style,
            Color = 0xFFFFFF,
            Commenter = commenter,
            Content = content,
            SizeRatio = 1
        };
    }
}
