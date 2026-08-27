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
        public async Task<Bild?> HamtaEnAsync(string id)
        {
            return await _blobService.HamtaEnAsync(id);
        }

        // POST
        public async Task<Bild> SkapaBildAsync(NyBild nyBild)
        {
            return await _blobService.SkapaBildAsync(nyBild);
        }

        // PUT
        public async Task<Bild?> UppdateraBildAsync(
            string id,
            BildUpdate update)
        {
            return await _blobService
                .UppdateraMetadataAsync(id, update);
        }

        // DELETE
        public async Task<bool> RaderaBildAsync(string id)
        {
            return await _blobService.DeleteAsync(id);
        }
    }
}
