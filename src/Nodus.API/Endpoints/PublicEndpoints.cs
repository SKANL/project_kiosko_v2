using MongoDB.Driver;
using Nodus.API.Models;
using Nodus.API.Services;

namespace Nodus.API.Endpoints;

public static class PublicEndpoints
{
    public static void Map(WebApplication app)
    {
        var grp = app.MapGroup("/api/public").RequireCors("AllowAll");

        // GET /api/public/event/{id}
        // Web App calls this to check if event exists and is open
        grp.MapGet("/event/{id}", async (string id, MongoDbService mongo) =>
        {
            var evt = await mongo.Events.Find(Builders<EventDocument>.Filter.Eq(e => e.Id, id)).FirstOrDefaultAsync();
            if (evt is null) return Results.NotFound();

            return Results.Ok(new
            {
                EventId = evt.Id,
                Name = evt.Name,
                Institution = evt.Institution,
                Categories = evt.Categories,
                MaxProjects = evt.MaxProjects,
                IsOpen = evt.FinishedAt == null
            });
        });

        // POST /api/public/projects
        // Web App calls this to register a new project in the cloud
        grp.MapPost("/projects", async (ProjectDocument req, MongoDbService mongo) =>
        {
            var evt = await mongo.Events.Find(Builders<EventDocument>.Filter.Eq(e => e.Id, req.EventId)).FirstOrDefaultAsync();
            if (evt is null) return Results.NotFound("Event not found");
            if (evt.FinishedAt != null) return Results.StatusCode(409); // Registration closed

            // Check project limit
            var count = await mongo.Projects.CountDocumentsAsync(Builders<ProjectDocument>.Filter.Eq(p => p.EventId, req.EventId));
            if (count >= evt.MaxProjects) return Results.StatusCode(409);

            // Generate unique code PROJ-XYZ
            req.Id = await GenerateUniqueCodeAsync(mongo, req.EventId);
            req.EditToken = Guid.NewGuid().ToString("N");
            req.SyncedAtUtc = DateTime.UtcNow;

            await mongo.Projects.InsertOneAsync(req);

            return Results.Ok(new
            {
                ProjectId = req.Id,
                ProjectName = req.Name,
                QrPayload = $"nodus://vote?pid={req.Id}",
                StandNumber = req.StandNumber,
                EditToken = req.EditToken,
                EditUrl = $"https://project-kiosko-v2.vercel.app/edit?token={req.EditToken}&cloudApi=https://nodusapi-kk2jf5eg.b4a.run"
            });
        });

        // GET /api/public/projects/edit/{token}
        grp.MapGet("/projects/edit/{token}", async (string token, MongoDbService mongo) =>
        {
            var project = await mongo.Projects.Find(Builders<ProjectDocument>.Filter.Eq(p => p.EditToken, token)).FirstOrDefaultAsync();
            if (project is null) return Results.NotFound();

            var evt = await mongo.Events.Find(Builders<EventDocument>.Filter.Eq(e => e.Id, project.EventId)).FirstOrDefaultAsync();

            return Results.Ok(new
            {
                EventId = project.EventId,
                ProjectId = project.Id,
                Name = project.Name,
                Category = project.Category,
                Description = project.Description,
                GithubLink = project.GithubLink,
                TeamMembers = project.TeamMembers,
                IsEventOpen = evt?.FinishedAt == null
            });
        });

        // PUT /api/public/projects/edit/{token}
        grp.MapPut("/projects/edit/{token}", async (string token, ProjectDocument req, MongoDbService mongo) =>
        {
            var existing = await mongo.Projects.Find(Builders<ProjectDocument>.Filter.Eq(p => p.EditToken, token)).FirstOrDefaultAsync();
            if (existing is null) return Results.NotFound();

            var evt = await mongo.Events.Find(Builders<EventDocument>.Filter.Eq(e => e.Id, existing.EventId)).FirstOrDefaultAsync();
            if (evt?.FinishedAt != null) return Results.StatusCode(410); // Gone

            existing.Name = req.Name;
            existing.Category = req.Category;
            existing.Description = req.Description;
            existing.GithubLink = req.GithubLink;
            existing.TeamMembers = req.TeamMembers;
            existing.SyncedAtUtc = DateTime.UtcNow;

            await mongo.Projects.ReplaceOneAsync(Builders<ProjectDocument>.Filter.Eq(p => p.Id, existing.Id), existing);

            return Results.NoContent();
        });

        // GET /api/public/projects/{pid}
        grp.MapGet("/projects/{pid}", async (string pid, MongoDbService mongo) =>
        {
            var project = await mongo.Projects.Find(Builders<ProjectDocument>.Filter.Eq(p => p.Id, pid)).FirstOrDefaultAsync();
            if (project is null) return Results.NotFound();
            return Results.Ok(project);
        });
        
        // GET /api/public/projects?eventId={eventId}
        // Admin calls this to pull new projects from the cloud
        grp.MapGet("/projects", async (string eventId, MongoDbService mongo) =>
        {
            var projects = await mongo.Projects.Find(Builders<ProjectDocument>.Filter.Eq(p => p.EventId, eventId)).ToListAsync();
            return Results.Ok(projects);
        });
    }

    private static async Task<string> GenerateUniqueCodeAsync(MongoDbService mongo, string eventId)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        while (true)
        {
            var code = new string(Enumerable.Range(0, 3)
                .Select(_ => alphabet[Random.Shared.Next(alphabet.Length)])
                .ToArray());
            var full = $"PROJ-{code}";
            
            var existing = await mongo.Projects.Find(Builders<ProjectDocument>.Filter.And(
                Builders<ProjectDocument>.Filter.Eq(p => p.EventId, eventId),
                Builders<ProjectDocument>.Filter.Eq(p => p.Id, full)
            )).AnyAsync();

            if (!existing) return full;
        }
    }
}
