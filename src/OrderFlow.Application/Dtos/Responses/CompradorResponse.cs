namespace OrderFlow.Application.Dtos.Responses;

public class CompradorResponse
{
    public Guid Id { get; set; }

    public string Nome { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public DateTime DataCriacao { get; set; }
}