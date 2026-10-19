using BulgariaConquest.Core.Bots;
using BulgariaConquest.Core.Game;
using BulgariaConquest.Core.Questions;
using BulgariaConquest.Server.Games;
using BulgariaConquest.Server.Hubs;
using BulgariaConquest.Server.Storage;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);

// Static game content (map, questions, rules) – JSON files in the Data folder.
var data = StaticData.Load(Path.Combine(builder.Environment.ContentRootPath, "Data"));

// Saved games – one JSON file per game. Configurable so a host can point it at a persistent disk.
var gamesDirectory = Path.Combine(
    builder.Environment.ContentRootPath,
    builder.Configuration["Storage:GamesDirectory"] ?? "App_Data/games");

builder.Services.AddSingleton(data.Map);
builder.Services.AddSingleton(data.Settings);
builder.Services.AddSingleton<IQuestionProvider>(data.Questions);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<GameEngine>(sp => new GameEngine(
    data.Map, sp.GetRequiredService<IQuestionProvider>(), sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton(sp => new BotBrain(sp.GetRequiredService<GameEngine>()));
builder.Services.AddSingleton(sp => new BotDirector(
    sp.GetRequiredService<GameEngine>(), sp.GetRequiredService<BotBrain>(), sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<IGameRepository>(new JsonFileGameRepository(gamesDirectory));
builder.Services.AddSingleton<GameManager>();
builder.Services.AddHostedService<GameTicker>();

builder.Services
    .AddSignalR(o => o.AddFilter<GameRuleExceptionFilter>())
    .AddJsonProtocol(o => Json.Configure(o.PayloadSerializerOptions));
builder.Services.ConfigureHttpJsonOptions(o => Json.Configure(o.SerializerOptions));

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    // Always revalidate (cheap ETag check) so players get new JS/CSS right after a deploy.
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache",
});

app.MapHub<GameHub>("/hubs/game");

var api = app.MapGroup("/api");
api.MapGet("/health", () => Results.Ok(new { status = "ok" }));
api.MapGet("/provinces", (BulgariaConquest.Core.Map.GameMap map) => map.Provinces);
api.MapGet("/settings", (BulgariaConquest.Core.Rules.GameSettings settings) => settings);
api.MapGet("/categories", (IQuestionProvider questions) => new { choice = questions.ChoiceCategories, numeric = questions.NumericCategories });
api.MapGet("/games/{id}", async (string id, GameManager games) =>
{
    try { return Results.Ok(await games.GetViewAsync(id)); }
    catch (GameRuleException ex) { return Results.NotFound(new { error = ex.Message }); }
});

app.Run();
