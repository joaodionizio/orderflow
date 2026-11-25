using Ecommerce.Api.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Ecommerce.Api.OpenApi;

internal sealed class AutorizacaoOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var metadados = context.ApiDescription.ActionDescriptor.EndpointMetadata;
        var autorizacao = metadados.OfType<IAuthorizeData>().FirstOrDefault();
        if (autorizacao is null || metadados.OfType<IAllowAnonymous>().Any())
            return;

        var perfis = autorizacao.Policy == PoliticasDeAcesso.Provisionamento
            ? "Perfil Admin obrigatório."
            : "Perfil Admin ou Operator obrigatório.";
        operation.Description = string.IsNullOrEmpty(operation.Description)
            ? perfis
            : $"{operation.Description}\n\n{perfis}";
        operation.Responses.TryAdd("401", new OpenApiResponse { Description = "Token ausente ou inválido." });
        operation.Responses.TryAdd("403", new OpenApiResponse { Description = "Usuário autenticado sem o perfil exigido." });
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            }] = []
        });
    }
}
