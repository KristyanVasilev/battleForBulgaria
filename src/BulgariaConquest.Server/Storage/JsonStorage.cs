using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using BulgariaConquest.Core.Game;
using BulgariaConquest.Core.Map;
using BulgariaConquest.Core.Questions;
using BulgariaConquest.Core.Rules;

namespace BulgariaConquest.Server.Storage;

public static class Json
{
    /// <summary>Shared options: camelCase, enums as strings, readable Cyrillic in saved files.</summary>
    public static readonly JsonSerializerOptions Options = Configure(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public static JsonSerializerOptions Configure(JsonSerializerOptions options)
    {
        options.WriteIndented = true;
        options.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    public static T Read<T>(string path) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options)
        ?? throw new InvalidDataException($"Empty JSON file: {path}");
}

/// <summary>Read-only game content from the Data folder (map, questions, rules).</summary>
public sealed record StaticData(GameMap Map, QuestionBank Questions, GameSettings Settings)
{
    private sealed record ProvinceFile(List<Province> Provinces);

    public static StaticData Load(string dataDirectory)
    {
        var map = new GameMap(Json.Read<ProvinceFile>(Path.Combine(dataDirectory, "provinces.json")).Provinces);
        var numeric = Json.Read<List<NumericQuestion>>(Path.Combine(dataDirectory, "questions", "numeric.json"));
        var choice = Json.Read<List<ChoiceQuestion>>(Path.Combine(dataDirectory, "questions", "choice.json"));
        var settings = Json.Read<GameSettings>(Path.Combine(dataDirectory, "game-settings.json"));
        return new StaticData(map, new QuestionBank(numeric, choice), settings);
    }
}

/// <summary>
/// Where game state is persisted. Today: one JSON file per game.
/// Swap the implementation (e.g. for a database or blob storage) without touching game logic.
/// </summary>
public interface IGameRepository
{
    Task SaveAsync(GameState game, CancellationToken ct = default);
    Task<GameState?> LoadAsync(string id, CancellationToken ct = default);
    bool Exists(string id);
}

public sealed class JsonFileGameRepository : IGameRepository
{
    private readonly string _directory;

    public JsonFileGameRepository(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
    }

    public async Task SaveAsync(GameState game, CancellationToken ct = default)
    {
        var path = PathFor(game.Id);
        var temp = path + ".tmp";
        await using (var stream = File.Create(temp))
            await JsonSerializer.SerializeAsync(stream, game, Json.Options, ct);
        File.Move(temp, path, overwrite: true); // atomic replace, so a crash never leaves half a file
    }

    public async Task<GameState?> LoadAsync(string id, CancellationToken ct = default)
    {
        var path = PathFor(id);
        if (!File.Exists(path)) return null;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<GameState>(stream, Json.Options, ct);
    }

    public bool Exists(string id) => File.Exists(PathFor(id));

    private string PathFor(string id)
    {
        if (id.Length == 0 || !id.All(char.IsLetterOrDigit))
            throw new GameRuleException("Невалиден код на игра.");
        return Path.Combine(_directory, $"{id}.json");
    }
}
