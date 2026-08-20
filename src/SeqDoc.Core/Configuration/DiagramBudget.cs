namespace SeqDoc.Core.Configuration;

public sealed record DiagramBudget
{
    public static DiagramBudget Default { get; } = new(1024, 4096, 1024, 256, 45_000);

    public DiagramBudget(
        int maxExpandedMethods,
        int maxExpandedCalls,
        int maxMaterialMessages,
        int maxParticipants,
        int maxMermaidCharacters)
    {
        Validate(maxExpandedMethods, nameof(maxExpandedMethods));
        Validate(maxExpandedCalls, nameof(maxExpandedCalls));
        Validate(maxMaterialMessages, nameof(maxMaterialMessages));
        Validate(maxParticipants, nameof(maxParticipants));
        Validate(maxMermaidCharacters, nameof(maxMermaidCharacters));
        MaxExpandedMethods = maxExpandedMethods;
        MaxExpandedCalls = maxExpandedCalls;
        MaxMaterialMessages = maxMaterialMessages;
        MaxParticipants = maxParticipants;
        MaxMermaidCharacters = maxMermaidCharacters;
    }

    public int MaxExpandedMethods { get; }
    public int MaxExpandedCalls { get; }
    public int MaxMaterialMessages { get; }
    public int MaxParticipants { get; }
    public int MaxMermaidCharacters { get; }

    private static void Validate(int value, string parameterName)
    {
        if (value < 1)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "The diagram budget must be at least 1.");
        }
    }
}
