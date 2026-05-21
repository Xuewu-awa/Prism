using Microsoft.AspNetCore.Http.Features;
using UEBPR.Models;
using UEBPR.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 512L * 1024L * 1024L;
});

builder.Services.AddSingleton<NodeLibraryService>();
builder.Services.AddSingleton<AssetSessionService>();
builder.Services.AddSingleton<BlueprintGraphService>();
builder.Services.AddSingleton<AssetWritebackService>();
builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapPost("/api/assets/open", async (HttpRequest request, AssetSessionService sessions, BlueprintGraphService graphs, CancellationToken cancellationToken) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest(new ErrorDto("Expected multipart/form-data."));
    }

    var form = await request.ReadFormAsync(cancellationToken);
    var uasset = form.Files.GetFile("uasset");
    if (uasset is null)
    {
        return Results.BadRequest(new ErrorDto("Missing uasset file."));
    }

    var session = await sessions.OpenAsync(
        uasset,
        form.Files.GetFile("uexp"),
        form.Files.GetFile("usmap"),
        cancellationToken);

    return Results.Ok(graphs.DescribeSession(session));
});

app.MapPost("/api/assets/{sessionId:guid}/usmap", async (Guid sessionId, HttpRequest request, AssetSessionService sessions, CancellationToken cancellationToken) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest(new ErrorDto("Expected multipart/form-data."));
    }

    var form = await request.ReadFormAsync(cancellationToken);
    var usmap = form.Files.GetFile("usmap");
    if (usmap is null)
    {
        return Results.BadRequest(new ErrorDto("Missing usmap file."));
    }

    return sessions.TryGet(sessionId, out var session)
        ? Results.Ok(await sessions.AttachUsmapAsync(session, usmap, cancellationToken))
        : Results.NotFound(new ErrorDto("Asset session was not found."));
});

app.MapGet("/api/assets/{sessionId:guid}/graphs/{exportIndex:int}", (Guid sessionId, int exportIndex, AssetSessionService sessions, BlueprintGraphService graphs) =>
{
    return sessions.TryGet(sessionId, out var session)
        ? Results.Ok(graphs.BuildGraph(session, exportIndex))
        : Results.NotFound(new ErrorDto("Asset session was not found."));
});

app.MapPut("/api/assets/{sessionId:guid}/graphs/{exportIndex:int}", (Guid sessionId, int exportIndex, GraphDocumentDto graph, AssetSessionService sessions, AssetWritebackService writeback) =>
{
    return sessions.TryGet(sessionId, out var session)
        ? Results.Ok(writeback.ApplyGraph(session, exportIndex, graph))
        : Results.NotFound(new ErrorDto("Asset session was not found."));
});

app.MapPost("/api/assets/{sessionId:guid}/save", (Guid sessionId, AssetSessionService sessions, AssetWritebackService writeback) =>
{
    return sessions.TryGet(sessionId, out var session)
        ? Results.Ok(writeback.Save(session))
        : Results.NotFound(new ErrorDto("Asset session was not found."));
});

app.MapGet("/api/node-library", (NodeLibraryService library) => Results.Ok(library.GetLibrary()));
app.MapPost("/api/node-library/import", (NodeLibraryDto imported, NodeLibraryService library) => Results.Ok(library.Import(imported)));
app.MapGet("/api/node-library/export", (NodeLibraryService library) => Results.File(library.ExportBytes(), "application/json", "node-library.json"));

app.Run();
