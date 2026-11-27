using OrderFlow.Api.Authorization;
using OrderFlow.Api.Commands;
using OrderFlow.Api.Health;
using OrderFlow.Api.Middlewares;
using OrderFlow.Api.OpenApi;
using OrderFlow.Api.RateLimiting;
using OrderFlow.Infrastructure;
using OrderFlow.Infrastructure.Configuration;
using OrderFlow.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;

var provisionarAdmin = args.FirstOrDefault() == ProvisionarAdminCommand.Nome;
if (provisionarAdmin && args.Length != 1)
{
    Console.Error.WriteLine("Execute provision-admin sem argumentos. Os dados são solicitados no terminal.");
    Environment.ExitCode = 1;
    return;
}

var builder = WebApplication.CreateBuilder(provisionarAdmin ? [] : args);

var encaminhamento = provisionarAdmin ? null : TransporteHttp.Configurar(builder);
var connectionString = SqlServerConfiguration.GetRequiredConnectionString(builder.Configuration);
var jwt = JwtConfiguration.Load(builder.Configuration);

builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = LimitesDeCorpo.Padrao);
// Hospedagem, roteamento e MVC podem registrar URLs e argumentos fornecidos pelo cliente.
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.AspNetCore.Routing", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.AspNetCore.HttpOverrides", LogLevel.Warning);
// Esses eventos podem carregar exceções de autenticação e detalhes do protocolo original.
builder.Logging.AddFilter(typeof(JwtBearerHandler).FullName, LogLevel.None);
builder.Logging.AddFilter("Microsoft.AspNetCore.Server.Kestrel.BadRequests", LogLevel.None);
// O formatador pode registrar mensagens JSON com entrada do cliente em nível Debug.
builder.Logging.AddFilter(typeof(SystemTextJsonInputFormatter).FullName, LogLevel.None);
builder.Logging.AddFilter(DbLoggerCategory.Database.Name, LogLevel.Warning);
builder.Logging.AddFilter(DbLoggerCategory.Database.Command.Name, LogLevel.None);
builder.Logging.AddFilter(DbLoggerCategory.Query.Name, LogLevel.Warning);
builder.Services.AddSingleton(jwt);
builder.Services.AdicionarLimitesHttp(OpcoesDeLimites.Carregar(builder.Configuration));
builder.Services.AddControllers(options =>
{
    options.Filters.Add<LimitesDeCorpo>();
    var mensagens = options.ModelBindingMessageProvider;

    mensagens.SetAttemptedValueIsInvalidAccessor((_, campo) =>
        $"O valor informado para o campo {campo} não é válido.");
    mensagens.SetMissingBindRequiredValueAccessor(campo =>
        $"O campo {campo} é obrigatório.");
    mensagens.SetMissingKeyOrValueAccessor(() =>
        "Valor obrigatório não informado.");
    mensagens.SetUnknownValueIsInvalidAccessor(campo =>
        $"O valor informado para o campo {campo} não é válido.");
    mensagens.SetValueIsInvalidAccessor(_ =>
        "O valor informado não é válido.");
    mensagens.SetValueMustBeANumberAccessor(campo =>
        $"O campo {campo} deve ser um número.");
    mensagens.SetValueMustNotBeNullAccessor(_ =>
        "O campo é obrigatório.");
}).AddJsonOptions(options =>
{
    options.JsonSerializerOptions.MaxDepth = LimitesDeCorpo.ProfundidadeJson;
    options.AllowInputFormatterExceptionMessages = false;
});
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var detalhes = new ValidationProblemDetails(context.ModelState)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Ocorreram um ou mais erros de validação.",
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
            Instance = context.HttpContext.Request.Path
        };

        // O caminho de uma falha JSON pode conter um nome de propriedade enviado pelo cliente.
        var caminhosJson = detalhes.Errors.Keys.Where(chave => chave.StartsWith('$')).ToArray();
        if (caminhosJson.Length > 0)
        {
            foreach (var caminho in caminhosJson)
                detalhes.Errors.Remove(caminho);
            detalhes.Errors["$"] = ["O corpo JSON é inválido."];
        }

        detalhes.Extensions["traceId"] = context.HttpContext.TraceIdentifier;

        return new BadRequestObjectResult(detalhes);
    };
});
builder.Services.AddEndpointsApiExplorer();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.IncludeErrorDetails = false;
        options.TokenValidationParameters = jwt.CreateTokenValidationParameters();
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = ValidacaoDeClaimsJwt.ValidarAsync
        };
    });
builder.Services.AddAuthorization(PoliticasDeAcesso.Configurar);
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo { Title = Program.IdentificadorPublicoOpenApi, Version = "1.0" });
        options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Informe o token JWT."
        });

        options.OperationFilter<AutorizacaoOperationFilter>();
        options.OperationFilter<PaginacaoOperationFilter>();
        options.SchemaFilter<ValidacaoDeEntradaSchemaFilter>();
    });
}
builder.Services.AdicionarSondasDeSaude(connectionString);
builder.Services.AddInfrastructure(connectionString);

await using var app = builder.Build();

if (provisionarAdmin)
{
    Environment.ExitCode = await ProvisionarAdminCommand.ExecutarAsync(app.Services);
    return;
}

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    dbContext.Database.Migrate();
}

app.UsarTransporteSeguro(encaminhamento);
app.UseRouting();
app.UseMiddleware<ExceptionMiddleware>();
app.UseRateLimiter();
app.Use(LimitesHttp.LimitarFrequenciaAsync);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();
app.Use(LimitesHttp.LimitarUsuarioAsync);
app.Use(LimitesDeCorpo.VerificarAsync);

app.MapControllers();
app.MapearSondasDeSaude();

await app.RunAsync();

public partial class Program
{
    internal const string IdentificadorPublicoOpenApi = "Ecommerce.Api";
}
