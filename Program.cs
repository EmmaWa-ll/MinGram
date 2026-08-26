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

var keyVaultUrl = builder.Configuration["KeyVaultUrl"];

if (string.IsNullOrWhiteSpace(keyVaultUrl))
{
    throw new InvalidOperationException(
        "KeyVault URL saknas i konfigurationen.");
}

builder.Configuration.AddAzureKeyVault(
    new Uri(keyVaultUrl),
    new DefaultAzureCredential());


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


// Din BlobService
builder.Services.AddSingleton(sp =>
{
    var blobServiceClient =
        sp.GetRequiredService<BlobServiceClient>();

    return new BlobService(
        blobServiceClient,
        containerName);
});


// Din BildService
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
app.MapGet("/bilder/{namn}", async (
    string namn,
    BildService bildService) =>
{
    var bild =
        await bildService.HamtaEnAsync(namn);

    return bild is not null
        ? Results.Ok(bild)
        : Results.NotFound();
})
.WithName("HamtaBild")
.WithSummary("Hämta en specifik bild — alla roller");


// POST /bilder
// Fotograf och Admin får ladda upp bilder
app.MapPost("/bilder", async (
    IFormFile fil,
    string caption,
    string? taggar,
    HttpRequest req,
    BildService bildService) =>
{
    if (!HarBehorighet(HamtaRoll(req), "Fotograf"))
        return Results.StatusCode(403);

    var bild =
        await bildService.SkapaBildAsync(
            fil,
            caption,
            taggar);

    return Results.Created(
        $"/bilder/{bild.Namn}",
        bild);
})
.DisableAntiforgery()
.WithName("LaddaUppBild")
.WithSummary(
    "Ladda upp bild — kräver Fotograf eller Admin");


// PUT /bilder/{namn}
// Fotograf och Admin får uppdatera caption och taggar
app.MapPut("/bilder/{namn}", async (
    string namn,
    BildUpdate update,
    HttpRequest req,
    BildService bildService) =>
{
    if (!HarBehorighet(HamtaRoll(req), "Fotograf"))
        return Results.StatusCode(403);

    var bild =
        await bildService.UppdateraBildAsync(
            namn,
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
app.MapDelete("/bilder/{namn}", async (
    string namn,
    HttpRequest req,
    BildService bildService) =>
{
    if (!HarBehorighet(HamtaRoll(req), "Admin"))
        return Results.StatusCode(403);

    var borttagen =
        await bildService.RaderaBildAsync(namn);

    return borttagen
        ? Results.NoContent()
        : Results.NotFound();
})
.WithName("RaderaBild")
.WithSummary(
    "Radera bild — kräver Admin");


app.Run();


// ======================================================
// Rollkontroll
// ======================================================

string HamtaRoll(HttpRequest request)
{
    // Easy Auth-header från Azure
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
        (_, "Betraktare") => true,

        ("Fotograf" or "Admin", "Fotograf") => true,

        ("Admin", "Admin") => true,

        _ => false
    };