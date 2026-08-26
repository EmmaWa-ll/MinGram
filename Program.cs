
using System.Text;
using System.Text.Json;
using Azure.Identity;
using Azure.Storage.Blobs;
using MinGramApi.DTO;
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
// Sätt "KeyVaultKey:KeyVaultURL" i appsettings/App Service Configuration,
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

// -------------------------------------------------------
// In-memory datastore med seed-data
// -------------------------------------------------------
var bilder = new List<Bild>
{
    new(1, "demo.jpg", "Demobild — ersätt med din egen", ["demo", "placeholder"],
        "https://placehold.co/400x300?text=MinGram")
};
var nastaBildId = 2;

// =========================================================
// Bilder (metadata)
// =========================================================

app.MapGet("/bilder", () => bilder)
   .WithName("HamtaBilder")
   .WithSummary("Hämta alla bilder — alla roller");

app.MapGet("/bilder/{id:int}", (int id) =>
{
    var b = bilder.FirstOrDefault(b => b.Id == id);
    return b is not null ? Results.Ok(b) : Results.NotFound();
})
.WithName("HamtaBild")
.WithSummary("Hämta en specifik bild — alla roller");

// Skicka en redan befintlig URL (t.ex. om filen laddats upp separat via /blob/upload)
app.MapPost("/bilder", (NyBild ny, HttpRequest req) =>
{
    if (!HarBehorighet(HamtaRoll(req), "Fotograf")) return Results.StatusCode(403);
    var b = new Bild(nastaBildId++, ny.Namn, ny.Caption, ny.Taggar ?? [], ny.Url);
    bilder.Add(b);
    return Results.Created($"/bilder/{b.Id}", b);
})
.WithName("LaddaUppBild")
.WithSummary("Lägg till bild via URL — kräver Fotograf eller Admin");

app.MapPut("/bilder/{id:int}", (int id, BildUpdate update, HttpRequest req) =>
{
    if (!HarBehorighet(HamtaRoll(req), "Fotograf")) return Results.StatusCode(403);
    var index = bilder.FindIndex(b => b.Id == id);
    if (index < 0) return Results.NotFound();
    bilder[index] = bilder[index] with
    {
        Caption = update.Caption ?? bilder[index].Caption,
        Taggar = update.Taggar ?? bilder[index].Taggar
    };
    return Results.Ok(bilder[index]);
})
.WithName("UppdateraBild")
.WithSummary("Uppdatera bild — kräver Fotograf eller Admin");

// Ta bort bild-posten (raderar inte automatiskt filen i Blob Storage,
// se DELETE /blob/files/{fileName} om ni vill städa bort själva filen också)
app.MapDelete("/bilder/{id:int}", (int id, HttpRequest req) =>
{
    if (!HarBehorighet(HamtaRoll(req), "Admin")) return Results.StatusCode(403);
    var b = bilder.FirstOrDefault(b => b.Id == id);
    if (b is null) return Results.NotFound();
    bilder.Remove(b);
    return Results.NoContent();
})
.WithName("RaderaBild")
.WithSummary("Radera bild — kräver Admin");

// =========================================================
// Blob Storage — själva filerna
// =========================================================

// Lista alla filer som faktiskt ligger i containern
app.MapGet("/blob/files", async (IBlobService blobService, HttpRequest req) =>
{
    if (!HarBehorighet(HamtaRoll(req), "Betraktare")) return Results.StatusCode(403);
    return Results.Ok(await blobService.GetAllFilesAsync());
})
.WithName("HamtaBlobFiler")
.WithSummary("Lista filer i Blob Storage — alla roller");

// Ladda upp en fil direkt till Blob Storage och skapa en bild-post i samma anrop
app.MapPost("/bilder/upload", async (
        IFormFile fil, string caption, string? taggar,
        IBlobService blobService, HttpRequest req) =>
{
    if (!HarBehorighet(HamtaRoll(req), "Fotograf")) return Results.StatusCode(403);

    await using var stream = fil.OpenReadStream();
    var uppladdad = await blobService.UploadFileAsync(stream, fil.FileName, fil.ContentType);

    var taggarLista = string.IsNullOrWhiteSpace(taggar)
        ? new List<string>()
        : taggar.Split(',').Select(t => t.Trim()).ToList();

    var b = new Bild(nastaBildId++, uppladdad.FileName, caption, taggarLista, uppladdad.Url);
    bilder.Add(b);

    return Results.Created($"/bilder/{b.Id}", b);
})
.DisableAntiforgery() // krävs för IFormFile i minimal API (.NET 8+)
.WithName("LaddaUppOchSparaBild")
.WithSummary("Ladda upp bildfil till Blob Storage och skapa bild-post — kräver Fotograf eller Admin");

// Radera en fil direkt i Blob Storage (oberoende av bild-posten)
app.MapDelete("/blob/files/{fileName}", async (string fileName, IBlobService blobService, HttpRequest req) =>
{
    if (!HarBehorighet(HamtaRoll(req), "Admin")) return Results.StatusCode(403);
    await blobService.DeleteFileAsync(fileName);
    return Results.NoContent();
})
.WithName("RaderaBlobFil")
.WithSummary("Radera fil från Blob Storage — kräver Admin");

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
// Datamodeller
// =========================================================

record Bild(int Id, string Namn, string Caption, List<string> Taggar, string Url);
record NyBild(string Namn, string Caption, List<string>? Taggar, string Url);
record BildUpdate(string? Caption, List<string>? Taggar);