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

            // Skapa unikt id
            var id = Guid.NewGuid().ToString();

            // Blobnamnet innehåller både id och originalnamn
            var blobNamn =
                $"{id}-{nyBild.Namn}";

            var blobClient =
                _containerClient.GetBlobClient(blobNamn);

            var metadata = new Dictionary<string, string>
            {
                ["id"] = id,
                ["namn"] = nyBild.Namn,
                ["caption"] = nyBild.Caption,
                ["taggar"] = string.Join(
                    ",",
                    nyBild.Taggar ?? new List<string>()),
                ["url"] = nyBild.Url
            };

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
                id,
                nyBild.Namn,
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
                var id =
                    blob.Metadata.TryGetValue("id", out var i)
                        ? i
                        : "";

                var namn =
                    blob.Metadata.TryGetValue("namn", out var n)
                        ? n
                        : blob.Name;

                var caption =
                    blob.Metadata.TryGetValue("caption", out var c)
                        ? c
                        : "";

                var taggar =
                    blob.Metadata.TryGetValue("taggar", out var t)
                        ? t.Split(
                                ",",
                                StringSplitOptions.RemoveEmptyEntries)
                            .Select(x => x.Trim())
                            .ToList()
                        : new List<string>();

                var url =
                    blob.Metadata.TryGetValue("url", out var u)
                        ? u
                        : "";

                bilder.Add(new Bild(
                    id,
                    namn,
                    caption,
                    taggar,
                    url
                ));
            }

            return bilder;
        }

        // ======================================================
        // Hämta en bild via id
        // ======================================================

        public async Task<Bild?> HamtaEnAsync(string id)
        {
            var options = new GetBlobsOptions
            {
                Traits = BlobTraits.Metadata
            };

            await foreach (
                var blob in _containerClient.GetBlobsAsync(options))
            {
                if (!blob.Metadata.TryGetValue("id", out var blobId))
                    continue;

                if (blobId != id)
                    continue;

                var namn =
                    blob.Metadata.TryGetValue("namn", out var n)
                        ? n
                        : blob.Name;

                var caption =
                    blob.Metadata.TryGetValue("caption", out var c)
                        ? c
                        : "";

                var taggar =
                    blob.Metadata.TryGetValue("taggar", out var t)
                        ? t.Split(
                                ",",
                                StringSplitOptions.RemoveEmptyEntries)
                            .Select(x => x.Trim())
                            .ToList()
                        : new List<string>();

                var url =
                    blob.Metadata.TryGetValue("url", out var u)
                        ? u
                        : "";

                return new Bild(
                    id,
                    namn,
                    caption,
                    taggar,
                    url
                );
            }

            return null;
        }

        // ======================================================
        // Uppdatera caption och taggar via id
        // ======================================================

        public async Task<Bild?> UppdateraMetadataAsync(
            string id,
            BildUpdate update)
        {
            var blobClient =
                await HamtaBlobClientViaIdAsync(id);

            if (blobClient is null)
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

            return await HamtaEnAsync(id);
        }

        // ======================================================
        // Radera bild via id
        // ======================================================

        public async Task<bool> DeleteAsync(string id)
        {
            var blobClient =
                await HamtaBlobClientViaIdAsync(id);

            if (blobClient is null)
                return false;

            var result =
                await blobClient.DeleteIfExistsAsync();

            return result.Value;
        }

        // ======================================================
        // Hjälpmetod: hitta rätt blob från id
        // ======================================================

        private async Task<BlobClient?> HamtaBlobClientViaIdAsync(
            string id)
        {
            var options = new GetBlobsOptions
            {
                Traits = BlobTraits.Metadata
            };

            await foreach (
                var blob in _containerClient.GetBlobsAsync(options))
            {
                if (blob.Metadata.TryGetValue(
                        "id",
                        out var blobId)
                    && blobId == id)
                {
                    return _containerClient
                        .GetBlobClient(blob.Name);
                }
            }

            return null;
        }
    }

}

