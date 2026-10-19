using BulgariaConquest.Core.Game;
using BulgariaConquest.Core.Map;
using BulgariaConquest.Core.Questions;
using BulgariaConquest.Core.Rules;

namespace BulgariaConquest.Tests;

public class LifelineAndChatTests
{
    private static readonly GameMap LineMap = new(
    [
        new Province("a", "X-1", "A", ["b"]),
        new Province("b", "X-2", "B", ["a", "c"]),
        new Province("c", "X-3", "C", ["b", "d"]),
        new Province("d", "X-4", "D", ["c", "e"]),
        new Province("e", "X-5", "E", ["d", "f"]),
        new Province("f", "X-6", "F", ["e"]),
    ]);

    private readonly ManualTime _time = new();
    private readonly GameEngine _engine;
    private readonly GameState _game;

    public LifelineAndChatTests()
    {
        _engine = new GameEngine(LineMap, new YearQuestions(), _time, new Random(7));
        _game = _engine.CreateGame("JOKER", new GameSettings());
        foreach (var name in new[] { "Ана", "Боби", "Вики" }) _engine.AddPlayer(_game, name);
        _engine.Start(_game, "p1");
        _engine.ChooseCastle(_game, "p1", "a");
        _engine.ChooseCastle(_game, "p2", "c");
        _engine.ChooseCastle(_game, "p3", "e");
        // Now a numeric distribution question (answer 1878 г.) is open.
    }

    [Fact]
    public void Chronicler_gives_a_twenty_year_range_that_contains_the_answer()
    {
        for (var seed = 0; seed < 50; seed++)
        {
            var engine = new GameEngine(LineMap, new YearQuestions(), _time, new Random(seed));
            var game = engine.CreateGame("R", new GameSettings());
            foreach (var name in new[] { "А", "Б", "В" }) engine.AddPlayer(game, name);
            engine.Start(game, "p1");
            engine.ChooseCastle(game, "p1", "a");
            engine.ChooseCastle(game, "p2", "c");
            engine.ChooseCastle(game, "p3", "e");

            var r = engine.UseLifeline(game, "p1", LifelineKind.Range);
            Assert.InRange(1878, r.RangeLow!.Value, r.RangeHigh!.Value);
            Assert.InRange(r.RangeHigh!.Value - r.RangeLow!.Value, 20, 21);
        }
    }

    [Fact]
    public void Each_lifeline_works_once_per_game_and_only_on_its_question_kind()
    {
        var ex = Assert.Throws<GameRuleException>(() => _engine.UseLifeline(_game, "p1", LifelineKind.FiftyFifty));
        Assert.Contains("четири отговора", ex.Message);

        _engine.UseLifeline(_game, "p1", LifelineKind.Range);
        ex = Assert.Throws<GameRuleException>(() => _engine.UseLifeline(_game, "p1", LifelineKind.Range));
        Assert.Contains("вече е използван", ex.Message);
        Assert.Contains(LifelineKind.Range, _game.GetPlayer("p1").UsedLifelines);
    }

    [Fact]
    public void Fifty_fifty_removes_two_wrong_options_and_keeps_the_right_one()
    {
        var q = StartBattle();
        var r = _engine.UseLifeline(_game, "p1", LifelineKind.FiftyFifty);

        Assert.Equal(2, r.RemovedOptions!.Count);
        Assert.DoesNotContain(q.CorrectIndex!.Value, r.RemovedOptions);
        Assert.Single(_engine.LifelinesOf(q, "p1"));
        Assert.Empty(_engine.LifelinesOf(q, "p2")); // private to the player who used it
    }

    [Fact]
    public void Friend_never_suggests_an_option_already_removed_by_fifty_fifty()
    {
        var q = StartBattle();
        var removed = _engine.UseLifeline(_game, "p1", LifelineKind.FiftyFifty).RemovedOptions!;
        var friend = _engine.UseLifeline(_game, "p1", LifelineKind.Friend);

        Assert.NotNull(friend.FriendOption);
        Assert.DoesNotContain(friend.FriendOption!.Value, removed);
        Assert.InRange(friend.FriendConfidence, 40, 95);
    }

    [Fact]
    public void Lifelines_cannot_be_used_after_answering()
    {
        _engine.AnswerNumeric(_game, "p1", 1900);
        Assert.Throws<GameRuleException>(() => _engine.UseLifeline(_game, "p1", LifelineKind.Range));
    }

    [Fact]
    public void Chat_trims_text_limits_length_and_rate()
    {
        _engine.SendChat(_game, "p1", "  Здравейте,\n   владетели!  ");
        Assert.Equal("Здравейте, владетели!", _game.Chat.Single().Text);

        Assert.Throws<GameRuleException>(() => _engine.SendChat(_game, "p1", "пак аз"));   // too fast
        Assert.Throws<GameRuleException>(() => _engine.SendChat(_game, "p2", "   "));      // empty
        Assert.Throws<GameRuleException>(() => _engine.SendChat(_game, "p2", new string('а', 201)));

        _time.Advance(TimeSpan.FromSeconds(1));
        _engine.SendChat(_game, "p1", "пак аз");
        Assert.Equal(2, _game.Chat.Count);
    }

    [Fact]
    public void Theme_lifeline_is_picked_before_an_attack_and_sets_the_battle_question_category()
    {
        var engine = new GameEngine(LineMap, new ThemedQuestions(), _time, new Random(3));
        var game = engine.CreateGame("THEME", new GameSettings());
        foreach (var name in new[] { "А", "Б", "В" }) engine.AddPlayer(game, name);
        engine.Start(game, "p1");
        engine.ChooseCastle(game, "p1", "a");
        engine.ChooseCastle(game, "p2", "c");
        engine.ChooseCastle(game, "p3", "e");

        // Not during the distribution question – only in the battles.
        var ex = Assert.Throws<GameRuleException>(() => engine.UseLifeline(game, "p1", LifelineKind.Category, "Спорт"));
        Assert.Contains("преди да нападнеш", ex.Message);

        engine.AnswerNumeric(game, "p1", 1878);
        engine.AnswerNumeric(game, "p2", 1700);
        engine.AnswerNumeric(game, "p3", 1500);
        _time.Advance(TimeSpan.FromSeconds(game.Settings.RevealSeconds + 1));
        engine.Tick(game);
        engine.ClaimProvince(game, "p1", "b");
        engine.ClaimProvince(game, "p1", "f");
        engine.ClaimProvince(game, "p2", "d");
        Assert.Equal(GamePhase.Conquest, game.Phase);

        Assert.Throws<GameRuleException>(() => engine.UseLifeline(game, "p2", LifelineKind.Category, "Спорт"));      // not p2's turn
        Assert.Throws<GameRuleException>(() => engine.UseLifeline(game, "p1", LifelineKind.Category, "Кулинария")); // no such theme
        Assert.DoesNotContain(LifelineKind.Category, game.GetPlayer("p1").UsedLifelines);

        var r = engine.UseLifeline(game, "p1", LifelineKind.Category, "Спорт");
        Assert.Equal("Спорт", r.Category);
        Assert.Equal("Спорт", game.GetPlayer("p1").ThemeForNextAttack);

        engine.Attack(game, "p1", "c");
        Assert.Equal("Спорт", game.Battle!.Category);
        Assert.Equal("Спорт", game.Question!.Category);
        Assert.Equal(QuestionKind.Choice, game.Question.Kind);
        Assert.Null(game.GetPlayer("p1").ThemeForNextAttack); // spent on this attack
        Assert.Throws<GameRuleException>(() => engine.UseLifeline(game, "p1", LifelineKind.Category, "История"));
    }

    [Fact]
    public void Question_bank_deals_by_category_without_repeats()
    {
        var numeric = Enumerable.Range(1, 6).Select(i => new NumericQuestion($"n{i}", $"В{i}?", i, null, i <= 3 ? "История" : "Спорт")).ToList();
        var choice = new List<ChoiceQuestion> { new("c1", "К?", ["а", "б", "в", "г"], 2, "Наука", QuestionDifficulty.Hard) };
        var bank = new QuestionBank(numeric, choice, new Random(1));

        Assert.Equal(["История", "Спорт"], bank.NumericCategories);
        Assert.Equal(["Наука"], bank.ChoiceCategories);
        var drawn = Enumerable.Range(0, 3).Select(_ => bank.NextNumeric("История")).ToList();
        Assert.All(drawn, q => Assert.Equal("История", q.Category));
        Assert.Equal(3, drawn.Select(q => q.Id).Distinct().Count());

        var c = bank.NextChoice("Наука");
        Assert.Equal("в", c.Options[c.CorrectIndex]);
        Assert.Equal(QuestionDifficulty.Hard, c.Difficulty);
    }

    /// <summary>Finishes distribution and starts a battle p1 → p2 on province b.</summary>
    private QuestionRound StartBattle()
    {
        _engine.AnswerNumeric(_game, "p1", 1878);
        _engine.AnswerNumeric(_game, "p2", 1700);
        _engine.AnswerNumeric(_game, "p3", 1500);
        _time.Advance(TimeSpan.FromSeconds(_game.Settings.RevealSeconds + 1));
        _engine.Tick(_game);
        _engine.ClaimProvince(_game, "p1", "b");
        _engine.ClaimProvince(_game, "p1", "f");
        _engine.ClaimProvince(_game, "p2", "d");

        _engine.Attack(_game, "p1", "c");
        return _game.Question!;
    }

    private sealed class ThemedQuestions : IQuestionProvider
    {
        public NumericQuestion NextNumeric() => new("y", "Освобождението?", 1878, "г.", "История");
        public ChoiceQuestion NextChoice() => new("c", "Кое?", ["вярно", "грешно", "грешно", "грешно"], 0, "История");
        public IReadOnlyList<string> NumericCategories => ["История", "Спорт"];
        public IReadOnlyList<string> ChoiceCategories => ["История", "Спорт"];
        public NumericQuestion NextNumeric(string category) => new($"n-{category}", "Колко играчи?", 11, null, category);
        public ChoiceQuestion NextChoice(string category) => new($"c-{category}", "Кое?", ["вярно", "грешно", "грешно", "грешно"], 0, category);
    }

    private sealed class YearQuestions : IQuestionProvider
    {
        public NumericQuestion NextNumeric() => new("y", "Освобождението?", 1878, "г.");
        public ChoiceQuestion NextChoice() => new("c", "Кое?", ["вярно", "грешно", "грешно", "грешно"], 0);
    }
}
