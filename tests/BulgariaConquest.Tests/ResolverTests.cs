using BulgariaConquest.Core.Rules;

namespace BulgariaConquest.Tests;

public class ResolverTests
{
    [Fact]
    public void Numeric_closest_wins_then_fastest_and_silent_players_are_last()
    {
        var ranked = NumericResolver.Rank(
        [
            new NumericGuess("slow-exact", 1878, 9000),
            new NumericGuess("silent", null, int.MaxValue),
            new NumericGuess("fast-exact", 1878, 2000),
            new NumericGuess("far", 1900, 100),
        ], 1878);

        Assert.Equal(["fast-exact", "slow-exact", "far", "silent"], ranked.Select(r => r.PlayerId));
    }

    [Theory]
    [InlineData(true, false, BattleOutcome.AttackerWins)]
    [InlineData(true, true, BattleOutcome.Tiebreak)]
    [InlineData(false, true, BattleOutcome.DefenderHolds)]
    [InlineData(false, false, BattleOutcome.DefenderHolds)]
    public void Battle_needs_attacker_right_and_defender_wrong(bool attacker, bool defender, BattleOutcome expected) =>
        Assert.Equal(expected, BattleResolver.Resolve(attacker, defender));
}
