using BulgariaConquest.Core.Questions;

namespace BulgariaConquest.Tests;

/// <summary>Clock the test moves by hand.</summary>
internal sealed class ManualTime : TimeProvider
{
    private DateTimeOffset _now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>Always the same questions: numeric answer 100, choice answer index 0.</summary>
internal sealed class FixedQuestions : IQuestionProvider
{
    public const int Correct = 0;
    public const int Wrong = 1;

    public NumericQuestion NextNumeric() => new("n", "Колко?", 100);

    public ChoiceQuestion NextChoice() => new("c", "Кое?", ["вярно", "грешно", "грешно", "грешно"], Correct);
}
