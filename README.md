# OrderFlow

API REST corporativa interna para gerenciamento de compradores, produtos e pedidos, com foco em regras de negócio, organização em camadas e confiabilidade da aplicação. Contas representam funcionários. Compradores são entidades de negócio independentes.

> O projeto nasceu como um desafio técnico e foi evoluído para um case de portfólio voltado a backend e engenharia de software.

## O que este projeto demonstra

- Modelagem de domínio e regras de negócio para ciclo de vida de pedidos
- API REST com ASP.NET Core
- Persistência com Entity Framework Core e SQL Server
- Paginação e filtros
- Tratamento global de exceções
- Health checks e resiliência de conexão
- Testes unitários do domínio
- CI com build e testes automatizados
- Ambiente local reproduzível com Docker Compose

## Tecnologias

- .NET 8
- ASP.NET Core Web API
- Entity Framework Core 8
- SQL Server 2022
- Docker Compose
- Swagger / OpenAPI
- xUnit
- GitHub Actions

## Arquitetura

```mermaid
flowchart LR
    Client[Cliente / Swagger] --> API[Ecommerce.Api]
    API --> APP[Ecommerce.Application]
    APP --> DOMAIN[Ecommerce.Domain]
    API --> INFRA[Ecommerce.Infrastructure]
    INFRA --> DOMAIN
    INFRA --> DB[(SQL Server)]
```

```text
src
├── Ecommerce.Api
├── Ecommerce.Application
├── Ecommerce.Domain
└── Ecommerce.Infrastructure

tests
└── Ecommerce.Domain.Tests
```

### Ecommerce.Api

Endpoints HTTP, configuração da aplicação, Swagger, health check e middleware global de exceções.

### Ecommerce.Application

DTOs, contratos de serviços e exceções da aplicação.

### Ecommerce.Domain

Entidades, enums e regras do domínio.

### Ecommerce.Infrastructure

Entity Framework Core, DbContext, migrations e implementações dos serviços.

## Domínio de pedidos

O pedido percorre um fluxo de estados controlado por regras de negócio:

```text
Iniciado -> Processado -> Enviado
    |           |
    +------> Cancelado
```

Regras implementadas:

- Todo pedido deve possuir um comprador válido.
- Todo pedido deve possuir pelo menos um item.
- Cada item deve possuir quantidade maior que zero.
- Cada item deve referenciar um produto existente.
- Apenas pedidos iniciados podem ser alterados.
- Apenas pedidos iniciados podem ser processados.
- Apenas pedidos processados podem ser enviados.
- Apenas pedidos iniciados ou processados podem ser cancelados.
- Apenas pedidos iniciados podem ser excluídos.

## Recursos da API

### Autenticação e perfis

| Ação | Anônimo | Sem perfil | Operator | Admin |
| --- | --- | --- | --- | --- |
| `POST /api/v1/auth/login` com credenciais válidas | Sim | Sim | Sim | Sim |
| `POST /api/v1/auth/registrar` | 401 | 403 | 403 | 204 |
| Operações de compradores, produtos e pedidos | 401 | 403 | Sim | Sim |
| `GET /health` | Sim | Sim | Sim | Sim |

O cadastro recebe `nome`, `email` e `senha`. O servidor provisiona sempre um `Operator` e rejeita campos extras, incluindo `role` e `perfil`. O login retorna o token JWT em `token` e a validade em `expiraEm`. Use `Authorization: Bearer <token>` nas rotas protegidas. No Swagger em desenvolvimento, use **Authorize** para informar o JWT e consulte os perfis exigidos por operação. Tokens ausentes ou inválidos recebem `401` nas rotas protegidas. Um token válido sem o perfil exigido recebe `403`.

### Segurança dos tokens JWT

| Configuração | Requisito |
| --- | --- |
| `Jwt__Issuer` | Emissor obrigatório, com valor não vazio. `appsettings.json` fornece `Ecommerce.Api`. |
| `Jwt__Audience` | Público obrigatório, com valor não vazio. `appsettings.json` fornece `Ecommerce.Api`. |
| `Jwt__Chave` | Chave de assinatura obrigatória, com pelo menos 32 bytes em UTF-8. Forneça pelo ambiente ou por um gerenciador de segredos. |
| `Jwt__ExpiraMinutos` | Inteiro de 1 a 60. O padrão de 60 minutos só vale se a configuração estiver ausente. Um valor vazio é inválido. |

A API valida essas configurações antes das migrations. Aceita exclusivamente `HS256` com assinatura válida. Compara `iss` e `aud` por igualdade ordinal, incluindo capitalização e barras finais. O público deve ser um único valor, como string ou array de um elemento. `exp` é obrigatório e indica o instante a partir do qual o token expira. Quando presente, `nbf` indica o primeiro instante de validade. Ambos usam segundos inteiros desde a época Unix, com tolerância de relógio zero. Falhas de autenticação em rotas protegidas recebem `401`, `WWW-Authenticate: Bearer` e corpo vazio. [RFC 7519](https://www.rfc-editor.org/rfc/rfc7519.html#section-4.1) · [Opções do JWT Bearer no ASP.NET Core](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.authentication.jwtbearer.jwtbeareroptions?view=aspnetcore-8.0)

O `sub` deve ser uma única string com o GUID persistido do funcionário no formato `D`, diferente de `Guid.Empty`. Claims de identidade ambíguas recebem `401`. A API mantém os nomes originais das claims, com `sub` como identidade e `role` como perfil. `Operacao` exige uma única claim `role` escalar com `Admin` ou `Operator`. `Provisionamento` exige uma única claim `role` escalar com `Admin`. Perfil ausente, múltiplo, desconhecido ou apresentado por alias resulta em `403` nas rotas com essas policies quando o token é válido. Os novos tokens incluem `sub`, `iss`, `aud`, `exp` e, quando aplicável, `role`. Nome e e-mail ficam fora das novas emissões. Tokens anteriores emitidos pela API com essas claims continuam válidos enquanto atendem às regras de validação. Swagger e os fluxos HTTP do repositório usam o token como valor opaco. Integrações que decodificam o JWT precisam usar `sub` para identificar a conta.

`Jwt__ExpiraMinutos` limita a duração na emissão a 60 minutos. Na validação, a API verifica o `exp` assinado, sem recalcular a duração original nem aplicar retroativamente o limite de emissão. Tokens legados com uma duração maior podem permanecer válidos até seu próprio `exp`.

O perfil é registrado no token no momento do login. Após uma mudança no banco, o próximo login reflete o novo perfil. Um token já emitido mantém as permissões anteriores até expirar ou até a troca da chave de assinatura. Um token copiado também pode ser reutilizado nesse intervalo. A API atual não tem revogação individual nem consulta o perfil no banco a cada requisição.

Para rotacionar a chave, gere um segredo independente e coordene a troca em todas as instâncias, retirando de serviço as que usam a chave anterior antes de liberar tráfego para as atualizadas. Cada instância carrega uma única chave na inicialização. Após a troca, tokens assinados com a chave anterior recebem `401` e os funcionários precisam fazer login novamente. [RFC 8725](https://www.rfc-editor.org/rfc/rfc8725.html#section-3.5)

### Segurança das senhas

O cadastro de operadores e o provisionamento de administradores usam a mesma política. Novas senhas têm de 15 a 128 pontos de código Unicode e aceitam espaços e frases-senha, sem exigir combinações de tipos de caracteres. Um caractere suplementar conta como um ponto de código. Uma letra e um acento combinante separado contam como dois. Sequências Unicode inválidas são recusadas. A verificação e o hashing usam a senha inteira, incluindo espaços iniciais e finais, sem truncamento nem normalização Unicode. [NIST SP 800-63B-4](https://pages.nist.gov/800-63-4/sp800-63b.html#passwordver)

O bloqueio local compara a senha inteira, ignorando maiúsculas e minúsculas nessa comparação. Ele cobre uma lista curta de frases previsíveis e alguns padrões de teclado e repetição. A cobertura é parcial e não detecta senhas comprometidas fora da lista e dos padrões.

O login aceita senhas legadas abaixo de 15 e acima de 128 caracteres, até o teto de 1.024 pontos de código. O fluxo anterior não tinha teto de comprimento e o hash armazenado não revela o comprimento original. Por isso, senhas legadas acima de 1.024 caracteres passam a ser recusadas e exigem tratamento administrativo antes de o usuário voltar a acessar a conta.

O armazenamento continua no formato `iteracoes.salt.hash`, com PBKDF2-HMAC-SHA256, salt aleatório de 16 bytes e saída de 32 bytes. Novos hashes usam 600.000 iterações. A verificação aceita de 100.000 a 1.200.000 iterações e valida a estrutura, o Base64 e os tamanhos antes da derivação. A comparação dos bytes usa `FixedTimeEquals`. Registros inválidos resultam na mesma falha de autenticação de uma senha incorreta e geram um aviso sem identificadores de usuário, senhas ou hashes. [OWASP Password Storage Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html#pbkdf2)

Após um login correto, hashes abaixo de 600.000 iterações são atualizados com um salt novo. A atualização modifica apenas `SenhaHash` e só grava se o hash anterior ainda corresponder exatamente ao lido. Quando outra operação vence a disputa, o login verifica a senha novamente com o registro atual. Hashes válidos com custo superior são preservados. A atualização dispensa migration e ocorre à medida que os usuários autenticam.

#### Medição local do PBKDF2

Medição em Release no macOS 27.2.1, Apple M4 (Arm64), .NET 8.0.27 (SDK 8.0.421). Um processo nativo executou 20 amostras sequenciais por custo, em ordem alternada e após três aquecimentos por custo. A medição incluiu apenas a chamada `Rfc2898DeriveBytes.Pbkdf2`, com entrada sintética ASCII de 32 bytes, salt de 16 bytes e saída de 32 bytes.

| Iterações | Mediana | p95 |
| --- | --- | --- |
| 100.000 | 13,85 ms | 14,19 ms |
| 600.000 | 83,39 ms | 84,93 ms |

O custo mediano da derivação foi 6,02 vezes maior. O primeiro login que atualiza um hash de 100.000 iterações executa as duas derivações. Os números não incluem HTTP, banco ou concorrência e não são usados como limites de latência no CI.

### Compradores

| Método | Endpoint |
| --- | --- |
| POST | `/api/v1/compradores` |
| GET | `/api/v1/compradores` |
| GET | `/api/v1/compradores/{id}` |

### Produtos

| Método | Endpoint |
| --- | --- |
| POST | `/api/v1/produtos` |
| GET | `/api/v1/produtos` |
| GET | `/api/v1/produtos/{id}` |

### Pedidos

| Método | Endpoint |
| --- | --- |
| POST | `/api/v1/pedidos` |
| GET | `/api/v1/pedidos` |
| GET | `/api/v1/pedidos/{id}` |
| PUT | `/api/v1/pedidos/{id}` |
| DELETE | `/api/v1/pedidos/{id}` |
| PATCH | `/api/v1/pedidos/{id}/processar` |
| PATCH | `/api/v1/pedidos/{id}/enviar` |
| PATCH | `/api/v1/pedidos/{id}/cancelar` |

### Paginação e filtros

A listagem de pedidos aceita paginação e filtros por status e comprador.

```http
GET /api/v1/pedidos?pagina=1&tamanhoPagina=10&status=Iniciado
```

```http
GET /api/v1/pedidos?pagina=1&tamanhoPagina=10&compradorId={guid}
```

## Tratamento de erros

A aplicação usa middleware global para padronizar falhas da API. Erros inesperados são registrados com método HTTP, caminho da requisição e `TraceId`.

| Situação | Status |
| --- | ---: |
| Entidade não encontrada | 404 |
| Regra de negócio inválida | 400 |
| Erro inesperado | 500 |

## Health Check

```http
GET /health
```

O endpoint verifica a conexão com o mesmo SQL Server usado pelo EF Core. Quando a conexão está saudável, retorna HTTP 200 com o seguinte corpo.

```json
{
  "status": "Healthy",
  "checks": [{ "name": "sqlserver", "status": "Healthy" }]
}
```

## Como executar

Use o SDK .NET 8 compatível com `global.json`, o Docker com Compose v2 ou superior e o OpenSSL. Execute os comandos abaixo na raiz do repositório, em Bash ou Zsh.

### 1. Configurar e subir o SQL Server

Para um banco local novo, gere uma senha exclusiva e grave-a em um `.env` com permissões restritas. O comando protege um `.env` que já exista. O [`.env.example`](.env.example) descreve a variável esperada.

```bash
(
  set -eC
  umask 077
  sql_password="$(openssl rand -hex 24)"
  printf 'MSSQL_SA_PASSWORD=Aa1-%s\n' "$sql_password" > .env
)
```

Para usar uma senha própria, preencha `MSSQL_SA_PASSWORD` no `.env`. Para usar os comandos abaixo, mantenha o formato `VAR=valor` em uma única linha, sem aspas nem comentários na mesma linha. Use uma senha longa com maiúsculas, minúsculas, dígitos e hífens. Em uma instalação com dados existentes, siga a seção de rotação de credenciais antes de trocar a senha no arquivo.

O Compose lê o `.env` da raiz para preencher a variável do contêiner. Uma variável homônima exportada no terminal tem precedência. Os comandos abaixo usam o valor do arquivo e validam a configuração sem imprimir a senha. [Interpolação no Docker Compose](https://docs.docker.com/compose/how-tos/environment-variables/variable-interpolation/)

```bash
unset MSSQL_SA_PASSWORD
docker compose config --quiet
docker compose up -d sqlserver
docker compose ps sqlserver
```

O banco fica acessível no host em `127.0.0.1:1433` e persiste os dados no volume `sqlserver_data`. Aguarde a mensagem de que o SQL Server está pronto para conexões antes de iniciar a API.

```bash
docker compose logs --tail=50 sqlserver
```

#### macOS com Apple Silicon

O Compose mantém `platform: linux/amd64` para a imagem do SQL Server 2022. No Docker Desktop, a opção **Use Rosetta for x86_64/amd64 emulation on Apple Silicon** está disponível com o **Apple Virtualization framework**. A Microsoft oferece suporte aos contêineres do SQL Server em Linux x86-64 e não oferece suporte à execução por emulação. Se o banco local falhar sob emulação, execute a API normalmente no Mac e configure `ConnectionStrings__DefaultConnection` com o endereço e a credencial de um SQL Server em Linux x86-64. Em um servidor remoto, use um certificado confiável e mantenha a validação habilitada. [Configurações do Docker Desktop](https://docs.docker.com/desktop/settings-and-maintenance/settings/) · [Plataformas suportadas pelo SQL Server](https://learn.microsoft.com/en-us/sql/linux/install-upgrade/setup?view=sql-server-ver16)

### 2. Restaurar dependências e executar a API no host

`dotnet run` não carrega o `.env` automaticamente. O ASP.NET Core lê `ConnectionStrings__DefaultConnection` do ambiente do processo e usa a mesma string para o EF Core e o health check. `Jwt__Chave` fornece a chave de assinatura. O exemplo usa o emissor e o público de `appsettings.json` e o padrão de 60 minutos na ausência de `Jwt__ExpiraMinutos`. [Configuração do ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/?view=aspnetcore-8.0)

```bash
dotnet restore Ecommerce.sln
(
  set -e
  sql_password="$(sed -n 's/^MSSQL_SA_PASSWORD=//p' .env)"
  if [ -z "$sql_password" ]; then
    printf '%s\n' 'Preencha MSSQL_SA_PASSWORD no .env.' >&2
    exit 1
  fi
  Jwt__Chave="${Jwt__Chave:-$(openssl rand -hex 32)}"
  export Jwt__Chave
  export ConnectionStrings__DefaultConnection="Server=127.0.0.1,1433;Database=EcommerceDb;User ID=sa;Password=${sql_password};Encrypt=True;TrustServerCertificate=True"
  dotnet run --project src/Ecommerce.Api --launch-profile http
)
```

O exemplo usa `sa` e `TrustServerCertificate=True` para o contêiner local com certificado autossinado. Fora desse ambiente, forneça as duas variáveis pelo gerenciador de segredos, use um login apropriado para a API e configure a validação do certificado.

Para HS256, gere a chave a partir de pelo menos 32 bytes de aleatoriedade criptográfica, como faz `openssl rand -hex 32` no exemplo. A API mede o comprimento da chave em bytes UTF-8. Caracteres multibyte ocupam mais de um byte, e o comprimento por si só não indica a entropia da chave. Emissão e validação usam a mesma chave carregada no startup. A chave gerada pelo exemplo muda a cada execução se nenhuma chave for fornecida, o que invalida os tokens anteriores. Para manter os tokens entre reinícios, forneça a mesma chave de desenvolvimento por um gerenciador de segredos. [Requisitos do HS256](https://datatracker.ietf.org/doc/html/rfc7518#section-3.2) · [Entropia da chave](https://datatracker.ietf.org/doc/html/rfc8725#section-3.5)

Ao conectar a API a outro SQL Server, forneça a string completa e a chave JWT no ambiente. Antes das migrations, a API verifica a sintaxe da conexão, exige um servidor em `Data Source` ou `Server` e valida as configurações JWT. As mensagens indicam a configuração a corrigir sem incluir seu conteúdo.

### 3. Verificar a aplicação

Abra o Swagger em <http://localhost:5115/swagger>. Em outro terminal, consulte o health check.

```bash
curl --fail --show-error http://localhost:5115/health
```

### Rotação de credenciais anteriormente versionadas

- Trate a senha do SQL Server e a chave JWT que foram versionadas como expostas. Substitua-as em todos os ambientes onde foram usadas ou reutilizadas. A troca da chave JWT invalida os tokens assinados com a chave anterior.
- Em um volume existente, a senha efetiva de `sa` está gravada no SQL Server. Alterar `MSSQL_SA_PASSWORD` no `.env` por si só não troca essa senha. Conecte-se com a credencial administrativa atual e faça a rotação com `ALTER LOGIN` em uma sessão privada. Depois, atualize o `.env` e a string de conexão da API e execute novamente `docker compose up -d sqlserver` com o mesmo projeto Compose. Mantenha o volume existente. [Volumes e credenciais de contêineres SQL Server](https://learn.microsoft.com/en-us/sql/linux/sql-server-linux-docker-container-troubleshooting?view=sql-server-ver16)
- A remoção dos valores dos arquivos atuais não os apaga do histórico Git, de clones ou de forks. Trate a eventual limpeza do histórico como uma operação coordenada, após a rotação. O Git já ignora `.env` e variantes locais como `.env.local`, com exceção do modelo `.env.example`.

### Provisionar o primeiro administrador

1. Use um terminal privado com a configuração do ambiente de destino. Aplique as migrations antes do provisionamento. O startup normal da API já as aplica e pode ser executado em rede privada para preparar o banco.
2. Execute o comando abaixo. Confira o servidor e o banco exibidos. Informe nome, um e-mail de funcionário ainda não cadastrado e uma senha exclusiva de 15 a 128 caracteres. A senha é solicitada duas vezes com a digitação oculta.

   ```bash
   dotnet run --no-launch-profile --project src/Ecommerce.Api -- provision-admin
   ```

3. Faça login com a nova conta e use seu token para provisionar operadores. O comando encerra após a tentativa, sem servir HTTP. Ele exige um terminal interativo, recebe os dados por prompt e bloqueia a criação se já existir um administrador. Uma execução com e-mail já cadastrado falha sem alterar aquela conta.

### Contas e tokens anteriores à atualização

A migration `AdicionarPerfilAosUsuarios` preserva IDs, dados cadastrais e hashes. Como o cadastro anterior era público, todas as contas já existentes ficam com `Perfil = NULL` e podem autenticar, mas recebem JWT sem role e não podem operar nem provisionar. Um responsável pelo banco deve conferir a identidade do funcionário e atribuir `Operator` somente ao registro aprovado, pelo `Id` na tabela `dbo.Usuarios` e apenas enquanto `Perfil IS NULL`. Novos cadastros seguem pelo endpoint do administrador. Após a atribuição, o funcionário precisa fazer login novamente.

Em uma atualização de ambiente, retire de serviço as instâncias da versão antiga antes de liberar a nova, para encerrar o cadastro público em todas as instâncias. Tokens anteriores sem `role` recebem `403` nas rotas protegidas enquanto forem válidos. Tokens expirados recebem `401`. Antes de expor a nova versão a funcionários, conclua a rotação de qualquer chave de assinatura anteriormente exposta que ainda esteja em uso.

## Testes

```bash
dotnet restore Ecommerce.sln
dotnet build Ecommerce.sln --configuration Release --no-restore
dotnet test Ecommerce.sln --configuration Release --no-build
```

Os testes unitários de serviços usam EF Core em memória. Para executar também os testes HTTP, de provisionamento, de migration e de atualização concorrente de hashes, forneça `ORDERFLOW_TEST_SQLSERVER` com uma string de conexão para uma **instância SQL Server isolada**, com permissão para criar e excluir bancos. A suíte gera nomes exclusivos `OrderFlowTests_<guid>` para seus bancos e os remove ao final. Sem essa variável, os testes de integração são ignorados localmente e falham no GitHub Actions.

## Integração contínua

O workflow em `.github/workflows/ci.yml` executa restore, build em Release e testes a cada pull request ou push para `main`. O runner sobe um SQL Server descartável com senha gerada para os testes de integração.

## Decisões técnicas

- `Guid` é utilizado como identificador das entidades.
- Datas são persistidas em UTC.
- O preço unitário é copiado para o item do pedido no momento da criação, preservando o valor histórico da compra.
- Consultas de leitura usam `AsNoTracking()` quando apropriado.
- A conexão com SQL Server usa `EnableRetryOnFailure()` para falhas transitórias.
- E-mail de comprador possui índice único no banco.

## Roadmap

- [x] Autenticação JWT e autorização por perfil
- [x] Testes de integração para autorização, provisionamento e migration de perfis
- [x] Pipeline de CI com build e testes automatizados
- [ ] Deploy público da API e banco
- [ ] Melhorar separação entre camada de aplicação e infraestrutura
- [ ] Observabilidade estruturada e métricas

## Autor

Desenvolvido por **João Luís Dionízio dos Santos**.
