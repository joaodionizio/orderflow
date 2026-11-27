using OrderFlow.Application.Exceptions;

namespace OrderFlow.Application.Dtos.Requests;

public class PaginacaoRequest
{
    public const int PaginaInicial = 1;
    public const int TamanhoPaginaPadrao = 20;
    public const int TamanhoPaginaMaximo = 50;
    public const int DeslocamentoMaximo = 10_000;

    public PaginacaoRequest() : this(TamanhoPaginaPadrao) { }

    protected PaginacaoRequest(int tamanhoPagina)
    {
        TamanhoPagina = tamanhoPagina;
    }

    public int Pagina { get; set; } = PaginaInicial;
    public int TamanhoPagina { get; set; }

    public (int Pagina, int TamanhoPagina, int Deslocamento) Validar()
    {
        var pagina = Pagina;
        var tamanho = TamanhoPagina;
        if (pagina < PaginaInicial)
            throw new RegraDeNegocioException($"A página deve ser maior ou igual a {PaginaInicial}.");

        if (tamanho < 1 || tamanho > TamanhoPaginaMaximo)
            throw new RegraDeNegocioException($"O tamanho da página deve estar entre 1 e {TamanhoPaginaMaximo}.");

        var deslocamento = (pagina - (long)PaginaInicial) * tamanho;
        if (deslocamento > DeslocamentoMaximo)
            throw new RegraDeNegocioException($"O deslocamento da página deve ser de no máximo {DeslocamentoMaximo} registros.");

        return (pagina, tamanho, (int)deslocamento);
    }
}
