using System.Text.Json;
using BulgariaConquest.Core.Bots;
using BulgariaConquest.Core.Game;
using BulgariaConquest.Core.Map;
using BulgariaConquest.Core.Rules;

namespace BulgariaConquest.Tests;

public class BotTests
{
    [Theory]
    [InlineData(BotLevel.Easy)]
    [InlineData(BotLevel.Normal)]
    [InlineData(BotLevel.Hard)]
    public void Three_bots_finish_a_whole_game_on_the_real_map_without_timing_out(BotLevel level)
    {
        var time = new ManualTime();
        var random = new Random(42);
        var engine = new GameEngine(LoadRealMap(), new FixedQuestions(), time, random);
        var bots = new BotDirector(engine, new BotBrain(engine, random), time, random);

        var game = engine.CreateGame("BOTS", new GameSettings());
        foreach (var name in BotDirector.Names.Take(3)) engine.AddPlayer(game, name, level);
        engine.Start(game, "p1");

        var timeouts = 0;
        for (var tick = 0; tick < 50_000 && game.Phase != GamePhase.Finished; tick++)
        {
            time.Advance(TimeSpan.FromMilliseconds(250));
            var logBefore = game.Log.Count;
            engine.Tick(game);
            bots.Act(game);
            timeouts += game.Log.Skip(Math.Min(logBefore, game.Log.Count)).Count(l => l.Text.Contains("изтече"));
        }

        Assert.Equal(GamePhase.Finished, game.Phase);
        Assert.NotNull(game.WinnerId);
        Assert.All(game.Owners.Values, Assert.NotNull);
        Assert.Equal(0, timeouts);
    }

    [Fact]
    public void Bot_waits_before_answering_so_a_fast_human_can_beat_it()
    {
        var time = new ManualTime();
        var engine = new GameEngine(LoadRealMap(), new FixedQuestions(), time);
        var bots = new BotDirector(engine, new BotBrain(engine), time);
        var game = engine.CreateGame("MIX", new GameSettings());
        engine.AddPlayer(game, "Човек");
        engine.AddPlayer(game, "Бот 1", BotLevel.Hard);
        engine.AddPlayer(game, "Бот 2", BotLevel.Hard);
        engine.Start(game, "p1");

        // Castle selection: human first, then the bots pick on their own.
        engine.ChooseCastle(game, "p1", "vidin");
        for (var i = 0; i < 40 && game.Phase == GamePhase.CastleSelection; i++)
        {
            time.Advance(TimeSpan.FromMilliseconds(250));
            bots.Act(game);
        }
        Assert.Equal(GamePhase.Distribution, game.Phase);

        // The question just started: bots plan but do not answer instantly.
        bots.Act(game);
        Assert.Empty(game.Question!.Answers);
        engine.AnswerNumeric(game, "p1", 100);
        Assert.Single(game.Question.Answers);
    }

    private static GameMap LoadRealMap()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BulgariaConquest.sln"))) dir = dir.Parent;
        var path = Path.Combine(dir!.FullName, "src", "BulgariaConquest.Server", "Data", "provinces.json");
        var file = JsonSerializer.Deserialize<ProvinceFile>(File.ReadAllText(path), JsonSerializerOptions.Web)!;
        return new GameMap(file.Provinces);
    }

    private sealed record ProvinceFile(List<Province> Provinces);
}
