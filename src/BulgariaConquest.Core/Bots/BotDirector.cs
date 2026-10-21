using BulgariaConquest.Core.Game;

namespace BulgariaConquest.Core.Bots;

/// <summary>
/// Plays for the bots. Called on every server tick: when a bot has something to do, it first
/// "thinks" for a random, level-dependent time, then acts through the normal <see cref="GameEngine"/> rules.
/// A fast human can therefore beat a bot on response time.
/// </summary>
public sealed class BotDirector(GameEngine engine, BotBrain brain, TimeProvider? time = null, Random? random = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Random _random = random ?? Random.Shared;
    private readonly Dictionary<(string Game, string Player, string Context), DateTimeOffset> _plans = [];
    private readonly Lock _lock = new();

    public static readonly string[] Names =
    [
        "Хан Аспарух", "Хан Тервел", "Хан Крум", "Княз Борис", "Цар Симеон",
        "Цар Самуил", "Цар Калоян", "Иван Асен II", "Цар Петър", "Хан Омуртаг",
    ];

    /// <summary>Runs every bot action that is due. Returns true if the game changed.</summary>
    public bool Act(GameState game)
    {
        var changed = false;
        var now = _time.GetUtcNow();

        foreach (var bot in game.Players.Where(p => p.IsBot && !p.IsEliminated).ToList())
        {
            if (game.Phase is GamePhase.Lobby or GamePhase.Finished) break;

            var context = Context(game, bot, now);
            if (context is null) continue;

            var key = (game.Id, bot.Id, context);
            lock (_lock)
            {
                if (!_plans.TryGetValue(key, out var due))
                {
                    _plans[key] = now + ThinkTime(game, bot, now);
                    continue;
                }
                if (now < due) continue;
                _plans.Remove(key);
            }

            if (brain.Decide(game, bot) is not { } action) continue;
            try
            {
                Apply(game, action);
                changed = true;
            }
            catch (GameRuleException)
            {
                // The situation changed under the bot; it will re-plan on the next tick.
            }
        }

        Prune(now);
        return changed;
    }

    /// <summary>Identifies "the thing the bot must react to", so each decision is planned once.</summary>
    private string? Context(GameState game, PlayerState bot, DateTimeOffset now)
    {
        if (game.Question is { } q)
        {
            // Bots start thinking only once the question is shown to the humans (after the intro).
            return !q.IsResolved && now >= q.StartedAt && q.Participants.Contains(bot.Id) && !q.Answers.ContainsKey(bot.Id)
                ? $"q{q.StartedAt.UtcTicks}"
                : null;
        }
        return engine.CurrentPlayerId(game) == bot.Id && game.ActionDeadline is { } deadline
            ? $"t{deadline.UtcTicks}"
            : null;
    }

    private TimeSpan ThinkTime(GameState game, PlayerState bot, DateTimeOffset now)
    {
        double min, max;
        DateTimeOffset deadline;
        if (game.Question is { } q)
        {
            (min, max) = (q.Kind, bot.BotLevel) switch
            {
                (QuestionKind.Choice, BotLevel.Easy) => (5.0, 11.0),
                (QuestionKind.Choice, BotLevel.Hard) => (2.0, 6.0),
                (QuestionKind.Choice, _) => (3.5, 9.0),
                (_, BotLevel.Easy) => (6.0, 14.0),
                (_, BotLevel.Hard) => (3.0, 8.0),
                _ => (4.0, 11.0),
            };
            deadline = q.Deadline;
        }
        else
        {
            (min, max) = (1.2, 3.0);
            deadline = game.ActionDeadline ?? now.AddSeconds(30);
        }

        var seconds = min + _random.NextDouble() * (max - min);
        var latest = (deadline - now).TotalSeconds - 0.5; // always act before the time runs out
        return TimeSpan.FromSeconds(Math.Max(0, Math.Min(seconds, latest)));
    }

    private void Apply(GameState game, BotAction action)
    {
        switch (action)
        {
            case ChooseCastleAction a: engine.ChooseCastle(game, a.PlayerId, a.ProvinceId); break;
            case ClaimAction a: engine.ClaimProvince(game, a.PlayerId, a.ProvinceId); break;
            case AttackAction a: engine.Attack(game, a.PlayerId, a.ProvinceId); break;
            case NumericAnswerAction a: engine.AnswerNumeric(game, a.PlayerId, a.Value); break;
            case ChoiceAnswerAction a: engine.AnswerChoice(game, a.PlayerId, a.OptionIndex); break;
        }
    }

    private void Prune(DateTimeOffset now)
    {
        lock (_lock)
        {
            if (_plans.Count < 200) return;
            foreach (var key in _plans.Where(p => p.Value < now.AddMinutes(-5)).Select(p => p.Key).ToList())
                _plans.Remove(key);
        }
    }
}
