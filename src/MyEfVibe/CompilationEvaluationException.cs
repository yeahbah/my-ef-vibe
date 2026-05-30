namespace MyEfVibe;

internal sealed class CompilationEvaluationException : Exception
{
    internal CompilationEvaluationException(string message)
        : base(message)
    {
    }
}