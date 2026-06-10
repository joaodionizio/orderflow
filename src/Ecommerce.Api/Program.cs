using System.Text.Json;
using System.Text;
using Ecommerce.Api.Middlewares;
using Ecommerce.Infrastructure;
using Ecommerce.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers(options =>
{
    var mensagens = options.ModelBindingMessageProvider;

    mensagens.SetAttemptedValueIsInvalidAccessor((valor, campo) =>
        $"O valor '{valor}' não é válido para o campo {campo}.");
    mensagens.SetMissingBindRequiredValueAccessor(campo =>
        $"O campo {campo} é obrigatório.");
    mensagens.SetMissingKeyOrValueAccessor(() =>
        "Valor obrigatório não informado.");
    mensagens.SetUnknownValueIsInvalidAccessor(campo =>
        $"O valor informado para o campo {campo} não é válido.");
    mensagens.SetValueIsInvalidAccessor(valor =>
        $"O valor '{valor}' não é válido.");
    mensagens.SetValueMustBeANumberAccessor(campo =>
        $"O campo {campo} deve ser um número.");
    mensagens.SetValueMustNotBeNullAccessor(_ =>
        "O campo é obrigatório.");
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

        detalhes.Extensions["traceId"] = context.HttpContext.TraceIdentifier;

        return new BadRequestObjectResult(detalhes);
    };
});
builder.Services.AddEndpointsApiExplorer();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var chave = builder.Configuration["Jwt:Chave"];

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(chave!)),
            ClockSkew = TimeSpan.Zero
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Informe o token JWT."
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            []
        }
    });
});
builder.Services
    .AddHealthChecks()
    .AddSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")!,
        name: "sqlserver");
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    dbContext.Database.Migrate();
}

app.UseMiddleware<ExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";

        var response = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString()
            })
        };

        await context.Response.WriteAsync(
            JsonSerializer.Serialize(response));
    }
});

app.Run();
