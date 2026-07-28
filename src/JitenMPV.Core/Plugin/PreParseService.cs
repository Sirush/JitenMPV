using System.Text;
using JitenMPV.Core.Api;
using JitenMPV.Core.Cache;
using JitenMPV.Core.Config;
using JitenMPV.Core.Media;
using JitenMPV.Core.Mpv;
using JitenMPV.Core.Subtitles;
using Microsoft.Extensions.Logging;

namespace JitenMPV.Core.Plugin;

public sealed class PreParseService(
    JitenApiClient api,
    ParseCache cache,
    ILogger logger,
    int maxBatchChars = 60_000,
    SubtitleTimeline? timeline = null,
    PluginSettings? settings = null)
{
    private static readonly TimeSpan ExtractTimeout = TimeSpan.FromMinutes(2);

    public async Task PreParseFileAsync(string subtitleFilePath, CancellationToken ct)
    {
        logger.LogInformation("Pre-parsing external subtitle file: {Path}", subtitleFilePath);

        List<SubtitleCue> cues;
        try
        {
            cues = SubtitleParser.ParseFile(subtitleFilePath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse subtitle file");
            return;
        }

        logger.LogInformation("Parsed {Count} cues from file", cues.Count);
        timeline?.Load(cues);
        await BatchParseTextsAsync(ExtractUniqueTexts(cues), ct);
    }

    public async Task PreParseEmbeddedAsync(MpvIpcClient ipc, CancellationToken ct)
    {
        var texts = await TryFfmpegExtract(ipc, ct);
        if (texts is not null)
        {
            await BatchParseTextsAsync(texts, ct);
            return;
        }

        logger.LogInformation("No pre-parse method available, using on-demand parsing");
    }

    private async Task<List<string>?> TryFfmpegExtract(MpvIpcClient ipc, CancellationToken ct)
    {
        var ffmpeg = (await FfmpegLocator.ResolveAsync(settings?.FfmpegPath, ct))?.ExecutablePath;
        if (ffmpeg is null)
        {
            logger.LogInformation("ffmpeg not available, skipping embedded subtitle extraction");
            return null;
        }

        var videoPath = await ipc.GetPropertyAsync<string>("path", ct);
        if (string.IsNullOrEmpty(videoPath))
            return null;

        if (!Path.IsPathRooted(videoPath))
        {
            var workDir = await ipc.GetPropertyAsync<string>("working-directory", ct);
            if (!string.IsNullOrEmpty(workDir))
                videoPath = Path.Combine(workDir, videoPath);
        }

        // Guessing stream 0 here would seed the timeline with whatever language happens to be first,
        // and the mining sentence is taken from that timeline.
        if (await TrackIndexResolver.FindAsync(ipc, "sub", ct) is not { } subIndex)
        {
            logger.LogInformation("No subtitle track selected, skipping embedded extraction");
            return null;
        }

        try
        {
            var runner = new FfmpegRunner(ffmpeg, logger);
            var (result, bytes) = await runner.RunCaptureStdoutAsync(
                ["-i", videoPath, "-map", $"0:s:{subIndex}", "-f", "srt", "-"],
                ExtractTimeout, ct);

            if (!result.Succeeded)
            {
                logger.LogWarning("ffmpeg extraction failed (exit {Code}): {Error}",
                    result.ExitCode, result.ErrorTail);
                return null;
            }

            var srtOutput = Encoding.UTF8.GetString(bytes);
            if (string.IsNullOrWhiteSpace(srtOutput))
                return null;

            var cues = SrtParser.Parse(srtOutput);
            logger.LogInformation("Extracted {Count} subtitle cues via ffmpeg", cues.Count);
            timeline?.Load(cues);
            return ExtractUniqueTexts(cues);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("ffmpeg extraction failed: {Message}", ex.Message);
            return null;
        }
    }

    /// The parse cache is keyed by the exact string that gets rendered, so with single-line mode on
    /// both forms are seeded: which one a cue ends up displaying is only known once its joined width
    /// has been measured against the screen.
    private List<string> ExtractUniqueTexts(List<SubtitleCue> cues)
    {
        var texts = cues.Select(c => c.Text);

        if (settings?.SubtitleSingleLine == true)
            texts = texts.SelectMany(t => new[] { t, SubtitleLineJoiner.Join(t) });

        return texts
            .Where(t => !string.IsNullOrWhiteSpace(t) && JapaneseDetector.ContainsJapanese(t))
            .Distinct()
            .ToList();
    }

    private async Task BatchParseTextsAsync(List<string> texts, CancellationToken ct)
    {
        if (texts.Count == 0) return;

        logger.LogInformation("Batch-parsing {Count} unique subtitle lines", texts.Count);

        int i = 0;
        int batchNum = 0;
        while (i < texts.Count)
        {
            ct.ThrowIfCancellationRequested();

            int charCount = 0;
            int batchEnd = i;
            while (batchEnd < texts.Count && charCount + texts[batchEnd].Length <= maxBatchChars)
            {
                charCount += texts[batchEnd].Length;
                batchEnd++;
            }
            if (batchEnd == i) batchEnd = i + 1;

            var count = batchEnd - i;
            var batch = new string[count];
            texts.CopyTo(i, batch, 0, count);
            batchNum++;

            try
            {
                var response = await api.ParseBatchAsync(batch, ct);
                var (sharedVocab, sharedFreqs, sharedDetails, sharedPitch) =
                    ParseCacheEntry.BuildVocabData(response);

                for (int j = 0; j < batch.Length && j < response.Tokens.Count; j++)
                {
                    var entry = ParseCacheEntry.FromTokens(
                        response.Tokens[j], sharedVocab, sharedFreqs, sharedDetails, sharedPitch);
                    cache.Set(batch[j], entry);
                }

                logger.LogInformation("Pre-parsed batch {Batch}: {Done}/{Total} lines",
                    batchNum, batchEnd, texts.Count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("Pre-parse batch failed: {Message}", ex.Message);
                break;
            }

            i = batchEnd;
        }
    }
}
