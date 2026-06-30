namespace MyEfVibe;

internal readonly record struct QueryPagingOptions(int Skip, int PageSize)
{
    internal int PageIndex => PageSize > 0 ? Skip / PageSize : 0;

    internal bool IsRequested => PageSize > 0;
}
