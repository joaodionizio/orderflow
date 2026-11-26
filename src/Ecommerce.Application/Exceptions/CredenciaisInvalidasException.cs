namespace Ecommerce.Application.Exceptions;

public sealed class CredenciaisInvalidasException : Exception
{
    public const string MensagemPublica = "E-mail ou senha inválidos.";

    public CredenciaisInvalidasException() : base(MensagemPublica)
    {
    }
}
