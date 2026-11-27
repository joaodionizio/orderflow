using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace OrderFlow.Tests;

internal sealed class ComandosObservaveis : DbCommandInterceptor
{
    public ConcurrentQueue<string> Executados { get; } = new();

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Executados.Enqueue(command.CommandText);
        return new(result);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        Executados.Enqueue(command.CommandText);
        return new(result);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Executados.Enqueue(command.CommandText);
        return new(result);
    }
}
