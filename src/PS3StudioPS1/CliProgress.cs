using System.Diagnostics;

namespace PS2ClassicsPackageStudio;

internal sealed class CliProgress(Action<BuildProgress> report)
{
    private BuildProgress? _last;
    private string _discId = "";
    private readonly object _sync = new();

    public static void EnableLiveOutput(ProcessStartInfo info) =>
        info.Environment["PYTHONUNBUFFERED"] = "1";

    public void Observe(string line)
    {
        // Both redirected streams call this method. Only known stage messages belong
        // on the progress display; helper percentages refer to individual operations.
        if (line.Length > 1024) return;
        lock (_sync)
        {
            var stage = ReadStage(line.Trim());
            if (stage is null || stage == _last) return;
            _last = stage;
            report(stage);
        }
    }

    private BuildProgress? ReadStage(string message)
    {
        if (message.StartsWith("GAMEID ", StringComparison.Ordinal))
        {
            var id = message[7..].Trim();
            if (id.Length == 9 && id.All(char.IsAsciiLetterOrDigit)) _discId = id;
            return null;
        }

        string stage;
        if (message.StartsWith("Work directory", StringComparison.Ordinal)) stage = "Preparing workspace";
        else if (message.StartsWith("Get GAMEID from ", StringComparison.Ordinal)) stage = "Identifying disc";
        else if (message.StartsWith("Creating SND0", StringComparison.Ordinal)) stage = "Encoding menu audio";
        else if (message == "Creating PARAM.SFO") stage = "Preparing game metadata";
        else if (message == "Creating ISO.BIN.EDAT") stage = "Writing disc identifier";
        else if (message.StartsWith("Installing CONFIG from ", StringComparison.Ordinal)) stage = "Adding compatibility profile";
        else if (message.StartsWith("Copy ", StringComparison.Ordinal) && message.EndsWith("game.iso", StringComparison.OrdinalIgnoreCase))
            stage = "Copying disc image";
        else if (message == "Creating ISO.BIN.ENC") stage = "Encrypting disc image";
        else if (message is "Creating SCEVMC0.VME" or "Creating SCEVMC1.VME")
            return new("Creating virtual memory cards", null, message.EndsWith("0.VME", StringComparison.Ordinal) ? "Card 1 of 2" : "Card 2 of 2");
        else if (message.StartsWith("Creating PKG ", StringComparison.Ordinal)) return new("Writing PS3 package", null, "pop-fe2");
        else return null;

        return new(stage, null, _discId.Length == 0 || stage == "Identifying disc" ? "pop-fe2" : _discId);
    }
}
