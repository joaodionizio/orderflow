using Ecommerce.Api.Controllers;
using Ecommerce.Api.Health;
using Ecommerce.Api.Middlewares;
using Ecommerce.Api.Responses;
using Ecommerce.Application.Exceptions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Ecommerce.Api.OpenApi;

internal sealed class AutorizacaoOperationFilter(IAuthorizationPolicyProvider policies) : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        DocumentarLimites(operation, context);
        DocumentarCorpo(operation, context);
        var erroInterno = RespostaDeErro("Ocorreu um erro interno no servidor.", context);
        DocumentarCache(erroInterno, "no-store");
        operation.Responses.TryAdd("500", erroInterno);
        DocumentarProtecao(operation, context);

        if (context.ApiDescription.ActionDescriptor.EndpointMetadata.OfType<SondasDeSaude.Sonda>().Any())
        {
            DocumentarCache(operation.Responses["200"], "no-store, no-cache");
            if (operation.Responses.TryGetValue("503", out var indisponivel))
                DocumentarCache(indisponivel, "no-store, no-cache");
        }

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
            DocumentarCache(resposta, "no-store");
    }

    private static void DocumentarCorpo(OpenApiOperation operation, OperationFilterContext context)
    {
        var limite = context.ApiDescription.ActionDescriptor.EndpointMetadata.OfType<IRequestSizeLimitMetadata>()
            .LastOrDefault()?.MaxRequestBodySize ?? LimitesDeCorpo.Padrao;
        operation.Responses["413"] = RespostaDeErro(
            $"O corpo excede o limite de {limite} bytes. O servidor ou um proxy também pode responder diretamente.", context);

        if (operation.RequestBody is not null)
        {
            operation.RequestBody.Description = $"Limite de {limite} bytes e profundidade JSON de {LimitesDeCorpo.ProfundidadeJson} níveis.";
            operation.Responses.TryAdd("400", RespostaDeErro("Dados inválidos ou rejeitados por uma regra de negócio.", context));
            var respostaInvalida = operation.Responses["400"];
            respostaInvalida.Content.TryAdd("application/json", GerarSchema<MensagemDeErroResponse>(context));
            respostaInvalida.Content["application/problem+json"] = GerarSchema<ValidationProblemDetails>(context);
            operation.Responses.TryAdd("415", new OpenApiResponse
            {
                Description = "Formato de conteúdo não suportado.",
                Content = { ["application/problem+json"] = GerarSchema<ProblemDetails>(context) }
            });
        }
    }

    private static void DocumentarLimites(OpenApiOperation operation, OperationFilterContext context)
    {
        var resposta = RespostaDeErro("Limite de frequência ou de requisições simultâneas atingido.", context);
        resposta.Headers["Retry-After"] = new OpenApiHeader
        {
            Description = "Segundos até a renovação da janela. Ausente para limites de concorrência.",
            Schema = new OpenApiSchema { Type = "integer", Format = "int32", Minimum = 1 }
        };
        DocumentarCache(resposta, "no-store");
        operation.Responses.TryAdd("429", resposta);
    }

    private static OpenApiResponse RespostaDeErro(string descricao, OperationFilterContext context)
    {
        var resposta = new OpenApiResponse { Description = descricao };
        if (context.ApiDescription.HttpMethod != HttpMethods.Head)
            resposta.Content["application/json"] = GerarSchema<MensagemDeErroResponse>(context);

        return resposta;
    }

    private static OpenApiMediaType GerarSchema<T>(OperationFilterContext context) => new()
    {
        Schema = context.SchemaGenerator.GenerateSchema(typeof(T), context.SchemaRepository)
    };

    private static void DocumentarCache(OpenApiResponse resposta, string valor) => resposta.Headers["Cache-Control"] = new OpenApiHeader
    {
        Required = true,
        Schema = new OpenApiSchema { Type = "string", Enum = [new OpenApiString(valor)] }
    };

    private void DocumentarProtecao(OpenApiOperation operation, OperationFilterContext context)
    {
        var metadados = context.ApiDescription.ActionDescriptor.EndpointMetadata;
        if (metadados.OfType<IAllowAnonymous>().Any())
            return;

        // O Swashbuckle chama filtros de forma síncrona. O provider nativo usa políticas em memória.
        var politica = AuthorizationPolicy.CombineAsync(policies, metadados.OfType<IAuthorizeData>(),
            metadados.OfType<AuthorizationPolicy>()).GetAwaiter().GetResult();
        var requisitos = (politica?.Requirements ?? []).Concat(metadados
            .OfType<IAuthorizationRequirementData>().SelectMany(dados => dados.GetRequirements())).ToArray();
        if (requisitos.Length == 0)
            return;

        var perfis = requisitos.OfType<RolesAuthorizationRequirement>()
            .Select(requisito => $"Perfil {string.Join(" ou ", requisito.AllowedRoles)} obrigatório.");
        var descricao = string.Join(" ", perfis);
        if (descricao.Length > 0)
        {
            operation.Description = string.IsNullOrEmpty(operation.Description)
                ? descricao
                : $"{operation.Description}\n\n{descricao}";
        }

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
