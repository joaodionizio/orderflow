using System.ComponentModel.DataAnnotations;

namespace OrderFlow.Api.RateLimiting;

internal sealed class OpcoesDeLimites
{
    public const string Secao = "RateLimiting";
    private const int MaximoPorJanela = 100_000;
    private const int MaximoConcorrente = 1024;

    [Range(1, 3600)] public int JanelaSegundos { get; init; } = 60;
    [Range(1, MaximoPorJanela)] public int GlobalPorJanela { get; init; } = 1200;
    [Range(1, MaximoConcorrente)] public int GlobalConcorrente { get; init; } = 32;
    [Range(1, MaximoPorJanela)] public int LoginPorOrigem { get; init; } = 20;
    [Range(1, MaximoPorJanela)] public int LoginPorIdentificador { get; init; } = 5;
    [Range(1, MaximoConcorrente)] public int LoginConcorrente { get; init; } = 4;
    [Range(1, MaximoPorJanela)] public int CadastroPorAdmin { get; init; } = 10;
    [Range(1, MaximoPorJanela)] public int OperacoesPorUsuario { get; init; } = 120;
    [Range(1, MaximoPorJanela)] public int SaudePorJanela { get; init; } = 30;
    [Range(1, MaximoConcorrente)] public int SaudeConcorrente { get; init; } = 1;
    [Range(1, MaximoPorJanela)] public int LivenessPorJanela { get; init; } = 30;
    [Range(1, MaximoConcorrente)] public int LivenessConcorrente { get; init; } = 1;
    [Range(1, 100_000)] public int MaximoChavesPorJanela { get; init; } = 1024;

    internal TimeSpan Janela => TimeSpan.FromSeconds(JanelaSegundos);

    public static OpcoesDeLimites Carregar(IConfiguration configuration)
    {
        var secao = configuration.GetSection(Secao);
        if (secao.Value is { Length: > 0 })
            throw new ValidationException($"A seção {Secao} deve conter opções individuais.");

        var opcoes = secao.Get<OpcoesDeLimites>(binder => binder.ErrorOnUnknownConfiguration = true) ?? new();
        Validator.ValidateObject(opcoes, new ValidationContext(opcoes), validateAllProperties: true);
        return opcoes;
    }
}
