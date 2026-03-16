using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.GridFS;
using Nodus.API.Services;

namespace Nodus.API.Endpoints;

// ── DTOs ─────────────────────────────────────────────────────────────────

public sealed record MediaUploadResponse(string FileId, string Url);

// ── Endpoint Registration ─────────────────────────────────────────────────

public static class MediaEndpoints
{
    public static void Map(WebApplication app)
    {
        var grp = app.MapGroup("/api/media").RequireAuthorization();

        // POST /api/media/upload
        // Admin uploads a photo or audio file from a vote to MongoDB GridFS.
        // Returns the GridFS file ID and a stable URL for use as RemotePhotoUrl / RemoteAudioUrl.
        grp.MapPost("/upload", async (
            HttpRequest request,
            MongoDbService mongo,
            IMongoDatabase db) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest(new { error = "Expected multipart/form-data." });

            var form    = await request.ReadFormAsync();
            var file    = form.Files.GetFile("file");
            var voteId  = form["voteId"].ToString();
            var field   = form["field"].ToString(); // "photo" or "audio"

            if (file is null || string.IsNullOrEmpty(voteId) || string.IsNullOrEmpty(field))
                return Results.BadRequest(new { error = "Required: file, voteId, field." });

            // Allowed content types
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "image/jpeg", "image/png", "image/webp",   // photos
                "audio/mpeg", "audio/mp4", "audio/ogg", "audio/webm" // audio
            };

            if (!allowed.Contains(file.ContentType))
                return Results.BadRequest(new { error = $"Unsupported media type: {file.ContentType}" });

            // Max size: 10 MB
            if (file.Length > 10 * 1024 * 1024)
                return Results.BadRequest(new { error = "File exceeds 10 MB limit." });

            // Upload to GridFS
            var gridFs   = new GridFSBucket(db, new GridFSBucketOptions { BucketName = "media" });
            var metadata = new BsonDocument
            {
                { "voteId", voteId },
                { "field",  field  },
                { "originalName", file.FileName }
            };

            ObjectId fileId;
            await using (var stream = file.OpenReadStream())
            {
                fileId = await gridFs.UploadFromStreamAsync(
                    file.FileName,
                    stream,
                    new GridFSUploadOptions { Metadata = metadata });
            }

            // Update vote's remote URL in MongoDB
            var urlField = field.ToLowerInvariant() == "photo" ? "remotePhotoUrl" : "remoteAudioUrl";
            var fileUrl  = $"/api/media/{fileId}";

            await mongo.Votes.UpdateOneAsync(
                Builders<Nodus.API.Models.VoteDocument>.Filter.Eq("_id", voteId),
                Builders<Nodus.API.Models.VoteDocument>.Update.Set(urlField, fileUrl));

            return Results.Ok(new MediaUploadResponse(fileId.ToString(), fileUrl));
        })
        .DisableAntiforgery()
        .WithName("UploadMedia")
        .WithSummary("Upload vote photo/audio to GridFS; updates vote's RemotePhotoUrl or RemoteAudioUrl.")
        .Produces<MediaUploadResponse>()
        .ProducesProblem(400)
        .ProducesProblem(401);

        // GET /api/media/{fileId}
        // Streams the GridFS file back to the client (browser or Admin dashboard).
        grp.MapGet("/{fileId}", async (string fileId, IMongoDatabase db) =>
        {
            try
            {
                var gridFs    = new GridFSBucket(db, new GridFSBucketOptions { BucketName = "media" });
                var objectId  = ObjectId.Parse(fileId);
                var info      = await gridFs.Find(Builders<GridFSFileInfo>.Filter.Eq(f => f.Id, objectId))
                    .FirstOrDefaultAsync();

                if (info is null) return Results.NotFound();

                var stream = await gridFs.OpenDownloadStreamAsync(objectId);
                return Results.Stream(stream, info.Metadata?.GetValue("contentType", "application/octet-stream").AsString);
            }
            catch (FormatException)   { return Results.BadRequest(new { error = "Invalid file ID format." }); }
            catch (GridFSFileNotFoundException) { return Results.NotFound(); }
        })
        .WithName("GetMedia")
        .WithSummary("Stream a media file from GridFS.")
        .Produces(200)
        .ProducesProblem(404);
    }
}
