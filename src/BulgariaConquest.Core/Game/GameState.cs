using BulgariaConquest.Core.Questions;
using BulgariaConquest.Core.Rules;

namespace BulgariaConquest.Core.Game;

// Plain, serialisable state. Everything here is saved as one JSON file per game,
// so the whole match can be resumed after a server restart.

public enum GamePhase
{
    Lobby,
    CastleSelection,
    Distribution,
    Conquest,
    Finished,
}

public enum QuestionKind
{
    Numeric,
    Choice,
}

public enum QuestionPurpose
{
    /// <summary>Numeric question deciding who claims free provinces first.</summary>
    Distribution,

    /// <summary>Choice question between attacker and defender.</summary>
    Battle,

    /// <summary>Numeric question when both attacker and defender were right.</summary>
    BattleTiebreak,
}

public sealed class GameState
{
    public string Id { get; set; } = "";
    public int Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public GamePhase Phase { get; set; } = GamePhase.Lobby;
    public GameSettings Settings { get; set; } = new();
    public List<PlayerState> Players { get; set; } = [];

    /// <summary>Province id → owning player id (null = free land).</summary>
    public Dictionary<string, string?> Owners { get; set; } = [];

    /// <summary>Player ids in the order they take turns (castle selection and conquest).</summary>
    public List<string> TurnOrder { get; set; } = [];
    public int TurnIndex { get; set; }
    public int Round { get; set; }

    /// <summary>Deadline for the action the game is currently waiting for (castle, claim, attack).</summary>
    public DateTimeOffset? ActionDeadline { get; set; }

    public QuestionRound? Question { get; set; }
    public List<PendingPick> Picks { get; set; } = [];
    public Battle? Battle { get; set; }

    public string? WinnerId { get; set; }
    public List<LogEntry> Log { get; set; } = [];
    public List<ChatMessage> Chat { get; set; } = [];

    public PlayerState? FindPlayer(string id) => Players.FirstOrDefault(p => p.Id == id);
    public PlayerState GetPlayer(string id) => FindPlayer(id) ?? throw new GameRuleException("Няма такъв играч.");
    public IEnumerable<PlayerState> ActivePlayers => Players.Where(p => !p.IsEliminated);
    public IEnumerable<string> ProvincesOf(string playerId) => Owners.Where(o => o.Value == playerId).Select(o => o.Key);
}

public sealed class PlayerState
{
    public string Id { get; set; } = "";

    /// <summary>Secret used by the client to act as this player. Never sent to other players.</summary>
    public string Token { get; set; } = "";

    public string Name { get; set; } = "";
    public string Color { get; set; } = "";
    public string? CastleProvinceId { get; set; }
    public int Towers { get; set; }
    public bool IsEliminated { get; set; }

    /// <summary>Points that are not tied to land (e.g. taken over from an eliminated player).</summary>
    public int BonusPoints { get; set; }

    /// <summary>Set for computer-controlled players; the server's BotDirector plays for them.</summary>
    public BotLevel? BotLevel { get; set; }

    public bool IsBot => BotLevel is not null;

    /// <summary>Lifelines already spent this game (each kind can be used once).</summary>
    public List<LifelineKind> UsedLifelines { get; set; } = [];

    /// <summary>Category picked with the "Тема" lifeline; the player's next attack asks a question from it.</summary>
    public string? ThemeForNextAttack { get; set; }
}

/// <summary>"Who Wants to Be a Millionaire"-style help, usable once per game each.</summary>
public enum LifelineKind
{
    /// <summary>Removes two wrong options (choice questions).</summary>
    FiftyFifty,

    /// <summary>"Летописец": a range that surely contains the answer (numeric questions, e.g. years).</summary>
    Range,

    /// <summary>A friend suggests an answer – usually, but not always, right (any question).</summary>
    Friend,

    /// <summary>"Тема": before attacking, the player picks the category of the battle question (conquest only).</summary>
    Category,
}

/// <summary>What a lifeline revealed. Only the player who used it ever sees this.</summary>
public sealed class LifelineResult
{
    public LifelineKind Kind { get; set; }
    public List<int>? RemovedOptions { get; set; }
    public double? RangeLow { get; set; }
    public double? RangeHigh { get; set; }
    public int? FriendOption { get; set; }
    public double? FriendNumber { get; set; }
    public int FriendConfidence { get; set; }
    public string? Category { get; set; }
}

public sealed record ChatMessage(DateTimeOffset At, string PlayerId, string Text);

public enum BotLevel
{
    Easy,
    Normal,
    Hard,
}

public sealed class QuestionRound
{
    public QuestionKind Kind { get; set; }
    public QuestionPurpose Purpose { get; set; }
    public string QuestionId { get; set; } = "";
    public string Text { get; set; } = "";
    public string? Category { get; set; }
    public QuestionDifficulty Difficulty { get; set; } = QuestionDifficulty.Medium;

    public List<string>? Options { get; set; }
    public int? CorrectIndex { get; set; }
    public double? CorrectNumber { get; set; }
    public string? Unit { get; set; }

    public List<string> Participants { get; set; } = [];
    public Dictionary<string, SubmittedAnswer> Answers { get; set; } = [];
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset Deadline { get; set; }

    /// <summary>Set once the question is resolved; the result stays on screen until then.</summary>
    public DateTimeOffset? RevealUntil { get; set; }

    /// <summary>Numeric: participants best-first. Choice: participants who answered correctly.</summary>
    public List<string> Ranking { get; set; } = [];
    public List<string> CorrectPlayers { get; set; } = [];

    /// <summary>Lifelines used on this question, per player – private results.</summary>
    public Dictionary<string, List<LifelineResult>> Lifelines { get; set; } = [];

    public bool IsResolved => RevealUntil is not null;
}

public sealed class SubmittedAnswer
{
    public double? Number { get; set; }
    public int? Choice { get; set; }
    public int ResponseMs { get; set; }
}

public sealed class PendingPick
{
    public string PlayerId { get; set; } = "";
    public int Remaining { get; set; }
}

public sealed class Battle
{
    public string AttackerId { get; set; } = "";
    public string DefenderId { get; set; } = "";
    public string ProvinceId { get; set; } = "";
    public bool IsCastle { get; set; }

    /// <summary>Category chosen by the attacker with the "Тема" lifeline (null = any).</summary>
    public string? Category { get; set; }
}

public sealed record LogEntry(DateTimeOffset At, string Text);

/// <summary>A player tried something the rules do not allow. The message is shown to the player.</summary>
public sealed class GameRuleException(string message) : Exception(message);
