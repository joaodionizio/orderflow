using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RedeAspNet = Microsoft.AspNetCore.HttpOverrides.IPNetwork;

namespace Ecommerce.Api.Middlewares;

internal static class TransporteHttp
{
    private static readonly IPAddress InicioDosIpv4Mapeados = IPAddress.Any.MapToIPv6();

    public static ForwardedHeadersOptions? Configurar(WebApplicationBuilder builder)
    {
        var hosts = builder.Configuration["AllowedHosts"]?.Split(';', StringSplitOptions.TrimEntries);
        if (hosts is not { Length: > 0 } || hosts.Any(host => !EhHostExato(host)))
            throw new InvalidOperationException("AllowedHosts deve listar hosts sem portas ou curingas, separados por ponto e vírgula.");

        // Fornece a lista validada ao Host Filtering automático do ASP.NET Core.
        builder.Services.Configure<HostFilteringOptions>(options =>
        {
            options.AllowedHosts = hosts;
            options.AllowEmptyHosts = false;
            options.IncludeFailureMessage = false;
        });
        builder.Services.Insert(0, ServiceDescriptor.Transient<IStartupFilter, CabecalhosDeSeguranca>());
        builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

        if (string.Equals(builder.Configuration["ForwardedHeaders_Enabled"], "true", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Desative ASPNETCORE_FORWARDEDHEADERS_ENABLED e configure TrustedProxies explicitamente.");

        // Mantém inerte o encaminhamento via DI caso a configuração automática mude antes do startup do host.
        builder.Services.PostConfigure<ForwardedHeadersOptions>(options => options.ForwardedHeaders = ForwardedHeaders.None);
        return ConfigurarProxies(builder.Configuration.GetSection("TrustedProxies"));
    }

    public static void UsarTransporteSeguro(this WebApplication app, ForwardedHeadersOptions? encaminhamento)
    {
        if (encaminhamento is not null)
        {
            app.UseWhen(TemParEncaminhado,
                branch => branch.UseForwardedHeaders(encaminhamento));
        }

        if (app.Environment.IsDevelopment())
        {
            app.UseHttpsRedirection();
            return;
        }

        app.Use(ExigirHttpsAsync);
        app.UseHsts();
    }

    private static bool TemParEncaminhado(HttpContext context)
    {
        // O middleware nativo aceita cabeçalhos sem verificar a origem quando não há IP remoto.
        if (context.Connection.RemoteIpAddress is null)
            return false;

        var enderecos = context.Request.Headers[ForwardedHeadersDefaults.XForwardedForHeaderName];
        var esquemas = context.Request.Headers[ForwardedHeadersDefaults.XForwardedProtoHeaderName];
        return enderecos.Count == 1 && enderecos[0] is { Length: > 0 } endereco &&
            endereco.AsSpan().IndexOfAny(',', '"') < 0 && esquemas.Count == 1 &&
            (string.Equals(esquemas[0], "https", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(esquemas[0], "http", StringComparison.OrdinalIgnoreCase));
    }

    private static bool EhHostExato(string host)
    {
        if (host.IndexOfAny(['*', '+']) >= 0)
            return false;

        return Uri.CheckHostName(host) switch
        {
            UriHostNameType.Dns => !host.Contains(':'),
            UriHostNameType.IPv4 => IPAddress.TryParse(host, out var ipv4) && !ipv4.Equals(IPAddress.Any),
            UriHostNameType.IPv6 => host.StartsWith('[') && host.EndsWith(']') &&
                IPAddress.TryParse(host[1..^1], out var ipv6) && ipv6.ScopeId == 0 && !ipv6.Equals(IPAddress.IPv6Any),
            _ => false
        };
    }

    private static ForwardedHeadersOptions? ConfigurarProxies(IConfigurationSection secao)
    {
        var habilitado = secao["Enabled"];
        if (habilitado is null)
            return null;
        if (!bool.TryParse(habilitado, out var habilitar))
            throw new InvalidOperationException("TrustedProxies:Enabled deve ser true ou false.");
        if (!habilitar)
            return null;

        var enderecos = LerEntradas(secao.GetSection("Addresses")).Select(InterpretarEndereco).ToArray();
        var redes = LerEntradas(secao.GetSection("Networks")).Select(InterpretarRede).ToArray();
        if (enderecos.Length == 0 && redes.Length == 0)
            throw new InvalidOperationException("TrustedProxies requer ao menos um endereço ou uma rede autorizada.");

        var opcoes = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = 1,
            RequireHeaderSymmetry = true
        };
        // Substitui os loopbacks padrão pela lista explícita e validada acima.
        opcoes.KnownProxies.Clear();
        opcoes.KnownNetworks.Clear();
        foreach (var endereco in enderecos)
            opcoes.KnownProxies.Add(endereco);
        foreach (var rede in redes)
            opcoes.KnownNetworks.Add(rede);

        return opcoes;
    }

    private static IEnumerable<IConfigurationSection> LerEntradas(IConfigurationSection lista)
    {
        if (!string.IsNullOrEmpty(lista.Value))
            throw new InvalidOperationException($"{lista.Path} deve ser uma lista de valores.");

        return lista.GetChildren();
    }

    private static IPAddress InterpretarEndereco(IConfigurationSection entrada)
    {
        if (IPAddress.TryParse(entrada.Value?.Trim(), out var ip) && SemEscopo(ip))
        {
            ip = ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;
            if (!ip.Equals(IPAddress.Any) && !ip.Equals(IPAddress.IPv6Any))
                return ip;
        }

        throw new InvalidOperationException($"{entrada.Path} deve conter um endereço IP específico.");
    }

    private static RedeAspNet InterpretarRede(IConfigurationSection entrada)
    {
        if (System.Net.IPNetwork.TryParse(entrada.Value?.Trim(), out var rede) && SemEscopo(rede.BaseAddress))
        {
            var ip = rede.BaseAddress;
            var prefixo = rede.PrefixLength;
            if (ip.IsIPv4MappedToIPv6)
            {
                ip = ip.MapToIPv4();
                prefixo -= 96;
            }
            else if (rede.Contains(InicioDosIpv4Mapeados))
            {
                throw new InvalidOperationException($"{entrada.Path} não pode abranger todo o espaço IPv4 mapeado em IPv6.");
            }

            if (prefixo > 0)
                return new RedeAspNet(ip, prefixo);
        }

        throw new InvalidOperationException($"{entrada.Path} deve conter uma rede CIDR válida com prefixo maior que zero.");
    }

    private static bool SemEscopo(IPAddress ip) => ip.AddressFamily != AddressFamily.InterNetworkV6 || ip.ScopeId == 0;

    private static Task ExigirHttpsAsync(HttpContext context, RequestDelegate next)
    {
        if (context.Request.IsHttps)
            return next(context);

        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        context.Response.Headers.CacheControl = "no-store";
        return Task.CompletedTask;
    }
}
