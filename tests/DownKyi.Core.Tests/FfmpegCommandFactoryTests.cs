using DownKyi.Core.FFmpeg;

namespace DownKyi.Core.Tests;

public sealed class FfmpegCommandFactoryTests
{
    [Fact]
    public void GenericMergePreservesOptionalAudioBehavior()
    {
        var command = FfmpegCommandFactory.BuildMerge(
            audioFile: null,
            videoFile: "segment.flv",
            outputFile: "output.mp4",
            transcodeAudioToMp3: false,
            embeddedAudioMode: FfmpegEmbeddedAudioMode.Optional);

        Assert.Contains("0:a?", command.Arguments);
        Assert.DoesNotContain("0:a:0", command.Arguments);
        Assert.DoesNotContain("-an", command.Arguments);
    }

    [Fact]
    public void MergeVideoOnlyContractDisablesAudioFromDurlInput()
    {
        var command = FfmpegCommandFactory.BuildMerge(
            audioFile: null,
            videoFile: "segment.flv",
            outputFile: "output.mp4",
            transcodeAudioToMp3: false,
            embeddedAudioMode: FfmpegEmbeddedAudioMode.Excluded);

        Assert.Contains("-an", command.Arguments);
        Assert.DoesNotContain("0:a:0", command.Arguments);
        Assert.DoesNotContain("0:a?", command.Arguments);
    }

    [Fact]
    public void MergeAudioVideoContractRequiresAudioFromDurlInput()
    {
        var command = FfmpegCommandFactory.BuildMerge(
            audioFile: null,
            videoFile: "segment.flv",
            outputFile: "output.mp4",
            transcodeAudioToMp3: false,
            embeddedAudioMode: FfmpegEmbeddedAudioMode.Required);

        Assert.Contains("0:a:0", command.Arguments);
        Assert.DoesNotContain("0:a?", command.Arguments);
        Assert.DoesNotContain("-an", command.Arguments);
    }

    [Fact]
    public void ConcatVideoOnlyContractDisablesAudioFromDurlInputs()
    {
        var command = FfmpegCommandFactory.BuildConcat(
            "segments.txt",
            "output.mp4",
            FfmpegConcatStrategy.StreamCopy,
            hardwareEncoder: null,
            embeddedAudioMode: FfmpegEmbeddedAudioMode.Excluded);

        Assert.Contains("-an", command.Arguments);
        Assert.DoesNotContain("0:a:0", command.Arguments);
        Assert.DoesNotContain("0:a?", command.Arguments);
    }

    [Fact]
    public void GenericConcatPreservesOptionalAudioBehavior()
    {
        var command = FfmpegCommandFactory.BuildConcat(
            "segments.txt",
            "output.mp4",
            FfmpegConcatStrategy.StreamCopy,
            hardwareEncoder: null,
            embeddedAudioMode: FfmpegEmbeddedAudioMode.Optional);

        Assert.Contains("0:a?", command.Arguments);
        Assert.DoesNotContain("0:a:0", command.Arguments);
        Assert.DoesNotContain("-an", command.Arguments);
    }

    [Fact]
    public void ConcatAudioVideoContractRequiresAudioFromDurlInputs()
    {
        var command = FfmpegCommandFactory.BuildConcat(
            "segments.txt",
            "output.mp4",
            FfmpegConcatStrategy.StreamCopy,
            hardwareEncoder: null,
            embeddedAudioMode: FfmpegEmbeddedAudioMode.Required);

        Assert.Contains("0:a:0", command.Arguments);
        Assert.DoesNotContain("0:a?", command.Arguments);
        Assert.DoesNotContain("-an", command.Arguments);
    }
}
