namespace MinGramApi.Models
{
    public record Bild(
    string Namn,
    string Caption,
    List<string> Taggar,
    string Url
);
}
