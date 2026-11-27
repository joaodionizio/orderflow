namespace OrderFlow.Application.Dtos.Responses;

public class ResultadoPaginadoResponse<T>
{
    public List<T> Items { get; set; } = [];

    public int PaginaAtual { get; set; }

    public int TamanhoPagina { get; set; }

    public long TotalItens { get; set; }

    public long TotalPaginas { get; set; }

    public static ResultadoPaginadoResponse<T> Criar(List<T> items, int pagina, int tamanhoPagina, long totalItens)
    {
        var totalPaginas = Math.DivRem(totalItens, tamanhoPagina, out var restante);
        return new ResultadoPaginadoResponse<T>
        {
            Items = items,
            PaginaAtual = pagina,
            TamanhoPagina = tamanhoPagina,
            TotalItens = totalItens,
            TotalPaginas = totalPaginas + (restante > 0 ? 1 : 0)
        };
    }
}
