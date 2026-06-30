using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Reflection;

namespace MyEfVibe;

internal static class RawSqlExecutor
{
    internal static async Task<(object? Result, EvaluationMetrics Metrics, IReadOnlyList<Dictionary<string, string>>? Rows)>
        ExecuteAsync(
            object dbContext,
            string sql,
            IEnumerable<Assembly> inspectionAssemblies,
            DbLogSettings dbLogSettings,
            CancellationToken cancellationToken = default,
            QueryPagingOptions? paging = null)
    {
        var trimmed = sql.Trim().TrimEnd(';');

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new InvalidOperationException("SQL is empty.");
        }

        using var sqlCapture = EfSqlCapture.TryAttach(dbContext, dbLogSettings);
        var stopwatch = Stopwatch.StartNew();
        var warnings = new List<string>
        {
            "Executed as raw SQL — verify injection safety and access controls."
        };

        try
        {
            if (RawSqlClassifier.LooksLikeQuery(trimmed))
            {
                return await ExecuteQueryAsync(
                    dbContext,
                    trimmed,
                    inspectionAssemblies,
                    sqlCapture,
                    stopwatch,
                    warnings,
                    paging,
                    cancellationToken);
            }

            var rowsAffected = await ExecuteNonQueryAsync(
                dbContext,
                trimmed,
                inspectionAssemblies,
                cancellationToken);

            if (rowsAffected < 0 && RawSqlClassifier.ContainsQueryStatement(trimmed))
            {
                return await ExecuteQueryAsync(
                    dbContext,
                    trimmed,
                    inspectionAssemblies,
                    sqlCapture,
                    stopwatch,
                    warnings,
                    paging,
                    cancellationToken);
            }

            stopwatch.Stop();

            var commandMetrics = BuildMetrics(
                trimmed,
                stopwatch.ElapsedMilliseconds,
                sqlCapture,
                ResultKind.Scalar,
                "raw-sql-command",
                null,
                true,
                warnings);

            return ($"{rowsAffected} row(s) affected", commandMetrics, null);
        }
        catch (Exception failure)
        {
            stopwatch.Stop();

            throw new EvaluationFailedException(
                EvaluationMetrics.Failed(trimmed, stopwatch.ElapsedMilliseconds, failure.Message),
                failure);
        }
    }

    private static async Task<(object? Result, EvaluationMetrics Metrics, IReadOnlyList<Dictionary<string, string>>? Rows)>
        ExecuteQueryAsync(
            object dbContext,
            string trimmed,
            IEnumerable<Assembly> inspectionAssemblies,
            EfSqlCapture? sqlCapture,
            Stopwatch stopwatch,
            List<string> warnings,
            QueryPagingOptions? paging,
            CancellationToken cancellationToken)
    {
        var (rows, totalCount, hasMore) = await ReadQueryRowsAsync(
            dbContext,
            trimmed,
            inspectionAssemblies,
            paging,
            cancellationToken);

        stopwatch.Stop();

        var metrics = BuildMetrics(
            trimmed,
            stopwatch.ElapsedMilliseconds,
            sqlCapture,
            ResultKind.Enumerable,
            "raw-sql",
            totalCount,
            true,
            warnings,
            paging,
            hasMore);

        var value = totalCount switch
        {
            0 => "(empty)",
            1 when rows.Count == 1 => FormatSingleRowSummary(rows[0]),
            _ => $"{totalCount} row(s)"
        };

        return (value, metrics, rows);
    }

    private static EvaluationMetrics BuildMetrics(
        string snippet,
        long totalMilliseconds,
        EfSqlCapture? sqlCapture,
        ResultKind resultKind,
        string resultTypeName,
        int? rowCount,
        bool succeeded,
        IReadOnlyList<string> warnings,
        QueryPagingOptions? paging = null,
        bool? hasMore = null)
    {
        var executedSql = sqlCapture?.Commands.Select(EfSqlCapture.FormatEntry).ToArray() ?? [];

        if (executedSql.Length == 0)
        {
            executedSql = [snippet];
        }

        return new EvaluationMetrics
        {
            Snippet = snippet,
            TotalMilliseconds = totalMilliseconds,
            DatabaseMilliseconds = sqlCapture is { HasEntries: true } ? sqlCapture.TotalDatabaseMilliseconds : null,
            SqlCommandCount = sqlCapture?.Commands.Count ?? 1,
            ExecutedSql = executedSql,
            ResultKind = resultKind,
            ResultTypeName = resultTypeName,
            RowCount = rowCount,
            IsMaterialized = true,
            Warnings = warnings,
            Succeeded = succeeded,
            PageIndex = paging?.PageIndex,
            PageSize = paging?.PageSize,
            HasMore = hasMore,
            PagingSupported = RawSqlClassifier.LooksLikeQuery(snippet),
        };
    }

    private static async Task<(IReadOnlyList<Dictionary<string, string>> Rows, int TotalCount, bool HasMore)>
        ReadQueryRowsAsync(
            object dbContext,
            string sql,
            IEnumerable<Assembly> inspectionAssemblies,
            QueryPagingOptions? paging,
            CancellationToken cancellationToken)
    {
        var skip = paging?.Skip ?? 0;
        var pageSize = paging?.PageSize ?? QueryPagingRewriter.DefaultPageSize;
        var maxRowsToRead = skip + pageSize + 1;

        await using var scope = await OpenConnectionScopeAsync(dbContext, inspectionAssemblies, cancellationToken);

        await using var command = scope.Connection.CreateCommand();
        command.CommandText = sql;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var columnNames = Enumerable.Range(0, reader.FieldCount)
            .Select(reader.GetName)
            .ToArray();
        var rows = new List<Dictionary<string, string>>();
        var seenIndex = 0;
        var hasMore = false;

        while (await reader.ReadAsync(cancellationToken))
        {
            if (seenIndex >= skip + pageSize)
            {
                hasMore = true;
                break;
            }

            if (seenIndex >= skip)
            {
                var row = new Dictionary<string, string>(columnNames.Length, StringComparer.Ordinal);

                for (var column = 0; column < reader.FieldCount; column++)
                {
                    row[columnNames[column]] = reader.IsDBNull(column)
                        ? string.Empty
                        : TabularExportBuilder.FormatScalar(reader.GetValue(column));
                }

                rows.Add(row);
            }

            seenIndex++;

            if (seenIndex >= maxRowsToRead)
            {
                hasMore = true;
                break;
            }
        }

        return (rows, rows.Count, hasMore);
    }

    private static async Task<int> ExecuteNonQueryAsync(
        object dbContext,
        string sql,
        IEnumerable<Assembly> inspectionAssemblies,
        CancellationToken cancellationToken)
    {
        await using var scope = await OpenConnectionScopeAsync(dbContext, inspectionAssemblies, cancellationToken);

        await using var command = scope.Connection.CreateCommand();
        command.CommandText = sql;

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<ConnectionScope> OpenConnectionScopeAsync(
        object dbContext,
        IEnumerable<Assembly> inspectionAssemblies,
        CancellationToken cancellationToken)
    {
        var database = dbContext.GetType().GetProperty("Database")?.GetValue(dbContext)
                       ?? throw new InvalidOperationException("Database facade not found.");

        if (!RelationalDatabaseFacadeInvoker.TryGetDbConnection(database, inspectionAssemblies, out var connection)
            || connection is not DbConnection dbConnection)
        {
            throw new InvalidOperationException(
                "Could not resolve EF Core GetDbConnection. Ensure Microsoft.EntityFrameworkCore.Relational is loaded.");
        }

        var openedHere = dbConnection.State != ConnectionState.Open;

        if (openedHere)
        {
            await dbConnection.OpenAsync(cancellationToken);
        }

        return new ConnectionScope(dbConnection, openedHere);
    }

    private static string FormatSingleRowSummary(IReadOnlyDictionary<string, string> row)
    {
        if (row.Count == 1)
        {
            return row.Values.First();
        }

        return string.Join(", ", row.Select(static pair => $"{pair.Key}={pair.Value}"));
    }

    private sealed class ConnectionScope : IAsyncDisposable
    {
        private readonly bool _openedHere;

        internal ConnectionScope(DbConnection connection, bool openedHere)
        {
            Connection = connection;
            _openedHere = openedHere;
        }

        internal DbConnection Connection { get; }

        public async ValueTask DisposeAsync()
        {
            if (_openedHere && Connection.State == ConnectionState.Open)
            {
                await Connection.CloseAsync();
            }
        }
    }
}
