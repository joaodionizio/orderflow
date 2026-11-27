using System.ComponentModel.DataAnnotations;

namespace OrderFlow.Application.Security;

[AttributeUsage(AttributeTargets.Property)]
internal sealed class ValidarSenhaAttribute(bool nova) : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var senha = value as string;
        var erro = nova ? PoliticaDeSenhas.ValidarNova(senha) : PoliticaDeSenhas.ValidarLogin(senha);
        return erro is null ? ValidationResult.Success : new ValidationResult(erro,
            validationContext.MemberName is { } membro ? [membro] : []);
    }
}
