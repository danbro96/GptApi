namespace GptApi.Validation;

public readonly record struct SchemaVerdict(bool Ok, string? Error)
{
    public static readonly SchemaVerdict Pass = new(true, null);
    public static SchemaVerdict Fail(string error) => new(false, error);
}
