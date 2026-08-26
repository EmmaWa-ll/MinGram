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

// -------------------------------------------------------
// In-memory metadata (caption/taggar) kopplat till blob-filnamn.
// Själva bildfilerna ligger i Blob Storage, containern "mingram-bilder".
// -------------------------------------------------------
var bilder = new List<Bild>
{
    new(
        1,
        "demo.jpg",
        "Demobild",
        new List<string> { "demo" },
        "https://placehold.co/400x300?text=MinGram"
    )
};

var nastaBildId = 2;

// =========================================================
// Bilder
// =========================================================

// Läser alla bilder direkt från Blob Storage och matchar mot
// eventuell metadata (caption/taggar) som finns sparad i minnet.
app.MapGet("/bilder", async (IBlobService blobService) =>
{
    var blobFiler = await blobService.GetAllFilesAsync();

    var resultat = blobFiler.Select(blob =>
    {
        var befintlig = bilder.FirstOrDefault(b => b.Namn == blob.FileName);

        return befintlig is not null
            ? befintlig with { Url = blob.Url }
            : new Bild(
                nastaBildId++,
                blob.FileName,
                "",
                new List<string>(),
                blob.Url
              );
    }).ToList();

    // Metadata-poster som saknar motsvarande blob (t.ex. demo-posten) visas också
    var utanBlob = bilder.Where(b => blobFiler.All(f => f.FileName != b.Namn));
    resultat.AddRange(utanBlob);

    return Results.Ok(resultat);
})
.WithName("HamtaBilder")
.WithSummary("Hämta alla bilder — läser från Blob Storage, alla roller");

app.MapGet("/bilder/{id:int}", (int id) =>
{
    var b = bilder.FirstOrDefault(b => b.Id == id);

    return b is not null
        ? Results.Ok(b)
        : Results.NotFound();
})
.WithName("HamtaBild")
.WithSummary("Hämta en specifik bild — alla roller");


// Fotograf och Admin får ladda upp en bildfil till Blob Storage
app.MapPost("/bilder", async (
    IFormFile fil,
    string caption,
    string? taggar,
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

    var taggarLista = string.IsNullOrWhiteSpace(taggar)
        ? new List<string>()
        : taggar
            .Split(',')
            .Select(t => t.Trim())
            .ToList();

    var b = new Bild(
        nastaBildId++,
        uppladdad.FileName,
        caption,
        taggarLista,
        uppladdad.Url
    );

    bilder.Add(b);

    return Results.Created($"/bilder/{b.Id}", b);
})
.DisableAntiforgery() // krävs för IFormFile i minimal API (.NET 8+)
.WithName("LaddaUppBild")
.WithSummary("Ladda upp bild till Blob Storage — kräver Fotograf eller Admin");


// Fotograf och Admin får uppdatera caption och taggar
app.MapPut("/bilder/{id:int}", (int id, BildUpdate update, HttpRequest req) =>
{
    if (!HarBehorighet(HamtaRoll(req), "Fotograf"))
        return Results.StatusCode(403);

    var index = bilder.FindIndex(b => b.Id == id);

    if (index < 0)
        return Results.NotFound();

    bilder[index] = bilder[index] with
    {
        Caption = update.Caption ?? bilder[index].Caption,
        Taggar = update.Taggar ?? bilder[index].Taggar
    };

    return Results.Ok(bilder[index]);
})
.WithName("UppdateraBild")
.WithSummary("Uppdatera bild — kräver Fotograf eller Admin");


// Bara Admin får ta bort bild-posten
app.MapDelete("/bilder/{id:int}", (int id, HttpRequest req) =>
{
    if (!HarBehorighet(HamtaRoll(req), "Admin"))
        return Results.StatusCode(403);

    var b = bilder.FirstOrDefault(b => b.Id == id);

    if (b is null)
        return Results.NotFound();

    bilder.Remove(b);

    return Results.NoContent();
})
.WithName("RaderaBild")
.WithSummary("Radera bild — kräver Admin");

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
record BildUpdate(string? Caption, List<string>? Taggar);