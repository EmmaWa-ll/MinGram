using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using MinGramApi.DTO;
using MinGramApi.Interfaces;

namespace MinGramApi.Services
{
    public class BlobService : IBlobService
    {
        private readonly BlobContainerClient _containerClient;

        public BlobService(BlobServiceClient blobServiceClient, string containerName)
        {
            _containerClient = blobServiceClient.GetBlobContainerClient(containerName);
            _containerClient.CreateIfNotExists(PublicAccessType.Blob);
        }

        public async Task<List<DtoFile>> GetAllFilesAsync()
        {
            var files = new List<DtoFile>();
            await foreach (BlobItem blobItem in _containerClient.GetBlobsAsync())
            {
                var blobClient = _containerClient.GetBlobClient(blobItem.Name);
                files.Add(new DtoFile
                {
                    FileName = blobItem.Name,
                    ContentType = blobItem.Properties.ContentType,
                    Size = blobItem.Properties.ContentLength ?? 0,
                    Url = blobClient.Uri.ToString()
                });
            }
            return files;
        }

        public async Task<(Stream Content, string? ContentType, string FileName)> GetFileAsync(string fileName)
        {
            var blobClient = _containerClient.GetBlobClient(fileName);
            if (!await blobClient.ExistsAsync())
                throw new FileNotFoundException($"Filen '{fileName}' hittades inte i containern.");

            var response = await blobClient.DownloadAsync();
            return (response.Value.Content, response.Value.Details.ContentType, fileName);
        }

        public async Task<DtoFile> UploadFileAsync(Stream content, string fileName, string? contentType)
        {
            var uniqueName = $"{Guid.NewGuid()}-{fileName}";
            var blobClient = _containerClient.GetBlobClient(uniqueName);

            var headers = new BlobHttpHeaders { ContentType = contentType ?? "application/octet-stream" };
            await blobClient.UploadAsync(content, headers);

            var props = await blobClient.GetPropertiesAsync();

            return new DtoFile
            {
                FileName = uniqueName,
                ContentType = contentType,
                Size = props.Value.ContentLength,
                Url = blobClient.Uri.ToString()
            };
        }

        public async Task DeleteFileAsync(string fileName)
        {
            var blobClient = _containerClient.GetBlobClient(fileName);
            await blobClient.DeleteIfExistsAsync();
        }
    }
}
