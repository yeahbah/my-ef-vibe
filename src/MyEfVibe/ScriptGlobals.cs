namespace MyEfVibe;

public sealed class ScriptGlobals<TContext> where TContext : class
{
#pragma warning disable IDE1006

    public TContext dbContext { get; init; } = null!;

#pragma warning restore IDE1006
}
