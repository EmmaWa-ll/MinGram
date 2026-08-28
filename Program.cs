using System.Text;
using System.Text.Json;
using Azure.Identity;
using Azure.Storage.Blobs;
using MinGramApi.Models;
using MinGramApi.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();


// ======================================================
// Key Vault
// ======================================================

if (!builder.Environment.IsDevelopment())
{
    var keyVaultUrl = builder.Configuration["KeyVaultUrl"];

    if (string.IsNullOrWhiteSpace(keyVaultUrl))
    {
        throw new InvalidOperationException(
            "KeyVault URL saknas i konfigurationen.");
    }

    builder.Configuration.AddAzureKeyVault(
        new Uri(keyVaultUrl),
        new DefaultAzureCredential());
}
// ======================================================
// Blob Storage
// ======================================================

// Connection string hämtas från Key Vault
var blobConnectionString =
    builder.Configuration["BlobStorageConnectionString"];

if (string.IsNullOrWhiteSpace(blobConnectionString))
{
    throw new InvalidOperationException(
        "BlobStorageConnectionString saknas i Key Vault.");
}

// Container-namn
var containerName =
    builder.Configuration["BlobStorage:ContainerName"]
    ?? "mingram-bilder";


// BlobServiceClient
builder.Services.AddSingleton(
    new BlobServiceClient(blobConnectionString));


// BlobService
builder.Services.AddSingleton(sp =>
{
    var blobServiceClient =
        sp.GetRequiredService<BlobServiceClient>();

    return new BlobService(
        blobServiceClient,
        containerName);
});


// BildService
builder.Services.AddSingleton<BildService>();


// ======================================================
// CORS
// ======================================================

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


var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

app.UseCors("MinGramPolicy");


// ======================================================
// Bilder
// ======================================================


// GET /bilder
// Alla roller får se bilder
app.MapGet("/bilder", async (
    BildService bildService) =>
{
    var bilder =
        await bildService.HamtaAllaAsync();

    return Results.Ok(bilder);
})
.WithName("HamtaBilder")
.WithSummary("Hämta alla bilder — alla roller");


// GET /bilder/{namn}
// Alla roller får hämta en specifik bild
app.MapGet("/bilder/{id}", async (
    string id,
    BildService bildService) =>
{
    var bild =
        await bildService.HamtaEnAsync(id);

    return bild is not null
        ? Results.Ok(bild)
        : Results.NotFound();
})
.WithName("HamtaBild")
.WithSummary("Hämta en specifik bild — alla roller");


// POST /bilder
// Fotograf och Admin får lägga till bilder
app.MapPost("/bilder", async (
    NyBild nyBild,
    HttpRequest req,
    BildService bildService) =>
{
    if (!HarBehorighet(HamtaRoll(req), "Fotograf"))
        return Results.StatusCode(403);

    var bild =
        await bildService.SkapaBildAsync(nyBild);

    return Results.Created(
        $"/bilder/{bild.Id}",
        bild);
})
.WithName("LaggTillBild")
.WithSummary(
    "Lägg till bild — kräver Fotograf eller Admin");

// PUT /bilder/{namn}
// Fotograf och Admin får uppdatera caption och taggar
app.MapPut("/bilder/{id}", async (
    string id,
    BildUpdate update,
    HttpRequest req,
    BildService bildService) =>
{
    if (!HarBehorighet(HamtaRoll(req), "Fotograf"))
        return Results.StatusCode(403);

    var bild =
        await bildService.UppdateraBildAsync(
            id,
            update);

    return bild is not null
        ? Results.Ok(bild)
        : Results.NotFound();
})
.WithName("UppdateraBild")
.WithSummary(
    "Uppdatera bild — kräver Fotograf eller Admin");

// DELETE /bilder/{namn}
// Bara Admin får ta bort bilder
app.MapDelete("/bilder/{id}", async (
    string id,
    HttpRequest req,
    BildService bildService) =>
{
    if (!HarBehorighet(HamtaRoll(req), "Admin"))
        return Results.StatusCode(403);

    var borttagen =
        await bildService.RaderaBildAsync(id);

    return borttagen
        ? Results.NoContent()
        : Results.NotFound();
})
.WithName("RaderaBild")
.WithSummary(
    "Radera bild — kräver Admin");


app.Run();


string HamtaRoll(HttpRequest request)
{
    // ======================================================
    // Demo-roll
    // Används eftersom Entra ID inte kan användas
    // med skolkontot.
    // ======================================================

    var demoRoll =
        request.Headers["X-Demo-Role"]
            .FirstOrDefault();

    if (!string.IsNullOrWhiteSpace(demoRoll))
    {
        if (demoRoll == "Admin" ||
            demoRoll == "Fotograf" ||
            demoRoll == "Betraktare")
        {
            return demoRoll;
        }
    }




    // ======================================================
    // Easy Auth / Entra ID
    // ======================================================

    var header =
        request.Headers["X-MS-CLIENT-PRINCIPAL"]
            .FirstOrDefault();

    // Lokalt utan Easy Auth
    if (string.IsNullOrEmpty(header))
        return "Admin";

    try
    {
        var json =
            Encoding.UTF8.GetString(
                Convert.FromBase64String(header));

        using var doc =
            JsonDocument.Parse(json);

        foreach (var claim in
            doc.RootElement
                .GetProperty("claims")
                .EnumerateArray())
        {
            if (claim.GetProperty("typ").GetString() == "roles")
            {
                return claim
                    .GetProperty("val")
                    .GetString()
                    ?? "Betraktare";
            }
        }
    }
    catch
    {
    }

    return "Betraktare";



}

// ======================================================
// Behörighet
// Betraktare < Fotograf < Admin
// ======================================================

bool HarBehorighet(
    string roll,
    string kravRoll) =>
    (roll, kravRoll) switch
    {
        // Alla roller får läsa
        (_, "Betraktare") => true,

        // Fotograf och Admin får skapa/uppdatera
        ("Fotograf" or "Admin", "Fotograf") => true,

        // Endast Admin får radera
        ("Admin", "Admin") => true,

        // Allt annat nekas
        _ => false
    };