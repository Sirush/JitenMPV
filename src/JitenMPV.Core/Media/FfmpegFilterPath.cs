namespace JitenMPV.Core.Media;

public static class FfmpegFilterPath
{
    /// Escapes a path for use inside a filtergraph argument. The filtergraph parser consumes one
    /// level of backslash escaping before the option parser sees ':' as a separator, so both are
    /// doubled here. Arguments reach ffmpeg through ArgumentList, so there is no shell layer on top.
    public static string Escape(string path)
        => "'" + path.Replace(@"\", @"\\").Replace(":", @"\:") + "'";

    /// <summary>
    /// False for a path <see cref="Escape"/> cannot express. A literal apostrophe has no escape
    /// inside ffmpeg's filtergraph quoting - it ends the quoted section and the rest of the graph is
    /// then mis-parsed - so such a path must be routed through a temp copy instead.
    /// </summary>
    public static bool IsEscapable(string path) => !path.Contains('\'');
}
