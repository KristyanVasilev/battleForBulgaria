namespace BulgariaConquest.Core.Rules;

/// <summary>Tunable rules. Loaded from Data/game-settings.json on the server.</summary>
public sealed record GameSettings
{
    public int MinPlayers { get; init; } = 3;
    public int MaxPlayers { get; init; } = 3;

    /// <summary>Towers on each player's main castle; each successful attack on it knocks one down.</summary>
    public int TowersPerCastle { get; init; } = 3;

    public int ChoiceAnswerSeconds { get; init; } = 15;
    public int NumericAnswerSeconds { get; init; } = 20;

    /// <summary>Time to pick a castle / claim a province / declare an attack before the server acts.</summary>
    public int TurnSeconds { get; init; } = 30;

    /// <summary>How long the correct answer (and everyone's pick) is shown before the game moves on.</summary>
    public int RevealSeconds { get; init; } = 6;

    /// <summary>
    /// Pause between announcing a question (e.g. "X attacks Y") and showing it, so players see what is
    /// at stake on the map. The answer timer starts after it.
    /// </summary>
    public double QuestionIntroSeconds { get; init; } = 2.5;

    /// <summary>Full rotations of attacks (every active player attacks once per round).</summary>
    public int ConquestRounds { get; init; } = 6;

    public int TerritoryPoints { get; init; } = 200;

    /// <summary>A province taken in battle (conquest phase) is worth this much to whoever holds it.</summary>
    public int ConqueredTerritoryPoints { get; init; } = 400;
    public int CastlePoints { get; init; } = 1000;

    /// <summary>Bonus for the defender who answers right and keeps the province (or castle tower).</summary>
    public int DefensePoints { get; init; } = 100;

    /// <summary>A fallen castle ("old capital") keeps its value for whoever holds the ruins.</summary>
    public int RuinedCastlePoints { get; init; } = 1000;

    /// <summary>How many free provinces the 1st, 2nd, 3rd... closest guess may claim in a distribution round.</summary>
    public int[] DistributionPicks { get; init; } = [2, 1, 0];

    /// <summary>When true, claims must border your own land (if any such free province exists).</summary>
    public bool RequireAdjacentClaims { get; init; } = true;

    public string[] PlayerColors { get; init; } = ["#e7a95f", "#b5524a", "#7d6ba6", "#4f9a8a", "#4f7fb8"];
}
