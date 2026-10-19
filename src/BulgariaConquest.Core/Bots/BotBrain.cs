using BulgariaConquest.Core.Game;
using BulgariaConquest.Core.Questions;

namespace BulgariaConquest.Core.Bots;

public abstract record BotAction(string PlayerId);
public sealed record ChooseCastleAction(string PlayerId, string ProvinceId) : BotAction(PlayerId);
public sealed record ClaimAction(string PlayerId, string ProvinceId) : BotAction(PlayerId);
public sealed record AttackAction(string PlayerId, string ProvinceId) : BotAction(PlayerId);
public sealed record NumericAnswerAction(string PlayerId, double Value) : BotAction(PlayerId);
public sealed record ChoiceAnswerAction(string PlayerId, int OptionIndex) : BotAction(PlayerId);

/// <summary>
/// Decides what a bot does right now. Bots "know" the correct answer but get it right only with
/// a probability that depends on their level, and their numeric guesses are noisy.
/// </summary>
public sealed class BotBrain(GameEngine engine, Random? random = null)
{
    private readonly Random _random = random ?? Random.Shared;

    private static (double ChoiceAccuracy, double NumericSpread) Skill(BotLevel level) => level switch
    {
        BotLevel.Easy => (0.45, 0.06),
        BotLevel.Hard => (0.85, 0.01),
        _ => (0.65, 0.025),
    };

    public BotAction? Decide(GameState game, PlayerState bot)
    {
        if (bot.BotLevel is not { } level || bot.IsEliminated) return null;

        if (game.Question is { IsResolved: false } q)
        {
            if (!q.Participants.Contains(bot.Id) || q.Answers.ContainsKey(bot.Id)) return null;
            return q.Kind == QuestionKind.Choice
                ? new ChoiceAnswerAction(bot.Id, PickOption(q, level))
                : new NumericAnswerAction(bot.Id, Guess(q.CorrectNumber!.Value, level, q.Difficulty));
        }

        if (game.Question is not null || engine.CurrentPlayerId(game) != bot.Id) return null;

        return game.Phase switch
        {
            GamePhase.CastleSelection => new ChooseCastleAction(bot.Id, BestCastle(game)),
            GamePhase.Distribution => new ClaimAction(bot.Id, BestClaim(game, bot.Id)),
            GamePhase.Conquest when game.Battle is null && BestAttack(game, bot) is { } target => new AttackAction(bot.Id, target),
            _ => null,
        };
    }

    /// <summary>Easy questions are answered right more often, hard ones less often and with wilder guesses.</summary>
    private static (double Accuracy, double Spread) ForDifficulty(QuestionDifficulty difficulty) => difficulty switch
    {
        QuestionDifficulty.Easy => (0.12, 0.7),
        QuestionDifficulty.Hard => (-0.15, 1.6),
        _ => (0, 1),
    };

    private int PickOption(QuestionRound q, BotLevel level)
    {
        var correct = q.CorrectIndex!.Value;
        var accuracy = Math.Clamp(Skill(level).ChoiceAccuracy + ForDifficulty(q.Difficulty).Accuracy, 0.25, 0.97);
        if (_random.NextDouble() < accuracy) return correct;
        var wrong = Enumerable.Range(0, q.Options!.Count).Where(i => i != correct).ToList();
        return wrong[_random.Next(wrong.Count)];
    }

    private double Guess(double correct, BotLevel level, QuestionDifficulty difficulty)
    {
        // Gaussian noise proportional to the size of the answer (Box–Muller).
        var gaussian = Math.Sqrt(-2 * Math.Log(1 - _random.NextDouble())) * Math.Cos(2 * Math.PI * _random.NextDouble());
        var guess = correct + gaussian * Math.Max(1, Math.Abs(correct)) * Skill(level).NumericSpread * ForDifficulty(difficulty).Spread;
        if (correct >= 0) guess = Math.Max(0, guess);
        return correct == Math.Round(correct) ? Math.Round(guess) : Math.Round(guess, 2);
    }

    /// <summary>A castle with lots of free land around it, so the bot can grow.</summary>
    private string BestCastle(GameState game) =>
        PickBest(engine.CastleOptions(game), id =>
            engine.Map.NeighborsOf(id).Count(n => game.Owners[n] is null) + _random.NextDouble() * 2);

    /// <summary>Keeps territory compact: prefers land touching many of its own provinces.</summary>
    private string BestClaim(GameState game, string botId) =>
        PickBest(engine.ClaimOptions(game, botId), id =>
            engine.Map.NeighborsOf(id).Sum(n => game.Owners[n] == botId ? 2 : game.Owners[n] is null ? 0.5 : 0)
            + _random.NextDouble());

    private string? BestAttack(GameState game, PlayerState bot)
    {
        var targets = engine.AttackTargets(game, bot.Id);
        if (targets.Count == 0) return null;
        if (bot.BotLevel == BotLevel.Easy) return targets[_random.Next(targets.Count)];

        var leaderScore = game.ActivePlayers.Where(p => p.Id != bot.Id).Max(p => engine.Score(game, p.Id));
        return PickBest(targets, id =>
        {
            var owner = game.GetPlayer(game.Owners[id]!);
            var isCastle = owner.CastleProvinceId == id;
            var score = _random.NextDouble() * 2;
            if (isCastle && owner.Towers == 1) score += 10;                     // finish them off
            else if (isCastle) score += bot.BotLevel == BotLevel.Hard ? 0.5 : -1;
            if (engine.Score(game, owner.Id) == leaderScore) score += 1.5;      // slow down the leader
            if (!isCastle) score += (engine.ProvincePoints(game, id) - game.Settings.TerritoryPoints) / 400.0; // rich ruins
            return score;
        });
    }

    private static string PickBest(IReadOnlyList<string> options, Func<string, double> score) =>
        options.MaxBy(score) ?? throw new GameRuleException("Няма възможен ход.");
}
