namespace BulgariaConquest.Core.Rules;

public sealed record NumericGuess(string PlayerId, double? Value, int ResponseMs);

public static class NumericResolver
{
    /// <summary>
    /// Orders guesses best-first: closest to the correct answer, then fastest.
    /// Players who did not answer come last.
    /// </summary>
    public static IReadOnlyList<NumericGuess> Rank(IEnumerable<NumericGuess> guesses, double correct) =>
        guesses
            .OrderBy(g => g.Value is null)
            .ThenBy(g => g.Value is { } v ? Math.Abs(v - correct) : double.MaxValue)
            .ThenBy(g => g.ResponseMs)
            .ToList();
}

public enum BattleOutcome
{
    AttackerWins,
    DefenderHolds,

    /// <summary>Both answered correctly – a numeric question decides.</summary>
    Tiebreak,
}

public static class BattleResolver
{
    /// <summary>The attacker takes the province only if they are right and the defender is wrong.</summary>
    public static BattleOutcome Resolve(bool attackerCorrect, bool defenderCorrect) =>
        (attackerCorrect, defenderCorrect) switch
        {
            (true, false) => BattleOutcome.AttackerWins,
            (true, true) => BattleOutcome.Tiebreak,
            _ => BattleOutcome.DefenderHolds,
        };
}
