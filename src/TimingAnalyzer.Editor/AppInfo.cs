namespace TimingAnalyzer.Editor;

/// <summary>Name, author and home of the editor, for the window title and the About box.</summary>
public static class AppInfo
{
    public const string Name = "TempoMap";
    public const string Tagline = "timing editor for osu!";
    public const string Author = "C4P741N";
    public const string Repository = "https://github.com/c4p741nth/TempoMap";
    /// <summary>The title bar's credit with no song open.</summary>
    public const string Title = Name + " - " + Tagline + " by " + Author;
    /// <summary>The credit after a song's name in the title bar.</summary>
    public const string ShortTitle = Name + " by " + Author;
}
