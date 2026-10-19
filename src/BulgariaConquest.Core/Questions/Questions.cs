namespace BulgariaConquest.Core.Questions;

public enum QuestionDifficulty
{
    Easy,
    Medium,
    Hard,
}

/// <summary>Question with a number for an answer – the closest guess wins, ties go to the faster player.</summary>
public sealed record NumericQuestion(
    string Id, string Text, double Answer, string? Unit = null, string? Category = null,
    QuestionDifficulty Difficulty = QuestionDifficulty.Medium);

/// <summary>Classic question with four options.</summary>
public sealed record ChoiceQuestion(
    string Id, string Text, IReadOnlyList<string> Options, int CorrectIndex, string? Category = null,
    QuestionDifficulty Difficulty = QuestionDifficulty.Medium);

public interface IQuestionProvider
{
    NumericQuestion NextNumeric();
    ChoiceQuestion NextChoice();

    /// <summary>Categories that have numeric / choice questions (offered by the "Тема" lifeline). Empty = no themes.</summary>
    IReadOnlyList<string> NumericCategories => [];
    IReadOnlyList<string> ChoiceCategories => [];

    /// <summary>Next question from one category.</summary>
    NumericQuestion NextNumeric(string category) => NextNumeric();
    ChoiceQuestion NextChoice(string category) => NextChoice();
}

/// <summary>
/// Deals questions like a shuffled deck (no repeats until the deck runs out) and shuffles answer options.
/// Each category also has its own deck, for questions asked by theme.
/// </summary>
public sealed class QuestionBank : IQuestionProvider
{
    private readonly IReadOnlyList<NumericQuestion> _numeric;
    private readonly IReadOnlyList<ChoiceQuestion> _choice;
    private readonly Dictionary<string, List<NumericQuestion>> _numericByCategory;
    private readonly Dictionary<string, List<ChoiceQuestion>> _choiceByCategory;
    private readonly Random _random;
    private readonly Queue<NumericQuestion> _numericDeck = new();
    private readonly Queue<ChoiceQuestion> _choiceDeck = new();
    private readonly Dictionary<string, Queue<NumericQuestion>> _numericCategoryDecks = [];
    private readonly Dictionary<string, Queue<ChoiceQuestion>> _choiceCategoryDecks = [];
    private readonly Lock _lock = new();

    public QuestionBank(IReadOnlyList<NumericQuestion> numeric, IReadOnlyList<ChoiceQuestion> choice, Random? random = null)
    {
        if (numeric.Count == 0 || choice.Count == 0)
            throw new ArgumentException("The question bank needs at least one numeric and one choice question.");
        foreach (var q in choice)
            if (q.Options.Count != 4 || q.CorrectIndex is < 0 or > 3)
                throw new InvalidDataException($"Choice question '{q.Id}' must have 4 options and a valid correct index.");

        _numeric = numeric;
        _choice = choice;
        _numericByCategory = ByCategory(numeric, q => q.Category);
        _choiceByCategory = ByCategory(choice, q => q.Category);
        NumericCategories = _numericByCategory.Keys.ToList();
        ChoiceCategories = _choiceByCategory.Keys.ToList();
        _random = random ?? Random.Shared;
    }

    public int NumericCount => _numeric.Count;
    public int ChoiceCount => _choice.Count;

    public IReadOnlyList<string> NumericCategories { get; }
    public IReadOnlyList<string> ChoiceCategories { get; }

    public NumericQuestion NextNumeric()
    {
        lock (_lock) return Draw(_numericDeck, _numeric);
    }

    public NumericQuestion NextNumeric(string category)
    {
        if (!_numericByCategory.TryGetValue(category, out var source)) return NextNumeric();
        lock (_lock) return Draw(DeckFor(_numericCategoryDecks, category), source);
    }

    public ChoiceQuestion NextChoice()
    {
        ChoiceQuestion q;
        lock (_lock) q = Draw(_choiceDeck, _choice);
        return Shuffled(q);
    }

    public ChoiceQuestion NextChoice(string category)
    {
        if (!_choiceByCategory.TryGetValue(category, out var source)) return NextChoice();
        ChoiceQuestion q;
        lock (_lock) q = Draw(DeckFor(_choiceCategoryDecks, category), source);
        return Shuffled(q);
    }

    private ChoiceQuestion Shuffled(ChoiceQuestion q)
    {
        var order = Enumerable.Range(0, q.Options.Count).OrderBy(_ => _random.Next()).ToArray();
        return q with
        {
            Options = order.Select(i => q.Options[i]).ToArray(),
            CorrectIndex = Array.IndexOf(order, q.CorrectIndex),
        };
    }

    private T Draw<T>(Queue<T> deck, IReadOnlyList<T> source)
    {
        if (deck.Count == 0)
            foreach (var item in source.OrderBy(_ => _random.Next()))
                deck.Enqueue(item);
        return deck.Dequeue();
    }

    private static Queue<T> DeckFor<T>(Dictionary<string, Queue<T>> decks, string category) =>
        decks.TryGetValue(category, out var deck) ? deck : decks[category] = new Queue<T>();

    private static Dictionary<string, List<T>> ByCategory<T>(IEnumerable<T> questions, Func<T, string?> category) =>
        questions
            .Where(q => !string.IsNullOrWhiteSpace(category(q)))
            .GroupBy(q => category(q)!)
            .ToDictionary(g => g.Key, g => g.ToList());
}
