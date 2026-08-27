namespace MinGramApi.Models
{
    public record Bild(
    string Id,
    string Namn,
    string Caption,
    List<string> Taggar,
    string Url
);
}
