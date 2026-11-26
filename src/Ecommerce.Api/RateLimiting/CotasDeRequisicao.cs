using System.Net;
using System.Security.Cryptography;
using System.Text;
using Ecommerce.Application.Security;

namespace Ecommerce.Api.RateLimiting;

internal sealed class CotasDeRequisicao(OpcoesDeLimites opcoes, TimeProvider relogio)
{
    private static readonly byte[] ChaveDeIdentificadores = RandomNumberGenerator.GetBytes(32);
    private readonly JanelaPorChave<bool> _global = new(relogio, opcoes.Janela, opcoes.GlobalPorJanela, maximoChaves: 1);
    private readonly JanelaPorChave<bool> _readiness = new(relogio, opcoes.Janela, opcoes.SaudePorJanela, maximoChaves: 1);
    private readonly JanelaPorChave<bool> _liveness = new(relogio, opcoes.Janela, opcoes.LivenessPorJanela, maximoChaves: 1);
    private readonly JanelaPorChave<Origem> _origens = new(
        relogio, opcoes.Janela, opcoes.LoginPorOrigem, opcoes.MaximoChavesPorJanela);
    private readonly JanelaPorChave<string> _identificadores = new(
        relogio, opcoes.Janela, opcoes.LoginPorIdentificador, opcoes.MaximoChavesPorJanela);
    private readonly JanelaPorChave<Guid> _cadastros = new(
        relogio, opcoes.Janela, opcoes.CadastroPorAdmin, opcoes.MaximoChavesPorJanela);
    private readonly JanelaPorChave<Guid> _operacoes = new(
        relogio, opcoes.Janela, opcoes.OperacoesPorUsuario, opcoes.MaximoChavesPorJanela);

    public TimeSpan? ConsumirOrcamento(OrcamentoHttp orcamento) => (orcamento switch
    {
        OrcamentoHttp.Readiness => _readiness,
        OrcamentoHttp.Liveness => _liveness,
        _ => _global
    }).TentarConsumir(true);

    public TimeSpan? ConsumirOrigem(IPAddress? ip) =>
        _origens.TentarConsumir(new Origem(ip is { IsIPv4MappedToIPv6: true } ? ip.MapToIPv4() : ip));

    public TimeSpan? ConsumirIdentificador(string email)
    {
        var normalizado = EmailDeUsuario.Normalizar(email);
        var chave = HMACSHA256.HashData(ChaveDeIdentificadores, Encoding.UTF8.GetBytes(normalizado));
        return _identificadores.TentarConsumir(Convert.ToHexString(chave));
    }

    public TimeSpan? ConsumirCadastro(Guid usuario) => _cadastros.TentarConsumir(usuario);

    public TimeSpan? ConsumirOperacao(Guid usuario) => _operacoes.TentarConsumir(usuario);

    private readonly record struct Origem(IPAddress? Endereco);
}
