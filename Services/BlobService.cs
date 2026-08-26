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
        // Skapa bild
        // ======================================================

        public async Task<Bild> SkapaBildAsync(NyBild nyBild)
        {
            await _containerClient.CreateIfNotExistsAsync();

            // Unikt namn i Blob Storage
            var blobNamn =
                $"{Guid.NewGuid()}-{nyBild.Namn}";

            var blobClient =
                _containerClient.GetBlobClient(blobNamn);

            var metadata = new Dictionary<string, string>
            {
                ["caption"] = nyBild.Caption,
                ["taggar"] = string.Join(
                    ",",
                    nyBild.Taggar ?? new List<string>()),
                ["url"] = nyBild.Url
            };

            // Vi behöver bara en blob att lagra metadata på.
            // Själva bilden ligger på URL:en.
            using var stream =
                new MemoryStream(Array.Empty<byte>());

            var options = new BlobUploadOptions
            {
                Metadata = metadata
            };

            await blobClient.UploadAsync(
                stream,
                options);

            return new Bild(
                blobNamn,
                nyBild.Caption,
                nyBild.Taggar ?? new List<string>(),
                nyBild.Url
            );
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

                var url =
                    blob.Metadata.TryGetValue(
                        "url",
                        out var u)
                    ? u
                    : "";

                bilder.Add(new Bild(
                    blob.Name,
                    caption,
                    taggar,
                    url
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

            var url =
                properties.Value.Metadata.TryGetValue(
                    "url",
                    out var u)
                ? u
                : "";

            return new Bild(
                fileName,
                caption,
                taggar,
                url
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

