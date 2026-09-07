namespace ValorantBot.Models;

/// <summary>Severity 1 is a footnote, 3 is the story of the match.</summary>
public record Highlight(string Kind, string Text, int Severity);

public class MatchHighlights
{
    public static readonly MatchHighlights Empty = new();
    public List<Highlight> Items { get; init; } = [];
}
