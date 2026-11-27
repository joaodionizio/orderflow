using Ecommerce.Api.Responses;
using Ecommerce.Application.Dtos.Requests;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Ecommerce.Api.OpenApi;

internal sealed class PaginacaoOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var tipoPaginacao = context.MethodInfo.GetParameters().Select(p => p.ParameterType)
            .FirstOrDefault(typeof(PaginacaoRequest).IsAssignableFrom);
        if (tipoPaginacao is null)
            return;

        var pagina = operation.Parameters.Single(p => p.Name.Equals("pagina", StringComparison.OrdinalIgnoreCase));
        pagina.Name = "pagina";
        pagina.Schema.Minimum = PaginacaoRequest.PaginaInicial;
        pagina.Schema.Default = new OpenApiInteger(PaginacaoRequest.PaginaInicial);
        pagina.Description = $"(pagina - 1) × tamanhoPagina deve ser de no máximo {PaginacaoRequest.DeslocamentoMaximo} registros.";

        var tamanho = operation.Parameters.Single(p => p.Name.Equals("tamanhoPagina", StringComparison.OrdinalIgnoreCase));
        tamanho.Name = "tamanhoPagina";
        tamanho.Schema.Minimum = 1;
        tamanho.Schema.Maximum = PaginacaoRequest.TamanhoPaginaMaximo;
        var tamanhoPadrao = tipoPaginacao == typeof(PedidoFiltroRequest)
            ? PedidoFiltroRequest.TamanhoPaginaPadraoPedidos
            : PaginacaoRequest.TamanhoPaginaPadrao;
        tamanho.Schema.Default = new OpenApiInteger(tamanhoPadrao);

        var paginaEncontrada = operation.Responses["200"];
        paginaEncontrada.Description = "Página solicitada, com items vazio quando não houver registros na página.";
        paginaEncontrada.Content["application/json"].Example = new OpenApiObject
        {
            ["items"] = new OpenApiArray(),
            ["paginaAtual"] = new OpenApiInteger(PaginacaoRequest.PaginaInicial),
            ["tamanhoPagina"] = new OpenApiInteger(tamanhoPadrao),
            ["totalItens"] = new OpenApiLong(0),
            ["totalPaginas"] = new OpenApiLong(0)
        };

        var erro = operation.Responses["400"];
        erro.Description = "Parâmetro de consulta inválido ou limite de paginação excedido.";
        erro.Content.Clear();
        erro.Content["application/json"] = new OpenApiMediaType
        {
            Schema = context.SchemaGenerator.GenerateSchema(typeof(MensagemDeErroResponse), context.SchemaRepository)
        };
        erro.Content["application/problem+json"] = new OpenApiMediaType
        {
            Schema = context.SchemaGenerator.GenerateSchema(typeof(ValidationProblemDetails), context.SchemaRepository)
        };
    }
}
