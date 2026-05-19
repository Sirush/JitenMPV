using System.Diagnostics;
using System.Text.Json;
using JitenMPV.Core.Api;
using JitenMPV.Core.Cache;
using JitenMPV.Core.Mpv;
using JitenMPV.Core.Subtitles;
using Microsoft.Extensions.Logging;

namespace JitenMPV.Core.Plugin;

public sealed class PreParseService(JitenApiClient api, ParseCache cache, ILogger logger)
{
    private const int MaxBatchChars = 60_000;

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
        if (!await IsFfmpegAvailableAsync(ct))
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

        var subIndex = await GetSubTrackIndex(ipc, ct);

        try
        {
            var srtOutput = await RunFfmpegExtract(videoPath, subIndex, ct);
            if (string.IsNullOrWhiteSpace(srtOutput))
                return null;

            var cues = SrtParser.Parse(srtOutput);
            logger.LogInformation("Extracted {Count} subtitle cues via ffmpeg", cues.Count);
            return ExtractUniqueTexts(cues);
        }
        catch (Exception ex)
        {
            logger.LogWarning("ffmpeg extraction failed: {Message}", ex.Message);
            return null;
        }
    }

    private static async Task<bool> IsFfmpegAvailableAsync(CancellationToken ct)
    {
        try
        {
            using var proc = new Process();
            proc.StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = "-version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            proc.Start();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(3000);
            await proc.WaitForExitAsync(timeoutCts.Token);
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<int> GetSubTrackIndex(MpvIpcClient ipc, CancellationToken ct)
    {
        try
        {
            var trackList = await ipc.GetPropertyRawAsync("track-list", ct);
            if (trackList is { ValueKind: JsonValueKind.Array })
            {
                int subIdx = 0;
                foreach (var track in trackList.Value.EnumerateArray())
                {
                    if (track.TryGetProperty("type", out var typeEl) && typeEl.GetString() != "sub")
                        continue;

                    if (track.TryGetProperty("selected", out var sel) && sel.GetBoolean())
                        return subIdx;

                    subIdx++;
                }
            }
        }
        catch { }
        return 0;
    }

    private static async Task<string> RunFfmpegExtract(string videoPath, int subIndex, CancellationToken ct)
    {
        using var proc = new Process();
        proc.StartInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        proc.StartInfo.ArgumentList.Add("-i");
        proc.StartInfo.ArgumentList.Add(videoPath);
        proc.StartInfo.ArgumentList.Add("-map");
        proc.StartInfo.ArgumentList.Add($"0:s:{subIndex}");
        proc.StartInfo.ArgumentList.Add("-f");
        proc.StartInfo.ArgumentList.Add("srt");
        proc.StartInfo.ArgumentList.Add("-");

        proc.Start();

        var outputTask = proc.StandardOutput.ReadToEndAsync(ct);
        var errorTask = proc.StandardError.ReadToEndAsync(ct);

        await proc.WaitForExitAsync(ct);

        var output = await outputTask;
        var error = await errorTask;

        if (proc.ExitCode != 0)
        {
            var lastLines = string.Join('\n', error.Split('\n').Where(l => l.Length > 0).TakeLast(5));
            throw new InvalidOperationException($"ffmpeg exit {proc.ExitCode}: {lastLines}");
        }

        return output;
    }

    private static List<string> ExtractUniqueTexts(List<SubtitleCue> cues)
        => cues
            .Select(c => c.Text)
            .Where(t => !string.IsNullOrWhiteSpace(t) && JapaneseDetector.ContainsJapanese(t))
            .Distinct()
            .ToList();

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
            while (batchEnd < texts.Count && charCount + texts[batchEnd].Length <= MaxBatchChars)
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
                var (sharedVocab, sharedFreqs, sharedDetails) = ParseCacheEntry.BuildVocabData(response);

                for (int j = 0; j < batch.Length && j < response.Tokens.Count; j++)
                {
                    var entry = ParseCacheEntry.FromTokens(response.Tokens[j], sharedVocab, sharedFreqs, sharedDetails);
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
