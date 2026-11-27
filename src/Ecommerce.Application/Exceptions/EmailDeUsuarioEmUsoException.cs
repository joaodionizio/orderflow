namespace Ecommerce.Application.Exceptions;

public sealed class EmailDeUsuarioEmUsoException : Exception
{
    public const string MensagemPublica = "Já existe um usuário cadastrado com este e-mail.";

    public EmailDeUsuarioEmUsoException() : base(MensagemPublica)
    {
    }
}
