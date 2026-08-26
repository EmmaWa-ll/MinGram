namespace MinGramApi.Models
{
    public record NyBild(
     string Namn,
     string Caption,
     List<string>? Taggar,
     string Url
 );
}
