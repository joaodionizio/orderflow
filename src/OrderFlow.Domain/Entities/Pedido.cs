using OrderFlow.Domain.Enums;

namespace OrderFlow.Domain.Entities;

public class Pedido
{
    public Guid Id { get; set; }

    public Guid CompradorId { get; set; }

    public Comprador Comprador { get; set; } = null!;

    public StatusPedido Status { get; set; } = StatusPedido.Iniciado;

    public DateTime DataCriacao { get; set; } = DateTime.UtcNow;

    public DateTime? DataAtualizacao { get; set; }

    public ICollection<ItemPedido> Itens { get; set; } = new List<ItemPedido>();

    public decimal CalcularValorTotal() => SomarItens(Itens).Valor;

    public static (decimal Valor, bool Exato) SomarItens(IEnumerable<ItemPedido> itens)
    {
        // Somar subtotais muito grandes pode arredondar centavos a cada adição.
        // Com até 100 itens válidos, a soma dos inteiros cabe em decimal e as frações são exatas.
        var inteiros = 0m;
        var fracoes = 0m;
        foreach (var item in itens)
        {
            var subtotal = item.CalcularSubtotal();
            var parteInteira = decimal.Truncate(subtotal);
            inteiros += parteInteira;
            fracoes += subtotal - parteInteira;
        }

        var inteirosDasFracoes = decimal.Truncate(fracoes);
        inteiros += inteirosDasFracoes;
        fracoes -= inteirosDasFracoes;
        var total = inteiros + fracoes;
        return (total, total - inteiros == fracoes);
    }

    public bool PodeSerAlterado()
    {
        return Status == StatusPedido.Iniciado;
    }

    public bool PodeSerProcessado()
    {
        return Status == StatusPedido.Iniciado;
    }

    public bool PodeSerCancelado()
    {
        return Status == StatusPedido.Iniciado ||
               Status == StatusPedido.Processado;
    }

    public bool PodeSerEnviado()
    {
        return Status == StatusPedido.Processado;
    }

    public bool PodeSerExcluido()
    {
        return Status == StatusPedido.Iniciado;
    }
}
