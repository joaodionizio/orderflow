using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Ecommerce.Tests;

public class LimitesDeCorpoKestrelTests(AutorizacaoApiFixture fixture) : IClassFixture<AutorizacaoApiFixture>
{
    [SqlServerFact]
    public async Task AplicaLimitesDeTransporteComOuSemContentLength()
    {
        var endereco = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var processo = new Process { StartInfo = CriarProcesso(), EnableRaisingEvents = true };
        processo.OutputDataReceived += (_, evento) =>
        {
            const string prefixo = "Now listening on: ";
            var linha = evento.Data?.Trim();
            if (linha?.StartsWith(prefixo, StringComparison.Ordinal) == true && Uri.TryCreate(linha[prefixo.Length..],
                UriKind.Absolute, out var uri))
            {
                endereco.TrySetResult(uri);
            }
        };
        processo.Exited += (_, _) => endereco.TrySetException(
            new InvalidOperationException($"Kestrel encerrou antes de iniciar. Código: {processo.ExitCode}."));

        Assert.True(processo.Start());
        try
        {
            processo.BeginOutputReadLine();
            processo.BeginErrorReadLine();
            using var client = new HttpClient { BaseAddress = await endereco.Task.WaitAsync(TimeSpan.FromSeconds(45)) };

            foreach (var emBlocos in new[] { false, true })
            {
                foreach (var diferenca in new[] { -1, 0, 1 })
                {
                    var login = JsonSerializer.Serialize(new
                    {
                        email = $"{Guid.NewGuid():N}@example.test",
                        senha = "SentinelaPrivadaDoKestrel"
                    });
                    await VerificarAsync(client, HttpMethod.Post, "/api/v1/auth/login", login, 16_384 + diferenca,
                        emBlocos, diferenca > 0 ? HttpStatusCode.RequestEntityTooLarge : HttpStatusCode.Unauthorized);

                    await VerificarAsync(client, HttpMethod.Post, "/api/v1/produtos", """{"nome":"Teste de corpo","preco":1.00}""",
                        262_144 + diferenca, emBlocos, diferenca > 0 ? HttpStatusCode.RequestEntityTooLarge : HttpStatusCode.Created,
                        fixture.OperatorToken);
                }
            }

            // O PATCH não lê JSON. Um corpo em blocos ainda precisa ser verificado antes da ação.
            await VerificarAsync(client, HttpMethod.Patch, $"/api/v1/pedidos/{Guid.NewGuid()}/processar", "",
                262_145, emBlocos: true, HttpStatusCode.RequestEntityTooLarge, fixture.OperatorToken);
            await VerificarAsync(client, HttpMethod.Post, "/api/v1/auth/login", "{?", 16_385,
                emBlocos: true, HttpStatusCode.RequestEntityTooLarge);
            await VerificarAsync(client, HttpMethod.Post, "/api/v1/auth/login", "texto", 16_385,
                emBlocos: true, HttpStatusCode.RequestEntityTooLarge, tipo: "text/plain");

            var email = new string('a', 187) + "@example.test";
            var emailEscapado = string.Concat(email.Select(caractere => $"\\u{(int)caractere:X4}"));
            var senhaEscapada = string.Concat(Enumerable.Repeat(@"\uD83D\uDD10", 1024));
            var legado = $$"""{"email":"{{emailEscapado}}","senha":"{{senhaEscapada}}"}""";
            Assert.True(legado.Length < 16_384);
            await VerificarAsync(client, HttpMethod.Post, "/api/v1/auth/login", legado, legado.Length,
                emBlocos: false, HttpStatusCode.Unauthorized);
        }
        finally
        {
            if (!processo.HasExited)
                processo.Kill(entireProcessTree: true);
            await processo.WaitForExitAsync();
        }
    }

    private ProcessStartInfo CriarProcesso()
    {
        var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = AppContext.BaseDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        info.ArgumentList.Add(typeof(Program).Assembly.Location);
        foreach (var (chave, valor) in fixture.Database.Configuration)
            info.Environment[chave.Replace(":", "__", StringComparison.Ordinal)] = valor;
        info.Environment["DOTNET_ENVIRONMENT"] = "Development";
        info.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        info.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:0";
        info.Environment["Logging__LogLevel__Microsoft.Hosting.Lifetime"] = "Information";
        return info;
    }

    private static async Task VerificarAsync(HttpClient client, HttpMethod metodo, string rota, string json,
        int bytesObservados, bool emBlocos, HttpStatusCode esperado, string? token = null, string tipo = "application/json")
    {
        var tamanho = bytesObservados;
        if (emBlocos)
        {
            // Um único bloco: tamanho em hexadecimal, dois CRLF e o terminador "0\r\n\r\n".
            var bytesDoEnquadramento = tamanho.ToString("X", CultureInfo.InvariantCulture).Length + 9;
            tamanho -= bytesDoEnquadramento;
        }
        var bytes = Encoding.UTF8.GetBytes(json.PadRight(tamanho));
        using var content = new CorpoDeTeste(bytes, emBlocos);
        using var request = new HttpRequestMessage(metodo, rota)
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Content = content
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(tipo);
        if (emBlocos)
        {
            request.Headers.TransferEncodingChunked = true;
            Assert.Null(request.Content.Headers.ContentLength);
        }
        else
        {
            Assert.Equal(bytesObservados, request.Content.Headers.ContentLength);
            if (esperado == HttpStatusCode.RequestEntityTooLarge)
                request.Headers.ExpectContinue = true;
        }
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await EnviarAsync();
        Assert.True(response.StatusCode == esperado,
            $"Esperado {(int)esperado}, obtido {(int)response.StatusCode} para {rota} com {bytesObservados} bytes observados (em blocos: {emBlocos}).");
        TransporteHttpTests.VerificarCabecalhos(response);
        if (esperado == HttpStatusCode.RequestEntityTooLarge)
        {
            if (!emBlocos)
                Assert.False(content.FoiEscrito);
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            using var erro = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var mensagem = Assert.Single(erro.RootElement.EnumerateObject());
            Assert.Equal("mensagem", mensagem.Name);
            Assert.Equal("O corpo da requisição excede o tamanho permitido.", mensagem.Value.GetString());
        }

        async Task<HttpResponseMessage> EnviarAsync()
        {
            try
            {
                return await client.SendAsync(request);
            }
            catch (HttpRequestException ex)
            {
                throw new HttpRequestException(
                    $"Falha de transporte em {rota} com {bytesObservados} bytes observados (em blocos: {emBlocos}).", ex);
            }
        }
    }

    private sealed class CorpoDeTeste(byte[] bytes, bool emBlocos) : HttpContent
    {
        public bool FoiEscrito { get; private set; }

        protected override bool TryComputeLength(out long length)
        {
            length = emBlocos ? 0 : bytes.Length;
            return !emBlocos;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            FoiEscrito = true;
            return stream.WriteAsync(bytes.AsMemory()).AsTask();
        }
    }
}
