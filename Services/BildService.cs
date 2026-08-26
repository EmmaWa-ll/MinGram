using MinGramApi.Models;

namespace MinGramApi.Services
{
    public class BildService
    {
        private readonly BlobService _blobService;

        public BildService(BlobService blobService)
        {
            _blobService = blobService;
        }

        // GET alla
        public async Task<List<Bild>> HamtaAllaAsync()
        {
            return await _blobService.HamtaAllaAsync();
        }

        // GET en
        public async Task<Bild?> HamtaEnAsync(string namn)
        {
            return await _blobService.HamtaEnAsync(namn);
        }

        // POST
        public async Task<Bild> SkapaBildAsync(
     IFormFile fil,
     string caption,
     string? taggar)
        {
            var taggLista = string.IsNullOrWhiteSpace(taggar)
                ? new List<string>()
                : taggar
                    .Split(',')
                    .Select(t => t.Trim())
                    .ToList();

            await using var stream = fil.OpenReadStream();

            var blobNamn = await _blobService.UploadAsync(
                fil.FileName,
                stream,
                fil.ContentType,
                caption,
                taggLista
            );

            var bild =
                await _blobService.HamtaEnAsync(blobNamn);

            return bild!;
        }

        // PUT
        public async Task<Bild?> UppdateraBildAsync(
            string namn,
            BildUpdate update)
        {
            return await _blobService
                .UppdateraMetadataAsync(namn, update);
        }

        // DELETE
        public async Task<bool> RaderaBildAsync(string namn)
        {
            return await _blobService.DeleteAsync(namn);
        }
    }
}
