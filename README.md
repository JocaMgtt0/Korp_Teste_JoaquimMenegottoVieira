# Korp_Teste_JoaquimMenegottoVieira

[![CI](https://github.com/JocaMgtt0/Korp_Teste_JoaquimMenegottoVieira/actions/workflows/ci.yml/badge.svg)](https://github.com/JocaMgtt0/Korp_Teste_JoaquimMenegottoVieira/actions/workflows/ci.yml)

Sistema de emissão de Notas Fiscais em arquitetura de microsserviços.
Teste prático Korp, desenvolvido por **Joaquim Menegotto Vieira**.

> Especificação completa de requisitos, regras de negócio e decisões de arquitetura: [ESPECIFICACAO.md](ESPECIFICACAO.md)

---

## Stack

| Camada | Tecnologia |
|---|---|
| Frontend | Angular 22, standalone components, signals, Angular Material |
| Backend | .NET 8, ASP.NET Core |
| Banco | PostgreSQL 16, uma instância por serviço |
| ORM | Entity Framework Core |
| Resiliência | Polly (retry, timeout, circuit breaker) |
| PDF | QuestPDF |
| Testes | xUnit, FluentAssertions, NSubstitute, Testcontainers |
| Orquestração | Docker Compose |

---

## Como executar

```bash
docker compose up --build
```

| Serviço | URL |
|---|---|
| Frontend | http://localhost:4200 |
| API de Faturamento (Swagger) | http://localhost:5002/swagger |
| API de Estoque (Swagger) | http://localhost:5001/swagger |

As migrations são aplicadas automaticamente na subida e o banco já vem com dados de seed.

---

## Arquitetura

```
                    +---------------------------+
                    |   Angular (porta 4200)    |
                    +-------------+-------------+
                                  |
                +-----------------+-----------------+
                |                                   |
      +---------v----------+            +-----------v---------+
      | Faturamento :5002  |  REST +    |   Estoque :5001     |
      | notas fiscais      |  Polly     |   produtos e saldo  |
      +---------+----------+ ---------> +-----------+---------+
                |                                   |
      +---------v----------+            +-----------v---------+
      | faturamento_db     |            |   estoque_db        |
      +--------------------+            +---------------------+
```

Dois microsserviços, dois bancos, nenhuma tabela compartilhada e nenhuma chave estrangeira atravessando os bancos. O serviço de Estoque é o dono exclusivo do saldo dos produtos: o Faturamento nunca lê nem escreve a tabela de produtos, sempre chama a API.

Cada serviço segue Clean Architecture em quatro projetos, com as dependências apontando para dentro:

```
Domain          entidades e regras de negócio, sem dependência externa
Application     casos de uso, interfaces de repositório, DTOs
Infrastructure  EF Core, repositórios, HttpClient, Polly, QuestPDF
Api             controllers, injeção de dependência, middleware, Swagger
```

---

## Cenário de falha e recuperação

O requisito obrigatório de tratamento de falhas é demonstrado assim:

```bash
docker compose stop estoque
```

Ao tentar imprimir uma nota com o serviço de Estoque fora do ar, o Faturamento esgota as tentativas do Polly, abre o circuit breaker, reverte a nota para o status `Aberta` e devolve `503 ESTOQUE_INDISPONIVEL`. O Angular exibe mensagem específica ao usuário. Nenhum dado fica inconsistente.

```bash
docker compose start estoque
```

Com o serviço de volta, a mesma nota imprime normalmente.

---

## Detalhamento técnico

Respostas aos itens exigidos no documento do desafio.

### 1. Ciclos de vida do Angular utilizados

_A preencher ao final da implementação: apontar arquivo e componente de cada hook._

- `ngOnInit`:
- `ngOnDestroy`:
- `ngOnChanges`:

### 2. Uso de RxJS

_A preencher: operador, onde é usado e qual problema resolve._

- `debounceTime` + `distinctUntilChanged` + `switchMap`:
- `catchError`:
- `finalize`:
- `forkJoin`:

### 3. Outras bibliotecas utilizadas e finalidade

_A preencher: uma linha por dependência, com justificativa._

### 4. Bibliotecas de componentes visuais

_A preencher._

### 5. Gerenciamento de dependências no Golang

Não aplicável. O backend deste projeto foi implementado em C# com .NET 8, opção permitida pelo desafio. O gerenciamento de dependências é feito pelo NuGet, declarado nos arquivos `.csproj` de cada projeto.

### 6. Frameworks utilizados no C#

_A preencher: ASP.NET Core, EF Core, Polly, QuestPDF, FluentValidation, Serilog, xUnit._

### 7. Tratamento de erros e exceções no backend

_A preencher: exceções de domínio tipadas, middleware global, ProblemDetails, códigos de erro._

### 8. Uso de LINQ

_A preencher: onde LINQ é usado em queries traduzidas para SQL pelo EF Core e onde é usado em memória sobre coleções do domínio._

---

## Requisitos opcionais implementados

- [x] **Tratamento de concorrência**: lock otimista com token de versão na entidade Produto. Duas notas disputando o mesmo saldo resultam em exatamente uma baixa efetivada.
- [ ] Idempotência: fora de escopo, decisão documentada na especificação.
- [ ] Inteligência Artificial: fora de escopo, decisão documentada na especificação.

---

## Testes

```bash
dotnet test
```

---

## Vídeo de apresentação

_Link a preencher._
