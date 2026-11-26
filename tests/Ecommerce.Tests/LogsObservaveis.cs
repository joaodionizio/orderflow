using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Ecommerce.Tests;

internal sealed class LogsObservaveis : ILoggerProvider
{
    public ConcurrentQueue<Registro> Registros { get; } = new();

    public ILogger CreateLogger(string categoryName) => new Captura(categoryName, Registros);
    public void Dispose() { }

    internal sealed record Registro(string Categoria, LogLevel Nivel, string Mensagem, Exception? Excecao);

    private sealed class Captura(string categoria, ConcurrentQueue<Registro> registros) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            registros.Enqueue(new Registro(categoria, logLevel, formatter(state, exception), exception));
    }
}
