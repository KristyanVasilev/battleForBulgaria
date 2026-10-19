using System.Text.RegularExpressions;

namespace BulgariaConquest.Core.Game;

// Lifelines ("жокери") and chat.
public sealed partial class GameEngine
{
    public static string LifelineName(LifelineKind kind) => kind switch
    {
        LifelineKind.FiftyFifty => "50:50",
        LifelineKind.Range => "Летописец",
        LifelineKind.Category => "Тема",
        _ => "Обаждане на приятел",
    };

    /// <summary>Themes the "Тема" lifeline can pick for questions of the given kind.</summary>
    public IReadOnlyList<string> CategoriesFor(QuestionKind kind) =>
        kind == QuestionKind.Choice ? questions.ChoiceCategories : questions.NumericCategories;

    /// <summary>Which question kind a lifeline works on (null = both).</summary>
    public static QuestionKind? LifelineQuestionKind(LifelineKind kind) => kind switch
    {
        LifelineKind.FiftyFifty => QuestionKind.Choice,
        LifelineKind.Range => QuestionKind.Numeric,
        _ => null,
    };

    /// <summary>
    /// Spends a lifeline on the current question. The result is private: it is returned to the caller
    /// and kept on the question (so the player can see it again after a page reload), never broadcast.
    /// The "Тема" lifeline is different: it is used before an attack, with <paramref name="category"/>.
    /// </summary>
    public LifelineResult UseLifeline(GameState game, string playerId, LifelineKind kind, string? category = null)
    {
        if (kind == LifelineKind.Category) return ChooseTheme(game, playerId, category);

        var q = game.Question;
        Require(q is not null && !q.IsResolved, "Жокерите се ползват само по време на въпрос.");
        Require(q!.Participants.Contains(playerId), "Този въпрос не е за теб.");
        Require(!q.Answers.ContainsKey(playerId), "Вече отговори.");
        var player = game.GetPlayer(playerId);
        Require(!player.UsedLifelines.Contains(kind), "Този жокер вече е използван.");
        Require(LifelineQuestionKind(kind) is not { } only || only == q.Kind,
            kind == LifelineKind.FiftyFifty ? "50:50 е само за въпроси с четири отговора." : "Летописецът е само за числови въпроси.");

        var removed = LifelinesOf(q, playerId).SelectMany(r => r.RemovedOptions ?? []).ToHashSet();
        var result = kind switch
        {
            LifelineKind.FiftyFifty => FiftyFifty(q, removed),
            LifelineKind.Range => Chronicler(q),
            _ => PhoneFriend(q, removed),
        };

        player.UsedLifelines.Add(kind);
        if (!q.Lifelines.TryGetValue(playerId, out var list)) q.Lifelines[playerId] = list = [];
        list.Add(result);
        Log(game, $"{player.Name} използва жокер „{LifelineName(kind)}“.");
        return result;
    }

    /// <summary>
    /// "Тема": during conquest, on your turn and before you attack, pick the category of the battle question.
    /// The choice is public (logged) and applies to your next attack, including a numeric tiebreak.
    /// </summary>
    private LifelineResult ChooseTheme(GameState game, string playerId, string? category)
    {
        Require(game.Phase == GamePhase.Conquest && game.Battle is null && game.Question is null,
            "Жокерът „Тема“ се използва в битките – преди да нападнеш.");
        RequireTurn(game, playerId);
        var player = game.GetPlayer(playerId);
        Require(!player.UsedLifelines.Contains(LifelineKind.Category), "Този жокер вече е използван.");
        Require(category is not null && CategoriesFor(QuestionKind.Choice).Contains(category), "Избери тема от списъка.");

        player.UsedLifelines.Add(LifelineKind.Category);
        player.ThemeForNextAttack = category;
        Log(game, $"{player.Name} използва жокер „Тема“: следващата атака е с въпрос от „{category}“.");
        return new LifelineResult { Kind = LifelineKind.Category, Category = category };
    }

    /// <summary>Results of the lifelines this player used on the current question.</summary>
    public IReadOnlyList<LifelineResult> LifelinesOf(QuestionRound? q, string playerId) =>
        q is not null && q.Lifelines.TryGetValue(playerId, out var list) ? list : [];

    /// <summary>Removes two wrong options, leaving the right one and one wrong one.</summary>
    private LifelineResult FiftyFifty(QuestionRound q, HashSet<int> alreadyRemoved)
    {
        var wrong = Enumerable.Range(0, q.Options!.Count)
            .Where(i => i != q.CorrectIndex && !alreadyRemoved.Contains(i))
            .OrderBy(_ => _random.Next())
            .ToList();
        return new LifelineResult { Kind = LifelineKind.FiftyFifty, RemovedOptions = wrong.Take(Math.Min(2, wrong.Count - 1)).ToList() };
    }

    /// <summary>
    /// A range that always contains the answer, with the answer at a random spot inside it:
    /// 20 years wide for years, about ±8% of the answer for other numbers.
    /// </summary>
    private LifelineResult Chronicler(QuestionRound q)
    {
        var answer = q.CorrectNumber!.Value;
        var isYear = q.Unit?.Trim() is "г." or "година";
        var width = isYear ? 20 : Math.Max(2, Math.Abs(answer) * 0.16);
        var low = answer - _random.NextDouble() * width;
        var high = low + width;

        if (answer == Math.Round(answer))
        {
            low = Math.Floor(low);
            high = Math.Ceiling(high);
        }
        else
        {
            low = Math.Round(low, 2);
            high = Math.Round(high, 2);
        }
        if (answer >= 0) low = Math.Max(0, low);
        return new LifelineResult { Kind = LifelineKind.Range, RangeLow = low, RangeHigh = high };
    }

    /// <summary>A friend suggests an answer: right about 80% of the time, sure of themselves to a varying degree.</summary>
    private LifelineResult PhoneFriend(QuestionRound q, HashSet<int> removed)
    {
        var right = _random.NextDouble() < 0.8;
        var confidence = right ? 60 + _random.Next(36) : 40 + _random.Next(36);

        if (q.Kind == QuestionKind.Choice)
        {
            var correct = q.CorrectIndex!.Value;
            var others = Enumerable.Range(0, q.Options!.Count).Where(i => i != correct && !removed.Contains(i)).ToList();
            var pick = right || others.Count == 0 ? correct : others[_random.Next(others.Count)];
            return new LifelineResult { Kind = LifelineKind.Friend, FriendOption = pick, FriendConfidence = confidence };
        }

        var answer = q.CorrectNumber!.Value;
        var spread = right ? 0.02 : 0.1;
        var guess = answer * (1 + (_random.NextDouble() * 2 - 1) * spread);
        guess = answer == Math.Round(answer) ? Math.Round(guess) : Math.Round(guess, 2);
        return new LifelineResult { Kind = LifelineKind.Friend, FriendNumber = guess, FriendConfidence = confidence };
    }

    // ---------------------------------------------------------------- chat

    private static readonly TimeSpan ChatCooldown = TimeSpan.FromMilliseconds(700);
    private const int ChatHistory = 100;

    public void SendChat(GameState game, string playerId, string text)
    {
        game.GetPlayer(playerId);
        text = Regex.Replace(text ?? "", @"\s+", " ").Trim();
        Require(text.Length is >= 1 and <= 200, "Съобщението трябва да е между 1 и 200 символа.");

        var last = game.Chat.LastOrDefault(m => m.PlayerId == playerId);
        Require(last is null || Now - last.At >= ChatCooldown, "Пишеш твърде бързо.");

        game.Chat.Add(new ChatMessage(Now, playerId, text));
        if (game.Chat.Count > ChatHistory) game.Chat.RemoveRange(0, game.Chat.Count - ChatHistory);
    }
}
