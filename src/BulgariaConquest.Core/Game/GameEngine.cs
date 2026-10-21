using BulgariaConquest.Core.Map;
using BulgariaConquest.Core.Questions;
using BulgariaConquest.Core.Rules;

namespace BulgariaConquest.Core.Game;

/// <summary>
/// All game rules live here. The engine is stateless: every method takes the <see cref="GameState"/>
/// it should change, so the same engine serves all games and is easy to unit test.
///
/// Flow: Lobby → CastleSelection → Distribution (numeric questions decide who claims free land)
///       → Conquest (attacks decided by 4-option questions, numeric tiebreaks) → Finished.
/// </summary>
public sealed partial class GameEngine(GameMap map, IQuestionProvider questions, TimeProvider? time = null, Random? random = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Random _random = random ?? Random.Shared;

    private DateTimeOffset Now => _time.GetUtcNow();

    public GameMap Map => map;

    // ---------------------------------------------------------------- lobby

    public GameState CreateGame(string id, GameSettings settings)
    {
        var game = new GameState { Id = id, Settings = settings, CreatedAt = Now, UpdatedAt = Now };
        foreach (var p in map.Provinces) game.Owners[p.Id] = null;
        Log(game, "Играта е създадена.");
        return game;
    }

    public PlayerState AddPlayer(GameState game, string name, BotLevel? bot = null)
    {
        Require(game.Phase == GamePhase.Lobby, "Играта вече е започнала.");
        name = (name ?? "").Trim();
        Require(name.Length is >= 1 and <= 20, "Името трябва да е между 1 и 20 символа.");
        Require(game.Players.Count < game.Settings.MaxPlayers, "Играта е пълна.");
        Require(!game.Players.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)), "Това име вече е заето.");

        var colors = game.Settings.PlayerColors;
        var player = new PlayerState
        {
            Id = $"p{game.Players.Count + 1}",
            Token = Guid.NewGuid().ToString("N"),
            Name = name,
            Color = colors[game.Players.Count % colors.Length],
            Towers = game.Settings.TowersPerCastle,
            BotLevel = bot,
        };
        game.Players.Add(player);
        Log(game, bot is null ? $"{name} се присъедини." : $"{name} (бот) се присъедини.");
        return player;
    }

    public void Start(GameState game, string playerId)
    {
        Require(game.Phase == GamePhase.Lobby, "Играта вече е започнала.");
        Require(game.Players.FirstOrDefault()?.Id == playerId, "Само създателят на играта може да я стартира.");
        Require(game.Players.Count >= game.Settings.MinPlayers, $"Нужни са поне {game.Settings.MinPlayers} играчи.");

        game.TurnOrder = game.Players.Select(p => p.Id).ToList();
        game.TurnIndex = 0;
        game.Phase = GamePhase.CastleSelection;
        Log(game, "Играта започна! Всеки играч избира област за своя замък.");
        StartTurnTimer(game);
    }

    // ---------------------------------------------------------------- castles

    /// <summary>Free provinces that do not border an existing castle.</summary>
    public IReadOnlyList<string> CastleOptions(GameState game) =>
        game.Owners
            .Where(o => o.Value is null)
            .Select(o => o.Key)
            .Where(id => !game.Players.Any(p => p.CastleProvinceId is { } c && (c == id || map.AreNeighbors(c, id))))
            .ToList();

    public void ChooseCastle(GameState game, string playerId, string provinceId)
    {
        Require(game.Phase == GamePhase.CastleSelection, "Сега не се избират замъци.");
        RequireTurn(game, playerId);
        Require(CastleOptions(game).Contains(provinceId), "Замъкът трябва да е в свободна област, несъседна на друг замък.");
        PlaceCastle(game, game.GetPlayer(playerId), provinceId);
    }

    private void PlaceCastle(GameState game, PlayerState player, string provinceId)
    {
        player.CastleProvinceId = provinceId;
        game.Owners[provinceId] = player.Id;
        Log(game, $"{player.Name} построи замък в {map[provinceId].Name}.");

        game.TurnIndex++;
        if (game.TurnIndex < game.TurnOrder.Count)
        {
            StartTurnTimer(game);
            return;
        }

        game.TurnIndex = 0;
        game.Phase = GamePhase.Distribution;
        Log(game, "Разпределение на земите: най-точният отговор избира първи.");
        StartQuestion(game, QuestionPurpose.Distribution, game.ActivePlayers.Select(p => p.Id).ToList());
    }

    // ---------------------------------------------------------------- distribution

    public IReadOnlyList<string> ClaimOptions(GameState game, string playerId)
    {
        var free = game.Owners.Where(o => o.Value is null).Select(o => o.Key).ToList();
        if (!game.Settings.RequireAdjacentClaims) return free;

        var own = game.ProvincesOf(playerId).ToHashSet();
        var adjacent = free.Where(f => map.NeighborsOf(f).Any(own.Contains)).ToList();
        return adjacent.Count > 0 ? adjacent : free;
    }

    public void ClaimProvince(GameState game, string playerId, string provinceId)
    {
        Require(game.Phase == GamePhase.Distribution && game.Question is null, "Сега не може да се заемат земи.");
        Require(game.Picks.FirstOrDefault()?.PlayerId == playerId, "Не е твой ред да избираш.");
        Require(ClaimOptions(game, playerId).Contains(provinceId), "Тази област не може да бъде заета.");

        game.Owners[provinceId] = playerId;
        Log(game, $"{game.GetPlayer(playerId).Name} зае {map[provinceId].Name}.");

        var pick = game.Picks[0];
        if (--pick.Remaining <= 0) game.Picks.RemoveAt(0);

        if (game.Owners.Values.All(o => o is not null))
            BeginConquest(game);
        else if (game.Picks.Count == 0)
            StartQuestion(game, QuestionPurpose.Distribution, game.ActivePlayers.Select(p => p.Id).ToList());
        else
            StartTurnTimer(game);
    }

    // ---------------------------------------------------------------- conquest

    /// <summary>Enemy provinces bordering the player's land.</summary>
    public IReadOnlyList<string> AttackTargets(GameState game, string playerId)
    {
        var own = game.ProvincesOf(playerId).ToHashSet();
        return game.Owners
            .Where(o => o.Value is { } owner && owner != playerId && !game.GetPlayer(owner).IsEliminated)
            .Select(o => o.Key)
            .Where(id => map.NeighborsOf(id).Any(own.Contains))
            .ToList();
    }

    public void Attack(GameState game, string playerId, string provinceId)
    {
        Require(game.Phase == GamePhase.Conquest && game.Battle is null && game.Question is null, "Сега не може да се напада.");
        RequireTurn(game, playerId);
        Require(AttackTargets(game, playerId).Contains(provinceId), "Можеш да нападаш само съседни вражески области.");

        var attacker = game.GetPlayer(playerId);
        var defender = game.GetPlayer(game.Owners[provinceId]!);
        game.Battle = new Battle
        {
            AttackerId = playerId,
            DefenderId = defender.Id,
            ProvinceId = provinceId,
            IsCastle = defender.CastleProvinceId == provinceId,
            Category = attacker.ThemeForNextAttack,
        };
        attacker.ThemeForNextAttack = null;
        Log(game, $"{attacker.Name} напада {(game.Battle.IsCastle ? "замъка на " + defender.Name + " в " : "")}{map[provinceId].Name}!"
            + (game.Battle.Category is { } theme ? $" Тема на въпроса: „{theme}“." : ""));
        StartQuestion(game, QuestionPurpose.Battle, [playerId, defender.Id]);
    }

    private void BeginConquest(GameState game)
    {
        game.Phase = GamePhase.Conquest;
        game.Picks.Clear();
        game.Round = 1;
        game.TurnIndex = -1;
        Log(game, "Всички земи са заети. Започват битките!");
        ShuffleTurnOrder(game);
        AdvanceTurn(game);
    }

    /// <summary>
    /// Draws a random attack order for the current conquest round. Every active player still attacks once
    /// per round; nobody attacks twice in a row across the round boundary, and the order differs from the
    /// previous round whenever that is possible.
    /// </summary>
    private void ShuffleTurnOrder(GameState game)
    {
        var active = game.TurnOrder.Where(id => !game.GetPlayer(id).IsEliminated).ToList();
        if (active.Count < 2) return; // the game is over anyway
        var lastToAct = game.RoundOrders.Count > 0 ? active.LastOrDefault() : null;

        var allowed = Permutations(active).Where(o => o.Count < 2 || o[0] != lastToAct).ToList();
        var fresh = allowed.Where(o => !o.SequenceEqual(active)).ToList();
        var pool = fresh.Count > 0 ? fresh : allowed.Count > 0 ? allowed : [active];

        game.TurnOrder = pool[_random.Next(pool.Count)]
            .Concat(game.TurnOrder.Where(id => game.GetPlayer(id).IsEliminated))
            .ToList();
        game.RoundOrders.Add(game.TurnOrder.ToList());
        var names = game.TurnOrder.Take(active.Count).Select(id => game.GetPlayer(id).Name);
        Log(game, $"Ред на атаките в рунд {game.Round}: {string.Join(", ", names)}.");
    }

    private static IEnumerable<List<string>> Permutations(List<string> items)
    {
        if (items.Count <= 1)
        {
            yield return items.ToList();
            yield break;
        }
        for (var i = 0; i < items.Count; i++)
        {
            var rest = items.Where((_, j) => j != i).ToList();
            foreach (var tail in Permutations(rest))
                yield return [items[i], .. tail];
        }
    }

    private void AdvanceTurn(GameState game)
    {
        game.ActionDeadline = null;
        for (var guard = 0; guard < game.TurnOrder.Count * 2; guard++)
        {
            if (game.ActivePlayers.Count() <= 1) break;

            game.TurnIndex++;
            if (game.TurnIndex >= game.TurnOrder.Count)
            {
                game.TurnIndex = 0;
                game.Round++;
                if (game.Round > game.Settings.ConquestRounds) break;
                Log(game, $"Рунд {game.Round} от {game.Settings.ConquestRounds}.");
                ShuffleTurnOrder(game);
            }

            var player = game.GetPlayer(game.TurnOrder[game.TurnIndex]);
            if (player.IsEliminated) continue;
            if (AttackTargets(game, player.Id).Count == 0)
            {
                Log(game, $"{player.Name} няма съседни врагове и пропуска хода си.");
                continue;
            }

            StartTurnTimer(game);
            return;
        }

        Finish(game);
    }

    private void ApplyBattle(GameState game, bool attackerWon)
    {
        var battle = game.Battle!;
        var attacker = game.GetPlayer(battle.AttackerId);
        var defender = game.GetPlayer(battle.DefenderId);
        var province = map[battle.ProvinceId].Name;

        if (!attackerWon)
        {
            Log(game, $"{defender.Name} удържа {province}.");
        }
        else if (!battle.IsCastle)
        {
            game.Owners[battle.ProvinceId] = attacker.Id;
            // The value belongs to the province, not to the capture: retaking it later never counts twice.
            game.ConqueredProvinces.Add(battle.ProvinceId);
            Log(game, $"{attacker.Name} превзе {province} (+{game.Settings.ConqueredTerritoryPoints} т.).");
        }
        else
        {
            defender.Towers--;
            Log(game, $"{attacker.Name} събори кула на замъка на {defender.Name} (остават {defender.Towers}).");
            if (defender.Towers <= 0) Eliminate(game, defender, attacker);
        }

        game.Battle = null;
        game.Question = null;
        AdvanceTurn(game);
    }

    private void Eliminate(GameState game, PlayerState loser, PlayerState winner)
    {
        foreach (var id in game.ProvincesOf(loser.Id).ToList())
            game.Owners[id] = winner.Id;
        winner.BonusPoints += loser.BonusPoints;
        loser.BonusPoints = 0;
        loser.IsEliminated = true;
        Log(game, $"Замъкът на {loser.Name} падна! {winner.Name} получава всичките му земи и точки.");
    }

    private void Finish(GameState game)
    {
        game.Phase = GamePhase.Finished;
        game.Question = null;
        game.Battle = null;
        game.ActionDeadline = null;

        var winner = game.ActivePlayers
            .OrderByDescending(p => Score(game, p.Id))
            .ThenByDescending(p => game.ProvincesOf(p.Id).Count())
            .FirstOrDefault();
        game.WinnerId = winner?.Id;
        Log(game, winner is null ? "Играта приключи." : $"Играта приключи! Победител е {winner.Name} с {Score(game, winner.Id)} точки.");
    }

    public int Score(GameState game, string playerId) =>
        game.ProvincesOf(playerId).Sum(id => ProvincePoints(game, id)) + game.GetPlayer(playerId).BonusPoints;

    /// <summary>
    /// What a province is worth to whoever holds it: a standing castle is worth <see cref="GameSettings.CastlePoints"/>,
    /// the ruins of a fallen castle ("old capital") <see cref="GameSettings.RuinedCastlePoints"/>,
    /// a province taken in battle <see cref="GameSettings.ConqueredTerritoryPoints"/>,
    /// every other province <see cref="GameSettings.TerritoryPoints"/>.
    /// </summary>
    public int ProvincePoints(GameState game, string provinceId)
    {
        var builder = game.Players.FirstOrDefault(p => p.CastleProvinceId == provinceId);
        if (builder is not null) return builder.IsEliminated ? game.Settings.RuinedCastlePoints : game.Settings.CastlePoints;
        return game.ConqueredProvinces.Contains(provinceId) ? game.Settings.ConqueredTerritoryPoints : game.Settings.TerritoryPoints;
    }

    /// <summary>Point value of every province, for display on the map.</summary>
    public IReadOnlyDictionary<string, int> AllProvincePoints(GameState game) =>
        game.Owners.Keys.ToDictionary(id => id, id => ProvincePoints(game, id));

    // ---------------------------------------------------------------- questions

    private void StartQuestion(GameState game, QuestionPurpose purpose, List<string> participants)
    {
        game.ActionDeadline = null;
        // Clients announce the question during the intro and show it at StartedAt; the timer runs from there.
        var start = Now.AddSeconds(Math.Max(0, game.Settings.QuestionIntroSeconds));
        var round = new QuestionRound { Purpose = purpose, Participants = participants, StartedAt = start };

        // A battle (and its tiebreak) asks from the theme the attacker picked with the "Тема" lifeline.
        var theme = purpose == QuestionPurpose.Distribution ? null : game.Battle?.Category;
        if (purpose == QuestionPurpose.Battle) Fill(game, round, theme is null ? questions.NextChoice() : questions.NextChoice(theme));
        else Fill(game, round, theme is null ? questions.NextNumeric() : questions.NextNumeric(theme));
        game.Question = round;
    }

    private void Fill(GameState game, QuestionRound round, ChoiceQuestion q)
    {
        round.Kind = QuestionKind.Choice;
        round.QuestionId = q.Id;
        round.Text = q.Text;
        round.Category = q.Category;
        round.Difficulty = q.Difficulty;
        round.Options = q.Options.ToList();
        round.CorrectIndex = q.CorrectIndex;
        round.CorrectNumber = null;
        round.Unit = null;
        round.Deadline = round.StartedAt.AddSeconds(game.Settings.ChoiceAnswerSeconds);
    }

    private void Fill(GameState game, QuestionRound round, NumericQuestion q)
    {
        round.Kind = QuestionKind.Numeric;
        round.QuestionId = q.Id;
        round.Text = q.Text;
        round.Category = q.Category;
        round.Difficulty = q.Difficulty;
        round.Options = null;
        round.CorrectIndex = null;
        round.CorrectNumber = q.Answer;
        round.Unit = q.Unit;
        round.Deadline = round.StartedAt.AddSeconds(game.Settings.NumericAnswerSeconds);
    }

    public void AnswerNumeric(GameState game, string playerId, double value)
    {
        Require(double.IsFinite(value), "Невалиден отговор.");
        var q = RequireOpenQuestion(game, playerId, QuestionKind.Numeric);
        q.Answers[playerId] = new SubmittedAnswer { Number = value, ResponseMs = ElapsedMs(q) };
        if (q.Participants.All(q.Answers.ContainsKey)) ResolveQuestion(game);
    }

    public void AnswerChoice(GameState game, string playerId, int optionIndex)
    {
        var q = RequireOpenQuestion(game, playerId, QuestionKind.Choice);
        Require(optionIndex >= 0 && optionIndex < q.Options!.Count, "Невалиден отговор.");
        q.Answers[playerId] = new SubmittedAnswer { Choice = optionIndex, ResponseMs = ElapsedMs(q) };
        if (q.Participants.All(q.Answers.ContainsKey)) ResolveQuestion(game);
    }

    private QuestionRound RequireOpenQuestion(GameState game, string playerId, QuestionKind kind)
    {
        var q = game.Question;
        Require(q is not null && !q.IsResolved && q.Kind == kind, "Няма активен въпрос.");
        Require(q!.Participants.Contains(playerId), "Този въпрос не е за теб.");
        Require(!q.Answers.ContainsKey(playerId), "Вече отговори.");
        return q;
    }

    private void ResolveQuestion(GameState game)
    {
        var q = game.Question!;
        if (q.Kind == QuestionKind.Numeric)
        {
            var guesses = q.Participants.Select(id =>
                q.Answers.TryGetValue(id, out var a) ? new NumericGuess(id, a.Number, a.ResponseMs) : new NumericGuess(id, null, int.MaxValue));
            q.Ranking = NumericResolver.Rank(guesses, q.CorrectNumber!.Value).Select(g => g.PlayerId).ToList();
            Log(game, $"Верен отговор: {q.CorrectNumber}{(q.Unit is null ? "" : " " + q.Unit)} – най-близо е {game.GetPlayer(q.Ranking[0]).Name}.");
        }
        else
        {
            q.CorrectPlayers = q.Participants.Where(id => q.Answers.TryGetValue(id, out var a) && a.Choice == q.CorrectIndex).ToList();
        }
        q.RevealedAt = Now;
        q.RevealUntil = Now.AddSeconds(game.Settings.RevealSeconds);
    }

    /// <summary>Called after the reveal pause: applies the result of the question to the game.</summary>
    private void CompleteQuestion(GameState game)
    {
        var q = game.Question!;
        switch (q.Purpose)
        {
            case QuestionPurpose.Distribution:
                game.Question = null;
                var free = game.Owners.Values.Count(o => o is null);
                game.Picks = q.Ranking
                    .Select((id, rank) => new PendingPick
                    {
                        PlayerId = id,
                        Remaining = rank < game.Settings.DistributionPicks.Length ? game.Settings.DistributionPicks[rank] : 0,
                    })
                    .Where(p => p.Remaining > 0 && q.Answers.ContainsKey(p.PlayerId))
                    .ToList();
                if (game.Picks.Count == 0 && free > 0)
                    StartQuestion(game, QuestionPurpose.Distribution, game.ActivePlayers.Select(p => p.Id).ToList());
                else
                    StartTurnTimer(game);
                break;

            case QuestionPurpose.Battle:
                var battle = game.Battle!;
                var outcome = BattleResolver.Resolve(
                    q.CorrectPlayers.Contains(battle.AttackerId),
                    q.CorrectPlayers.Contains(battle.DefenderId));
                if (outcome == BattleOutcome.Tiebreak)
                {
                    Log(game, "И двамата отговориха вярно – решава числов въпрос!");
                    StartQuestion(game, QuestionPurpose.BattleTiebreak, [battle.AttackerId, battle.DefenderId]);
                }
                else
                {
                    ApplyBattle(game, outcome == BattleOutcome.AttackerWins);
                }
                break;

            case QuestionPurpose.BattleTiebreak:
                var best = q.Ranking[0];
                ApplyBattle(game, best == game.Battle!.AttackerId && q.Answers.ContainsKey(best));
                break;
        }
    }

    // ---------------------------------------------------------------- time

    /// <summary>
    /// Moves the game forward when a deadline passes. The server calls this a few times per second.
    /// Returns true if the state changed.
    /// </summary>
    public bool Tick(GameState game)
    {
        if (game.Phase is GamePhase.Lobby or GamePhase.Finished) return false;
        var now = Now;

        if (game.Question is { } q)
        {
            if (!q.IsResolved && now >= q.Deadline) { ResolveQuestion(game); return true; }
            if (q.IsResolved && now >= q.RevealUntil) { CompleteQuestion(game); return true; }
            return false;
        }

        if (game.ActionDeadline is { } deadline && now >= deadline)
        {
            AutoAct(game);
            return true;
        }
        return false;
    }

    /// <summary>The player ran out of time: the game acts for them.</summary>
    private void AutoAct(GameState game)
    {
        var playerId = CurrentPlayerId(game);
        if (playerId is null) return;
        var player = game.GetPlayer(playerId);

        switch (game.Phase)
        {
            case GamePhase.CastleSelection:
                Log(game, $"Времето на {player.Name} изтече – замъкът е избран случайно.");
                PlaceCastle(game, player, PickRandom(CastleOptions(game)));
                break;
            case GamePhase.Distribution:
                Log(game, $"Времето на {player.Name} изтече – областта е избрана случайно.");
                ClaimProvince(game, playerId, PickRandom(ClaimOptions(game, playerId)));
                break;
            case GamePhase.Conquest:
                Log(game, $"Времето на {player.Name} изтече – ходът е пропуснат.");
                AdvanceTurn(game);
                break;
        }
    }

    /// <summary>Whose action the game is waiting for (null while a question is running).</summary>
    public string? CurrentPlayerId(GameState game)
    {
        if (game.Question is not null) return null;
        return game.Phase switch
        {
            GamePhase.CastleSelection or GamePhase.Conquest when game.TurnIndex >= 0 && game.TurnIndex < game.TurnOrder.Count
                => game.TurnOrder[game.TurnIndex],
            GamePhase.Distribution => game.Picks.FirstOrDefault()?.PlayerId,
            _ => null,
        };
    }

    // ---------------------------------------------------------------- helpers

    private void StartTurnTimer(GameState game) => game.ActionDeadline = Now.AddSeconds(game.Settings.TurnSeconds);

    private int ElapsedMs(QuestionRound q) => (int)Math.Max(0, (Now - q.StartedAt).TotalMilliseconds);

    private string PickRandom(IReadOnlyList<string> items) => items[_random.Next(items.Count)];

    private void RequireTurn(GameState game, string playerId) =>
        Require(CurrentPlayerId(game) == playerId, "Не е твой ред.");

    private void Log(GameState game, string text)
    {
        game.Log.Add(new LogEntry(Now, text));
        if (game.Log.Count > 200) game.Log.RemoveRange(0, game.Log.Count - 200);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new GameRuleException(message);
    }
}
