using System.IO;
using System.Text.RegularExpressions;

namespace PS2ClassicsPackageStudio;

internal sealed record CueReport(string? Error, int TrackCount);
internal enum CueAudioStatus { Unknown, None, Present, Missing }
internal static class CueValidation
{
    public static CueAudioStatus InspectAudio(string cue)
    {
        try
        {
            if (new FileInfo(cue).Length > 1024 * 1024) return CueAudioStatus.Unknown;
            string folder = Path.GetDirectoryName(Path.GetFullPath(cue))!;
            string? currentFile = null;
            bool foundAudio = false, foundTrack = false;
            foreach (string line in File.ReadLines(cue))
            {
                var file = Regex.Match(line, @"^\s*FILE\s+(?:""([^""\r\n]+)""|(\S+))\s+(\S+)\s*$", RegexOptions.IgnoreCase);
                if (file.Success)
                {
                    string name = file.Groups[1].Success ? file.Groups[1].Value : file.Groups[2].Value;
                    currentFile = Path.GetFullPath(Path.Combine(folder, name));
                    continue;
                }
                var track = Regex.Match(line, @"^\s*TRACK\s+\d+\s+(\S+)\s*$", RegexOptions.IgnoreCase);
                if (!track.Success) continue;
                foundTrack = true;
                if (!track.Groups[1].Value.Equals("AUDIO", StringComparison.OrdinalIgnoreCase)) continue;
                foundAudio = true;
                if (currentFile is null || !File.Exists(currentFile)) return CueAudioStatus.Missing;
            }
            return foundAudio ? CueAudioStatus.Present : foundTrack ? CueAudioStatus.None : CueAudioStatus.Unknown;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return CueAudioStatus.Unknown;
        }
    }
    public static string? Check(string cue) => Inspect(cue).Error;
    public static CueReport Inspect(string cue)
    {
        try
        {
            if (new FileInfo(cue).Length > 1024 * 1024) return new("The CUE file is unusually large.", 0);
            string folder = Path.GetDirectoryName(Path.GetFullPath(cue))!;
            bool hasFile = false, trackHasIndex = false;
            int tracks = 0, previousTrack = 0;
            foreach (string line in File.ReadLines(cue))
            {
                var file = Regex.Match(line, @"^\s*FILE\s+(?:""([^""\r\n]+)""|(\S+))\s+(\S+)\s*$", RegexOptions.IgnoreCase);
                if (file.Success)
                {
                    string name = file.Groups[1].Success ? file.Groups[1].Value : file.Groups[2].Value;
                    if (!file.Groups[3].Value.Equals("BINARY", StringComparison.OrdinalIgnoreCase)) return new("The built-in PS1 engine requires BINARY track files.", tracks);
                    if (!File.Exists(Path.GetFullPath(Path.Combine(folder, name)))) return new($"Missing track file: {name}", tracks);
                    hasFile = true;
                    continue;
                }
                var track = Regex.Match(line, @"^\s*TRACK\s+(\d+)\s+(\S+)\s*$", RegexOptions.IgnoreCase);
                if (track.Success)
                {
                    if (!hasFile) return new("A TRACK appears before its FILE entry.", tracks);
                    if (tracks > 0 && !trackHasIndex) return new($"Track {previousTrack:00} is missing INDEX 01.", tracks);
                    int number = int.Parse(track.Groups[1].Value);
                    if (number != previousTrack + 1 || number > 99) return new("Track numbers must start at 01 and increase in order.", tracks);
                    previousTrack = number; tracks++; trackHasIndex = false;
                    continue;
                }
                var index = Regex.Match(line, @"^\s*INDEX\s+01\s+(\d+):(\d{2}):(\d{2})\s*$", RegexOptions.IgnoreCase);
                if (index.Success && tracks > 0)
                {
                    if (int.Parse(index.Groups[2].Value) >= 60 || int.Parse(index.Groups[3].Value) >= 75) return new($"Track {previousTrack:00} has an invalid INDEX 01 time.", tracks);
                    trackHasIndex = true;
                }
            }
            if (!hasFile) return new("The CUE does not list any track files.", 0);
            if (tracks == 0) return new("The CUE does not contain any TRACK entries.", 0);
            if (!trackHasIndex) return new($"Track {previousTrack:00} is missing INDEX 01.", tracks);
            return new(null, tracks);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or FormatException or OverflowException)
        {
            return new($"Could not read CUE: {e.Message}", 0);
        }
    }
}
