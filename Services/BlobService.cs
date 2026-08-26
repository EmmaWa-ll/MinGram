using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using MinGramApi.Models;


namespace MinGramApi.Services
{
    public class BlobService
    {

        private readonly BlobContainerClient _containerClient;

        public BlobService(
            BlobServiceClient blobServiceClient,
            string containerName)
        {
            _containerClient =
                blobServiceClient.GetBlobContainerClient(containerName);
        }

        // ======================================================
        // Ladda upp bild
        // ======================================================

        public async Task<string> UploadAsync(
            string fileName,
            Stream content,
            string? contentType,
            string caption,
            List<string> taggar)
        {
            await _containerClient.CreateIfNotExistsAsync();

            var uniqueName = $"{Guid.NewGuid()}-{fileName}";

            var blobClient =
                _containerClient.GetBlobClient(uniqueName);

            var options = new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders
                {
                    ContentType =
                        contentType ?? "application/octet-stream"
                },

                Metadata = new Dictionary<string, string>
                {
                    ["caption"] = caption,
                    ["taggar"] = string.Join(",", taggar)
                }
            };

            await blobClient.UploadAsync(content, options);

            return uniqueName;
        }

        // ======================================================
        // Hämta alla bilder
        // ======================================================

        public async Task<List<Bild>> HamtaAllaAsync()
        {
            var bilder = new List<Bild>();

            var options = new GetBlobsOptions
            {
                Traits = BlobTraits.Metadata
            };

            await foreach (
                var blob in _containerClient.GetBlobsAsync(options))
            {
                var caption =
                    blob.Metadata.TryGetValue(
                        "caption",
                        out var c)
                    ? c
                    : "";

                var taggar =
                    blob.Metadata.TryGetValue(
                        "taggar",
                        out var t)
                    ? t.Split(
                            ",",
                            StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim())
                        .ToList()
                    : new List<string>();

                var blobClient =
                    _containerClient.GetBlobClient(blob.Name);

                bilder.Add(new Bild(
                    blob.Name,
                    caption,
                    taggar,
                    blobClient.Uri.ToString()
                ));
            }

            return bilder;
        }

        // ======================================================
        // Hämta en bild
        // ======================================================

        public async Task<Bild?> HamtaEnAsync(string fileName)
        {
            var blobClient =
                _containerClient.GetBlobClient(fileName);

            if (!await blobClient.ExistsAsync())
                return null;

            var properties =
                await blobClient.GetPropertiesAsync();

            var caption =
                properties.Value.Metadata.TryGetValue(
                    "caption",
                    out var c)
                ? c
                : "";

            var taggar =
                properties.Value.Metadata.TryGetValue(
                    "taggar",
                    out var t)
                ? t.Split(
                        ",",
                        StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .ToList()
                : new List<string>();

            return new Bild(
                fileName,
                caption,
                taggar,
                blobClient.Uri.ToString()
            );
        }

        // ======================================================
        // Uppdatera caption och taggar
        // ======================================================

        public async Task<Bild?> UppdateraMetadataAsync(
            string fileName,
            BildUpdate update)
        {
            var blobClient =
                _containerClient.GetBlobClient(fileName);

            if (!await blobClient.ExistsAsync())
                return null;

            var properties =
                await blobClient.GetPropertiesAsync();

            var metadata =
                new Dictionary<string, string>(
                    properties.Value.Metadata);

            if (update.Caption is not null)
            {
                metadata["caption"] =
                    update.Caption;
            }

            if (update.Taggar is not null)
            {
                metadata["taggar"] =
                    string.Join(",", update.Taggar);
            }

            await blobClient.SetMetadataAsync(metadata);

            return await HamtaEnAsync(fileName);
        }

        // ======================================================
        // Radera bild
        // ======================================================

        public async Task<bool> DeleteAsync(string fileName)
        {
            var result =
                await _containerClient
                    .GetBlobClient(fileName)
                    .DeleteIfExistsAsync();

            return result.Value;
        }
    }

}

