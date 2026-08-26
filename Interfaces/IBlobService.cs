using MinGramApi.DTO;

namespace MinGramApi.Interfaces
{
    public interface IBlobService
    {
        Task<List<DtoFile>> GetAllFilesAsync();
        Task<(Stream Content, string? ContentType, string FileName)> GetFileAsync(string fileName);
        Task<DtoFile> UploadFileAsync(Stream content, string fileName, string? contentType);
        Task DeleteFileAsync(string fileName);
    }
}
