using OrderFlow.Application.Dtos.Requests;
using OrderFlow.Application.Security;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace OrderFlow.Api.OpenApi;

internal sealed class ValidacaoDeEntradaSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type == typeof(CriarProdutoRequest))
        {
            var preco = schema.Properties["preco"];
            preco.Minimum = CriarProdutoRequest.PrecoMinimo;
            preco.Maximum = CriarProdutoRequest.PrecoMaximo;
            preco.MultipleOf = CriarProdutoRequest.PrecoMinimo;
            schema.AdditionalPropertiesAllowed = true;
        }
        else if (context.Type == typeof(LoginRequest) || context.Type == typeof(RegistrarUsuarioRequest))
        {
            var login = context.Type == typeof(LoginRequest);
            var senha = schema.Properties["senha"];
            senha.WriteOnly = true;
            senha.MinLength = login ? 1 : PoliticaDeSenhas.MinimoNova;
            senha.MaxLength = login ? PoliticaDeSenhas.MaximoLogin : PoliticaDeSenhas.MaximoNova;
            schema.AdditionalPropertiesAllowed = false;
        }
        else if (context.Type == typeof(CriarCompradorRequest) || context.Type == typeof(ItemPedidoRequest)
            || context.Type == typeof(CriarPedidoRequest) || context.Type == typeof(AtualizarPedidoRequest))
        {
            schema.AdditionalPropertiesAllowed = true;
        }
    }
}
