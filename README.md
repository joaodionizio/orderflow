# Ecommerce API

API REST em ASP.NET Core para gerenciamento de compradores, produtos e pedidos de um e-commerce.

O projeto usa uma arquitetura simples em camadas, com regras de negócio nos services, persistência com Entity Framework Core e SQL Server via Docker Compose.

## Tecnologias

* .NET 8
* ASP.NET Core Web API
* Entity Framework Core 8
* SQL Server 2022
* Docker Compose
* Swagger
* xUnit
* JWT Bearer

## Estrutura

```text
src
├── Ecommerce.Api
├── Ecommerce.Application
├── Ecommerce.Domain
└── Ecommerce.Infrastructure

tests
├── Ecommerce.Domain.Tests
└── Ecommerce.Tests
```

## Como Rodar

Suba o SQL Server:

```bash
docker compose up -d
```

Restaure as dependências:

```bash
dotnet restore Ecommerce.sln
```

Execute a API:

```bash
dotnet run --project src/Ecommerce.Api
```

As migrations são aplicadas automaticamente na inicialização da aplicação.

Swagger:

```text
http://localhost:5115/swagger
```

Health check:

```http
GET /health
```

## Autenticação

Os endpoints de compradores, produtos e pedidos exigem JWT. Os endpoints de autenticação ficam abertos.

### Registrar Usuário

```http
POST /api/v1/auth/registrar
Content-Type: application/json

{
  "nome": "João",
  "email": "joao@email.com",
  "senha": "123456"
}
```

### Login

```http
POST /api/v1/auth/login
Content-Type: application/json

{
  "email": "joao@email.com",
  "senha": "123456"
}
```

Resposta:

```json
{
  "token": "jwt-gerado",
  "expiraEm": "2026-06-10T15:00:00Z"
}
```

No Swagger, clique em **Authorize** e informe:

```text
Bearer seu-token-jwt
```

## Pedidos

Status disponíveis:

* Iniciado
* Processado
* Enviado
* Cancelado

Fluxo permitido:

```text
Iniciado -> Processado -> Enviado
Iniciado -> Cancelado
Processado -> Cancelado
```

Regras principais:

* Todo pedido deve possuir comprador válido.
* Todo pedido deve possuir pelo menos um item.
* Cada item deve possuir produto válido.
* A quantidade do item deve ser maior que zero.
* Apenas pedidos iniciados podem ser alterados ou excluídos.
* Apenas pedidos processados podem ser enviados.

### Listagem Com Filtros E Paginação

```http
GET /api/v1/pedidos?status=Iniciado&compradorId={guid}&pagina=1&tamanhoPagina=10
Authorization: Bearer seu-token-jwt
```

`pagina` usa valor padrão `1`.

`tamanhoPagina` usa valor padrão `10` e limite máximo `50`.

Resposta:

```json
{
  "items": [],
  "paginaAtual": 1,
  "tamanhoPagina": 10,
  "totalItens": 0,
  "totalPaginas": 0
}
```

## Endpoints

### Auth

| Método | Endpoint                  |
| ------ | ------------------------- |
| POST   | /api/v1/auth/registrar    |
| POST   | /api/v1/auth/login        |

### Compradores

| Método | Endpoint                 |
| ------ | ------------------------ |
| POST   | /api/v1/compradores      |
| GET    | /api/v1/compradores      |
| GET    | /api/v1/compradores/{id} |

### Produtos

| Método | Endpoint              |
| ------ | --------------------- |
| POST   | /api/v1/produtos      |
| GET    | /api/v1/produtos      |
| GET    | /api/v1/produtos/{id} |

### Pedidos

| Método | Endpoint                       |
| ------ | ------------------------------ |
| POST   | /api/v1/pedidos                |
| GET    | /api/v1/pedidos                |
| GET    | /api/v1/pedidos/{id}           |
| PUT    | /api/v1/pedidos/{id}           |
| DELETE | /api/v1/pedidos/{id}           |
| PATCH  | /api/v1/pedidos/{id}/processar |
| PATCH  | /api/v1/pedidos/{id}/enviar    |
| PATCH  | /api/v1/pedidos/{id}/cancelar  |

## Testes

Execute todos os testes:

```bash
dotnet test
```

Há testes unitários para:

* Regras de domínio do pedido.
* Regras principais de `PedidoService`.
* Regras principais de `ProdutoService`.
* Regras principais de `CompradorService`.

Os testes dos services usam banco em memória, sem depender do SQL Server real.

## Tratamento De Erros

A aplicação possui middleware global de exceções.

Retornos principais:

| Situação                  | Status |
| ------------------------- | ------ |
| Entidade não encontrada   | 404    |
| Regra de negócio inválida | 400    |
| Erro inesperado           | 500    |

## Diferenciais Implementados

* Paginação na listagem de pedidos.
* Testes unitários.
* Autenticação JWT.
* Docker Compose com SQL Server.
* Swagger com suporte a Bearer Token.
* Health Check com verificação do SQL Server.
* Middleware global de exceções.
* Migrations automáticas na inicialização.
* Índice único para e-mail de comprador e usuário.
* Retry automático para falhas transitórias do SQL Server.

## Observações Técnicas

* A API usa `Guid` como identificador das entidades.
* As datas são preenchidas com `DateTime.UtcNow`.
* Senhas são armazenadas com hash PBKDF2, não em texto puro.
* Os services acessam o `ApplicationDbContext` diretamente para manter o projeto simples.
