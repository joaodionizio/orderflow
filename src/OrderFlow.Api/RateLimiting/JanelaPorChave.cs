namespace OrderFlow.Api.RateLimiting;

internal sealed class JanelaPorChave<TChave>(
    TimeProvider relogio, TimeSpan janela, int limite, int maximoChaves) where TChave : notnull
{
    private readonly object _sincronizacao = new();
    private readonly Dictionary<TChave, int> _contadores = new();
    private long _inicio = relogio.GetTimestamp();

    // Uma rejeição informa quanto resta até a renovação da janela.
    public TimeSpan? TentarConsumir(TChave chave)
    {
        lock (_sincronizacao)
        {
            var agora = relogio.GetTimestamp();
            var decorrido = relogio.GetElapsedTime(_inicio, agora);
            if (decorrido >= janela)
            {
                _contadores.Clear();
                _inicio = agora;
                decorrido = TimeSpan.Zero;
            }

            if (_contadores.TryGetValue(chave, out var utilizadas))
            {
                if (utilizadas >= limite)
                    return janela - decorrido;
            }
            else if (_contadores.Count >= maximoChaves)
            {
                return janela - decorrido;
            }

            _contadores[chave] = utilizadas + 1;
            return null;
        }
    }
}
