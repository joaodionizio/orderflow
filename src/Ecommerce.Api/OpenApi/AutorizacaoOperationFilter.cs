using Ecommerce.Api.Authorization;
using Ecommerce.Api.Controllers;
using Ecommerce.Application.Exceptions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Ecommerce.Api.OpenApi;

internal sealed class AutorizacaoOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        DocumentarProtecao(operation, context);

        if (context.ApiDescription.ActionDescriptor is not ControllerActionDescriptor acao
            || acao.ControllerTypeInfo.AsType() != typeof(AuthController))
            return;

        if (acao.ActionName == nameof(AuthController.Login))
        {
            var falhaLogin = operation.Responses["401"];
            falhaLogin.Description = CredenciaisInvalidasException.MensagemPublica;
            DocumentarDesafio(falhaLogin, AuthController.DesafioLogin,
                "Esquema particular da API, não padronizado. Envie e-mail e senha válidos no corpo JSON do login.");
        }
        else if (acao.ActionName == nameof(AuthController.Registrar))
        {
            operation.Responses["409"].Description = EmailDeUsuarioEmUsoException.MensagemPublica;
        }

        foreach (var resposta in operation.Responses.Values)
        {
            resposta.Headers["Cache-Control"] = new OpenApiHeader
            {
                Required = true,
                Schema = new OpenApiSchema { Type = "string", Enum = [new OpenApiString("no-store")] }
            };
        }
    }

    private static void DocumentarProtecao(OpenApiOperation operation, OperationFilterContext context)
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
        DocumentarDesafio(operation.Responses["401"], JwtBearerDefaults.AuthenticationScheme, "Envie um token JWT válido.");
        operation.Responses.TryAdd("403", new OpenApiResponse { Description = "Usuário autenticado sem o perfil exigido." });
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            }] = []
        });
    }

    private static void DocumentarDesafio(OpenApiResponse resposta, string esquema, string descricao) =>
        resposta.Headers["WWW-Authenticate"] = new OpenApiHeader
        {
            Description = descricao,
            Required = true,
            Schema = new OpenApiSchema { Type = "string", Enum = [new OpenApiString(esquema)] }
        };
}
