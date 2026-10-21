using BulgariaConquest.Core.Game;
using BulgariaConquest.Core.Map;
using BulgariaConquest.Core.Questions;
using BulgariaConquest.Core.Rules;

namespace BulgariaConquest.Tests;

public class GameEngineTests
{
    // A small board in a line: a - b - c - d - e - f
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

    public GameEngineTests()
    {
        _engine = new GameEngine(LineMap, new FixedQuestions(), _time, new Random(1));
        _game = _engine.CreateGame("TEST", new GameSettings { ConquestRounds = 10 });
        foreach (var name in new[] { "Ана", "Боби", "Вики" }) _engine.AddPlayer(_game, name);
        _engine.Start(_game, "p1");
    }

    [Fact]
    public void Castles_cannot_border_each_other()
    {
        _engine.ChooseCastle(_game, "p1", "a");
        var ex = Assert.Throws<GameRuleException>(() => _engine.ChooseCastle(_game, "p2", "b"));
        Assert.Contains("несъседна", ex.Message);
    }

    [Fact]
    public void Only_the_current_player_may_act()
    {
        Assert.Throws<GameRuleException>(() => _engine.ChooseCastle(_game, "p2", "c"));
    }

    [Fact]
    public void Distribution_winner_claims_first_then_conquest_starts_when_map_is_full()
    {
        PlaceCastles();
        Assert.Equal(GamePhase.Distribution, _game.Phase);
        Assert.Equal(QuestionKind.Numeric, _game.Question!.Kind);

        // Correct answer is 100: p2 is closest, p1 second, p3 last.
        _engine.AnswerNumeric(_game, "p1", 90);
        _engine.AnswerNumeric(_game, "p2", 99);
        _engine.AnswerNumeric(_game, "p3", 500);
        Assert.Equal(["p2", "p1", "p3"], _game.Question!.Ranking);

        FinishReveal();
        Assert.Equal("p2", _engine.CurrentPlayerId(_game));
        Assert.Equal(["b", "d"], _engine.ClaimOptions(_game, "p2").Order());

        _engine.ClaimProvince(_game, "p2", "b");
        _engine.ClaimProvince(_game, "p2", "d");
        _engine.ClaimProvince(_game, "p1", "f");

        Assert.Equal(GamePhase.Conquest, _game.Phase);
        Assert.Equal(1, _game.Round);
        Assert.Equal(_game.TurnOrder[0], _engine.CurrentPlayerId(_game));
    }

    [Fact]
    public void Attack_order_is_shuffled_every_round_and_nobody_attacks_twice_in_a_row()
    {
        _game.Settings = _game.Settings with { ConquestRounds = 30 };
        StartConquest();

        var attackers = new List<string>();
        while (_game.Phase == GamePhase.Conquest)
        {
            attackers.Add(_engine.CurrentPlayerId(_game)!);
            SkipTurn();
        }
        Assert.DoesNotContain(null, attackers);

        Assert.Equal(30, _game.RoundOrders.Count);
        foreach (var order in _game.RoundOrders)
            Assert.Equal(["p1", "p2", "p3"], order.Order()); // everyone attacks once per round
        for (var i = 1; i < _game.RoundOrders.Count; i++)
        {
            Assert.False(_game.RoundOrders[i].SequenceEqual(_game.RoundOrders[i - 1]));
            Assert.NotEqual(_game.RoundOrders[i - 1][^1], _game.RoundOrders[i][0]);
        }
        Assert.True(_game.RoundOrders.Select(o => o[0]).Distinct().Count() > 1); // not always the same player first
        Assert.DoesNotContain(attackers.Zip(attackers.Skip(1)), pair => pair.First == pair.Second);
    }

    [Fact]
    public void A_province_taken_in_battle_is_worth_400_and_never_counts_twice()
    {
        StartConquest();
        var p1Before = _engine.Score(_game, "p1");
        var p2Before = _engine.Score(_game, "p2");

        WinBattle("p1", "p2", "b");
        Assert.Equal(400, _engine.ProvincePoints(_game, "b"));
        Assert.Equal(p1Before + 400, _engine.Score(_game, "p1"));
        Assert.Equal(p2Before - 200, _engine.Score(_game, "p2"));

        // p2 takes it back: p2 now holds it for 400, p1 loses those 400 – nothing is added twice.
        WinBattle("p2", "p1", "b");
        Assert.Equal(p1Before, _engine.Score(_game, "p1"));
        Assert.Equal(p2Before + 200, _engine.Score(_game, "p2"));

        WinBattle("p1", "p2", "b");
        Assert.Equal(400, _engine.ProvincePoints(_game, "b"));
        Assert.Equal(p1Before + 400, _engine.Score(_game, "p1"));
        Assert.Single(_game.ConqueredProvinces);

        // A failed attack keeps the province's value; the defender only earns the defence bonus.
        while (_engine.CurrentPlayerId(_game) != "p2") SkipTurn();
        _engine.Attack(_game, "p2", "b");
        _engine.AnswerChoice(_game, "p2", FixedQuestions.Wrong);
        _engine.AnswerChoice(_game, "p1", FixedQuestions.Correct);
        FinishReveal();
        Assert.Equal("p1", _game.Owners["b"]);
        Assert.Equal(400, _engine.ProvincePoints(_game, "b"));
        Assert.Equal(p1Before + 400 + 100, _engine.Score(_game, "p1"));
    }

    [Fact]
    public void Attacker_takes_province_only_when_right_and_defender_wrong()
    {
        StartConquest();

        // p1 owns a, f; p2 owns b, c (castle), d; p3 owns e (castle).
        while (_engine.CurrentPlayerId(_game) != "p1") SkipTurn();
        _engine.Attack(_game, "p1", "b");
        _engine.AnswerChoice(_game, "p1", FixedQuestions.Correct);
        _engine.AnswerChoice(_game, "p2", FixedQuestions.Wrong);
        FinishReveal();

        Assert.Equal("p1", _game.Owners["b"]);
        Assert.NotEqual("p1", _engine.CurrentPlayerId(_game));
    }

    [Fact]
    public void Both_right_goes_to_numeric_tiebreak()
    {
        StartConquest();

        while (_engine.CurrentPlayerId(_game) != "p1") SkipTurn();
        _engine.Attack(_game, "p1", "b");
        _engine.AnswerChoice(_game, "p1", FixedQuestions.Correct);
        _engine.AnswerChoice(_game, "p2", FixedQuestions.Correct);
        FinishReveal();

        Assert.Equal(QuestionPurpose.BattleTiebreak, _game.Question!.Purpose);
        _engine.AnswerNumeric(_game, "p1", 150);
        _engine.AnswerNumeric(_game, "p2", 101);
        FinishReveal();

        Assert.Equal("p2", _game.Owners["b"]);
    }

    [Fact]
    public void Three_successful_attacks_on_a_castle_eliminate_the_owner()
    {
        StartConquest();
        // Give p1 the land next to p3's castle so p1 can besiege it.
        _game.Owners["d"] = "p1";

        for (var tower = 0; tower < 3; tower++)
        {
            while (_engine.CurrentPlayerId(_game) != "p1") SkipTurn();
            _engine.Attack(_game, "p1", "e");
            _engine.AnswerChoice(_game, "p1", FixedQuestions.Correct);
            _engine.AnswerChoice(_game, "p3", FixedQuestions.Wrong);
            FinishReveal();
        }

        var p3 = _game.GetPlayer("p3");
        Assert.True(p3.IsEliminated);
        Assert.Equal(0, p3.Towers);
        Assert.Equal("p1", _game.Owners["e"]);
        Assert.Empty(_game.ProvincesOf("p3"));
    }

    [Fact]
    public void A_ruined_castle_keeps_its_value_for_whoever_holds_it()
    {
        StartConquest();
        _game.Owners["d"] = "p1";
        for (var tower = 0; tower < 3; tower++)
        {
            while (_engine.CurrentPlayerId(_game) != "p1") SkipTurn();
            _engine.Attack(_game, "p1", "e");
            _engine.AnswerChoice(_game, "p1", FixedQuestions.Correct);
            _engine.AnswerChoice(_game, "p3", FixedQuestions.Wrong);
            FinishReveal();
        }

        // p1: own castle a (1000) + f, d (200 each) + the ruins of p3's castle e (1000).
        Assert.Equal(2400, _engine.Score(_game, "p1"));
        Assert.Equal(0, _engine.Score(_game, "p3"));
        Assert.Equal(1000, _engine.ProvincePoints(_game, "e"));

        // p2 takes the ruins from p1: they are worth 1000 to p2 as well.
        _game.Owners["d"] = "p2";
        var before = _engine.Score(_game, "p2");
        while (_engine.CurrentPlayerId(_game) != "p2") SkipTurn();
        _engine.Attack(_game, "p2", "e");
        _engine.AnswerChoice(_game, "p2", FixedQuestions.Correct);
        _engine.AnswerChoice(_game, "p1", FixedQuestions.Wrong);
        FinishReveal();

        Assert.Equal("p2", _game.Owners["e"]);
        Assert.Equal(before + 1000, _engine.Score(_game, "p2"));
        Assert.Equal(1200, _engine.Score(_game, "p1")); // left with castle a + f (d went to p2 above)
    }

    [Fact]
    public void Defender_who_answers_right_and_holds_gets_100_points()
    {
        StartConquest();
        var before = _engine.Score(_game, "p2");

        while (_engine.CurrentPlayerId(_game) != "p1") SkipTurn();
        _engine.Attack(_game, "p1", "b");
        _engine.AnswerChoice(_game, "p1", FixedQuestions.Wrong);
        _engine.AnswerChoice(_game, "p2", FixedQuestions.Correct);
        FinishReveal();
        Assert.Equal(before + 100, _engine.Score(_game, "p2"));

        // Both wrong: the province is kept, but that is no real defence – no bonus.
        while (_engine.CurrentPlayerId(_game) != "p1") SkipTurn();
        _engine.Attack(_game, "p1", "b");
        _engine.AnswerChoice(_game, "p1", FixedQuestions.Wrong);
        _engine.AnswerChoice(_game, "p2", FixedQuestions.Wrong);
        FinishReveal();
        Assert.Equal(before + 100, _engine.Score(_game, "p2"));

        // Winning the numeric tiebreak is a defence too.
        while (_engine.CurrentPlayerId(_game) != "p1") SkipTurn();
        _engine.Attack(_game, "p1", "b");
        _engine.AnswerChoice(_game, "p1", FixedQuestions.Correct);
        _engine.AnswerChoice(_game, "p2", FixedQuestions.Correct);
        FinishReveal();
        _engine.AnswerNumeric(_game, "p1", 150);
        _engine.AnswerNumeric(_game, "p2", 101);
        FinishReveal();
        Assert.Equal("p2", _game.Owners["b"]);
        Assert.Equal(before + 200, _engine.Score(_game, "p2"));
    }

    [Fact]
    public void Knocking_down_a_tower_keeps_the_turn_for_another_attack()
    {
        StartConquest();
        _game.Owners["d"] = "p1"; // p1 borders p3's castle in e

        while (_engine.CurrentPlayerId(_game) != "p1") SkipTurn();
        _engine.Attack(_game, "p1", "e");
        _engine.AnswerChoice(_game, "p1", FixedQuestions.Correct);
        _engine.AnswerChoice(_game, "p3", FixedQuestions.Wrong);
        FinishReveal();

        Assert.Equal(2, _game.GetPlayer("p3").Towers);
        Assert.Equal("p1", _engine.CurrentPlayerId(_game));
        _engine.Attack(_game, "p1", "e"); // straight at the capital again
        Assert.True(_game.Battle!.IsCastle);
    }

    [Fact]
    public void When_the_attack_time_runs_out_the_game_attacks_for_the_player()
    {
        StartConquest();
        var attacker = _engine.CurrentPlayerId(_game)!;
        var targets = _engine.AttackTargets(_game, attacker);

        _time.Advance(TimeSpan.FromSeconds(_game.Settings.TurnSeconds + 1));
        Assert.True(_engine.Tick(_game));

        Assert.Equal(attacker, _game.Battle!.AttackerId);
        Assert.Contains(_game.Battle.ProvinceId, targets);
        Assert.Equal(QuestionPurpose.Battle, _game.Question!.Purpose);
    }

    [Fact]
    public void In_the_last_round_any_enemy_province_can_be_attacked()
    {
        _game.Settings = _game.Settings with { ConquestRounds = 2 };
        StartConquest();
        while (_engine.CurrentPlayerId(_game) != "p1") SkipTurn();
        Assert.Equal(["b", "e"], _engine.AttackTargets(_game, "p1").Order()); // only its neighbours

        while (_game.Round < 2 || _engine.CurrentPlayerId(_game) != "p1") SkipTurn();
        Assert.Equal(["b", "c", "d", "e"], _engine.AttackTargets(_game, "p1").Order());
        _engine.Attack(_game, "p1", "c"); // far away, p2's castle
        Assert.Equal("c", _game.Battle!.ProvinceId);
    }

    [Fact]
    public void Numeric_answers_must_be_whole_numbers()
    {
        PlaceCastles();
        var ex = Assert.Throws<GameRuleException>(() => _engine.AnswerNumeric(_game, "p1", 99.5));
        Assert.Contains("цяло число", ex.Message);
        _engine.AnswerNumeric(_game, "p1", -5);
    }

    [Fact]
    public void Host_can_kick_players_and_add_bots_in_the_lobby()
    {
        var game = _engine.CreateGame("LOBBY", new GameSettings(), GameDifficulty.Hard);
        _engine.AddPlayer(game, "Домакин");
        _engine.AddPlayer(game, "Гост");
        _engine.AddPlayer(game, "Друг");

        Assert.Throws<GameRuleException>(() => _engine.KickPlayer(game, "p2", "p3")); // only the host
        Assert.Throws<GameRuleException>(() => _engine.KickPlayer(game, "p1", "p1"));
        _engine.KickPlayer(game, "p1", "p2");
        Assert.Equal(["p1", "p3"], game.Players.Select(p => p.Id));

        var bot = _engine.AddBot(game, "p1", "Хан Крум", BotLevel.Hard);
        Assert.Equal("p4", bot.Id); // ids are never reused
        Assert.Equal(3, game.Players.Select(p => p.Color).Distinct().Count());
        Assert.Throws<GameRuleException>(() => _engine.AddBot(game, "p1", "Цар Симеон", BotLevel.Hard)); // full

        _engine.Start(game, "p1");
        Assert.Throws<GameRuleException>(() => _engine.KickPlayer(game, "p1", "p3"));
    }

    [Fact]
    public void Game_difficulty_picks_the_questions()
    {
        var numeric = new[]
        {
            new NumericQuestion("e", "Лесен?", 1, Difficulty: QuestionDifficulty.Easy),
            new NumericQuestion("h", "Труден?", 2, Difficulty: QuestionDifficulty.Hard),
        };
        var choice = new[] { new ChoiceQuestion("c", "Кое?", ["а", "б", "в", "г"], 0) };
        var bank = new QuestionBank(numeric, choice, new Random(1));

        Assert.All(Enumerable.Range(0, 5), _ => Assert.Equal("h", bank.NextNumeric(null, [QuestionDifficulty.Medium, QuestionDifficulty.Hard]).Id));
        Assert.All(Enumerable.Range(0, 5), _ => Assert.Equal("e", bank.NextNumeric(null, [QuestionDifficulty.Easy, QuestionDifficulty.Medium]).Id));
    }

    [Fact]
    public void Timeout_picks_a_castle_automatically()
    {
        _time.Advance(TimeSpan.FromSeconds(_game.Settings.TurnSeconds + 1));
        Assert.True(_engine.Tick(_game));
        Assert.NotNull(_game.GetPlayer("p1").CastleProvinceId);
        Assert.Equal("p2", _engine.CurrentPlayerId(_game));
    }

    [Fact]
    public void Game_ends_after_the_last_round_and_highest_score_wins()
    {
        _game.Settings = _game.Settings with { ConquestRounds = 1 };
        StartConquest();
        for (var i = 0; i < 3 && _game.Phase != GamePhase.Finished; i++) SkipTurn();

        Assert.Equal(GamePhase.Finished, _game.Phase);
        Assert.Equal("p2", _game.WinnerId); // castle + two provinces
    }

    // ------------------------------------------------------------ helpers

    private void PlaceCastles()
    {
        _engine.ChooseCastle(_game, "p1", "a");
        _engine.ChooseCastle(_game, "p2", "c");
        _engine.ChooseCastle(_game, "p3", "e");
    }

    /// <summary>Castles a, c, e; p2 wins distribution and takes b, d; p1 takes f.</summary>
    private void StartConquest()
    {
        PlaceCastles();
        _engine.AnswerNumeric(_game, "p1", 90);
        _engine.AnswerNumeric(_game, "p2", 99);
        _engine.AnswerNumeric(_game, "p3", 500);
        FinishReveal();
        _engine.ClaimProvince(_game, "p2", "b");
        _engine.ClaimProvince(_game, "p2", "d");
        _engine.ClaimProvince(_game, "p1", "f");
    }

    private void WinBattle(string attacker, string defender, string province)
    {
        while (_engine.CurrentPlayerId(_game) != attacker) SkipTurn();
        _engine.Attack(_game, attacker, province);
        _engine.AnswerChoice(_game, attacker, FixedQuestions.Correct);
        _engine.AnswerChoice(_game, defender, FixedQuestions.Wrong);
        FinishReveal();
        Assert.Equal(attacker, _game.Owners[province]);
    }

    private void FinishReveal()
    {
        _time.Advance(TimeSpan.FromSeconds(_game.Settings.RevealSeconds + 1));
        Assert.True(_engine.Tick(_game));
    }

    /// <summary>
    /// The current player lets the clock run out. In the battles the game then attacks for them; nobody
    /// answers, so the defender holds and the turn passes on.
    /// </summary>
    private void SkipTurn()
    {
        _time.Advance(TimeSpan.FromSeconds(_game.Settings.TurnSeconds + 1));
        Assert.True(_engine.Tick(_game));
        while (_game.Phase == GamePhase.Conquest && _game.Question is not null)
        {
            _time.Advance(TimeSpan.FromSeconds(30));
            Assert.True(_engine.Tick(_game));
        }
    }
}
