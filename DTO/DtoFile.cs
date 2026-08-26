namespace MinGramApi.DTO
{
    public class DtoFile
    {
        public string FileName { get; set; } = null!;
        public string? ContentType { get; set; }
        public long Size { get; set; }
        public string Url { get; set; } = null!;
    }
}
