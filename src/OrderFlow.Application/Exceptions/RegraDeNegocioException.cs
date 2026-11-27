namespace OrderFlow.Application.Exceptions;

public class RegraDeNegocioException : Exception
{
    public RegraDeNegocioException(string mensagem)
        : base(mensagem)
    {
    }
}