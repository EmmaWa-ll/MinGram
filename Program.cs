using System.Text;
using System.Text.Json;
using Azure.Identity;
using Azure.Storage.Blobs;
using MinGramApi.Interfaces;
using MinGramApi.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// -------------------------------------------------------
// CORS — huvudkonfigurationen görs i Azure Portal:
// App Service → API → CORS → lägg till din frontend-URL.
// Den här koden hanterar CORS lokalt under utveckling.
// -------------------------------------------------------
builder.Services.AddCors(options =>
{
    options.AddPolicy("MinGramPolicy", policy =>
    {
        var origins = builder.Configuration
                             .GetSection("AllowedOrigins")
                             .Get<string[]>() ?? [];
        policy.WithOrigins(origins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// =========================================================
// Key Vault + Blob Storage-koppling
// =========================================================
// Kräver INTE att ni skapar Entra ID-användare — bara att App Service
// har en Managed Identity (Identity → System assigned → On) och att
// den identityn har fått "Get/List"-access till Key Vault-secreten
// (Key Vault → Access policies, eller Access control (IAM) om ni kör
// RBAC-läge på Key Vault).
//
// Sätt "KeyVaultUrl" i appsettings/App Service Configuration,
// t.ex. https://ditt-keyvault-namn.vault.azure.net/
// ---------------------------------------------------------
var keyVaultUrl = builder.Configuration["KeyVaultUrl"];
if (string.IsNullOrWhiteSpace(keyVaultUrl))
{
    throw new InvalidOperationException("KeyVault URL saknas i konfigurationen.");
}

builder.Configuration.AddAzureKeyVault(
    new Uri(keyVaultUrl),
    new DefaultAzureCredential());

// Secretens namn i Key Vault ska vara "BlobStorageConnectionString"
// och innehålla er Blob Storage connection string.
var blobConnectionString = builder.Configuration["BlobStorageConnectionString"];
if (string.IsNullOrWhiteSpace(blobConnectionString))
{
    throw new InvalidOperationException(
        "Hittade ingen Blob Storage-connection string i Key Vault. " +
        "Kontrollera secret-namnet 'BlobStorageConnectionString'.");
}

var containerName = builder.Configuration["BlobStorage:ContainerName"] ?? "mingram-bilder";

builder.Services.AddSingleton(new BlobServiceClient(blobConnectionString));
builder.Services.AddScoped<IBlobService>(sp =>
{
    var client = sp.GetRequiredService<BlobServiceClient>();
    return new BlobService(client, containerName);
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors("MinGramPolicy");

// =========================================================
// Bilder — läses och sparas direkt mot Blob Storage
// =========================================================

// Alla roller får lista alla bilder som ligger i Blob Storage
app.MapGet("/bilder", async (IBlobService blobService) =>
{
    var blobFiler = await blobService.GetAllFilesAsync();

    var resultat = blobFiler.Select(blob => new Bild(blob.FileName, blob.Url));

    return Results.Ok(resultat);
})
.WithName("HamtaBilder")
.WithSummary("Hämta alla bilder — läser från Blob Storage, alla roller");


// Fotograf och Admin får ladda upp en bild till Blob Storage
app.MapPost("/bilder", async (
    IFormFile fil,
    IBlobService blobService,
    HttpRequest req) =>
{
    if (!HarBehorighet(HamtaRoll(req), "Fotograf"))
        return Results.StatusCode(403);

    await using var stream = fil.OpenReadStream();

    var uppladdad = await blobService.UploadFileAsync(
        stream,
        fil.FileName,
        fil.ContentType
    );

    var b = new Bild(uppladdad.FileName, uppladdad.Url);

    return Results.Created(uppladdad.Url, b);
})
.DisableAntiforgery() // krävs för IFormFile i minimal API (.NET 8+)
.WithName("LaddaUppBild")
.WithSummary("Ladda upp bild till Blob Storage — kräver Fotograf eller Admin");


// Bara Admin får ta bort en bild från Blob Storage
app.MapDelete("/bilder/{fileName}", async (
    string fileName,
    IBlobService blobService,
    HttpRequest req) =>
{
    if (!HarBehorighet(HamtaRoll(req), "Admin"))
        return Results.StatusCode(403);

    await blobService.DeleteFileAsync(fileName);

    return Results.NoContent();
})
.WithName("RaderaBild")
.WithSummary("Radera bild från Blob Storage — kräver Admin");

app.Run();

// =========================================================
// Rollkontroll
// =========================================================

// Läser rollen i den här ordningen:
//   1) Easy Auth-headern som Azure injicerar efter Entra ID-inloggning
//   2) X-Demo-Role — reservlösning om Entra ID inte var möjligt att sätta upp
//   3) "Admin" — lokal utveckling helt utan headers
string HamtaRoll(HttpRequest request)
{
    var easyAuthHeader = request.Headers["X-MS-CLIENT-PRINCIPAL"].FirstOrDefault();
    if (!string.IsNullOrEmpty(easyAuthHeader))
    {
        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(easyAuthHeader));
            using var doc = JsonDocument.Parse(json);
            foreach (var claim in doc.RootElement.GetProperty("claims").EnumerateArray())
            {
                if (claim.GetProperty("typ").GetString() == "roles")
                    return claim.GetProperty("val").GetString() ?? "Betraktare";
            }
        }
        catch { }
        return "Betraktare";
    }

    var demoRole = request.Headers["X-Demo-Role"].FirstOrDefault();
    if (!string.IsNullOrEmpty(demoRole)) return demoRole;

    return "Admin"; // lokal dev utan headers alls
}

// Hierarki: Betraktare < Fotograf < Admin
bool HarBehorighet(string roll, string kravRoll) => (roll, kravRoll) switch
{
    (_, "Betraktare") => true,
    ("Fotograf" or "Admin", "Fotograf") => true,
    ("Admin", "Admin") => true,
    _ => false
};

// =========================================================
// Datamodell
// =========================================================

record Bild(string Namn, string Url);