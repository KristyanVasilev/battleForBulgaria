using BulgariaConquest.Core.Game;
using BulgariaConquest.Core.Questions;

namespace BulgariaConquest.Server.Games;

// What clients see. Built from GameState but without secrets:
// no player tokens, and no correct answers / other players' answers until the question is revealed.

public sealed record GameView(
    string Id,
    int Version,
    GamePhase Phase,
    int Round,
    int MaxRounds,
    DateTimeOffset ServerNow,
    DateTimeOffset? ActionDeadline,
    string? CurrentPlayerId,
    IReadOnlyList<PlayerView> Players,
    IReadOnlyDictionary<string, string?> Owners,
    IReadOnlyDictionary<string, int> ProvincePoints,
    IReadOnlyList<PendingPick> Picks,
    Battle? Battle,
    QuestionView? Question,
    ActionOptions Options,
    string? WinnerId,
    IReadOnlyList<LogEntry> Log,
    IReadOnlyList<ChatMessage> Chat)
{
    public static GameView From(GameState game, GameEngine engine, DateTimeOffset now)
    {
        var current = engine.CurrentPlayerId(game);
        return new GameView(
            game.Id,
            game.Version,
            game.Phase,
            game.Round,
            game.Settings.ConquestRounds,
            now,
            game.ActionDeadline,
            current,
            game.Players.Select(p => new PlayerView(
                p.Id, p.Name, p.Color, p.CastleProvinceId, p.Towers, game.Settings.TowersPerCastle,
                p.IsEliminated, engine.Score(game, p.Id), game.ProvincesOf(p.Id).Count(), p.BotLevel,
                Enum.GetValues<LifelineKind>().Except(p.UsedLifelines).ToList(), p.ThemeForNextAttack)).ToList(),
            game.Owners,
            engine.AllProvincePoints(game),
            game.Picks,
            game.Battle,
            game.Question is { } q ? QuestionView.From(q) : null,
            ActionOptions.For(game, engine, current),
            game.WinnerId,
            game.Log.TakeLast(40).ToList(),
            game.Chat.TakeLast(60).ToList());
    }
}

public sealed record PlayerView(
    string Id,
    string Name,
    string Color,
    string? CastleProvinceId,
    int Towers,
    int MaxTowers,
    bool IsEliminated,
    int Score,
    int ProvinceCount,
    BotLevel? BotLevel,
    IReadOnlyList<LifelineKind> LifelinesLeft,
    string? ThemeForNextAttack);

public sealed record AnswerView(double? Number, int? Choice, int ResponseMs);

public sealed record QuestionView(
    QuestionKind Kind,
    QuestionPurpose Purpose,
    string Text,
    string? Category,
    QuestionDifficulty Difficulty,
    IReadOnlyList<string>? Options,
    string? Unit,
    IReadOnlyList<string> Participants,
    IReadOnlyList<string> Answered,
    DateTimeOffset StartedAt,
    DateTimeOffset Deadline,
    bool Revealed,
    int? CorrectIndex,
    double? CorrectNumber,
    IReadOnlyDictionary<string, AnswerView>? Answers,
    IReadOnlyList<string> Ranking,
    IReadOnlyList<string> CorrectPlayers,
    IReadOnlyDictionary<string, List<LifelineKind>> LifelinesUsed)
{
    public static QuestionView From(QuestionRound q)
    {
        var revealed = q.IsResolved;
        return new QuestionView(
            q.Kind, q.Purpose, q.Text, q.Category, q.Difficulty, q.Options, q.Unit, q.Participants,
            q.Answers.Keys.ToList(), q.StartedAt, q.Deadline, revealed,
            revealed ? q.CorrectIndex : null,
            revealed ? q.CorrectNumber : null,
            revealed ? q.Answers.ToDictionary(a => a.Key, a => new AnswerView(a.Value.Number, a.Value.Choice, a.Value.ResponseMs)) : null,
            revealed ? q.Ranking : [],
            revealed ? q.CorrectPlayers : [],
            // Everyone sees WHO used which lifeline, never what it revealed.
            q.Lifelines.ToDictionary(l => l.Key, l => l.Value.Select(r => r.Kind).ToList()));
    }
}

/// <summary>What the player whose turn it is may click on the map right now.</summary>
public sealed record ActionOptions(string? PlayerId, string? Kind, IReadOnlyList<string> Provinces)
{
    public static ActionOptions For(GameState game, GameEngine engine, string? playerId)
    {
        if (playerId is null) return new ActionOptions(null, null, []);
        return game.Phase switch
        {
            GamePhase.CastleSelection => new ActionOptions(playerId, "castle", engine.CastleOptions(game)),
            GamePhase.Distribution => new ActionOptions(playerId, "claim", engine.ClaimOptions(game, playerId)),
            GamePhase.Conquest when game.Battle is null => new ActionOptions(playerId, "attack", engine.AttackTargets(game, playerId)),
            _ => new ActionOptions(null, null, []),
        };
    }
}

public sealed record JoinResult(string GameId, string PlayerId, string Token);
