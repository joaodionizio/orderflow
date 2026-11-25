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

`dotnet run` não carrega o `.env` automaticamente. O ASP.NET Core lê `ConnectionStrings__DefaultConnection` do ambiente do processo e usa a mesma string para o EF Core e o health check. `Jwt__Chave` fornece a chave de assinatura exigida pela autenticação já existente. [Configuração do ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/?view=aspnetcore-8.0)

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

Ao conectar a API a outro SQL Server, forneça a string completa e a chave JWT no ambiente e execute o mesmo comando `dotnet run`. Antes das migrations, a API verifica a sintaxe da conexão e exige um servidor em `Data Source` ou `Server`. Ela também rejeita uma chave ausente, em branco ou com menos de 32 bytes em UTF-8. As mensagens indicam a configuração a corrigir sem incluir seu conteúdo.

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
2. Execute o comando abaixo. Confira o servidor e o banco exibidos. Informe nome, um e-mail de funcionário ainda não cadastrado e uma senha exclusiva de pelo menos 15 caracteres. A senha é solicitada duas vezes com a digitação oculta.

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
dotnet build Ecommerce.sln --no-restore
dotnet test Ecommerce.sln --no-build
```

Os testes de serviço usam EF Core em memória. Para executar também os testes HTTP, de provisionamento e de migration, forneça `ORDERFLOW_TEST_SQLSERVER` com uma string de conexão para uma **instância SQL Server isolada**, com permissão para criar e excluir bancos. A suíte gera nomes exclusivos `OrderFlowTests_<guid>` para seus bancos e os remove ao final. Sem essa variável, os testes de integração são ignorados localmente e falham no GitHub Actions.

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
