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

Respostas aos oito itens exigidos no documento do desafio.

### 1. Ciclos de vida do Angular utilizados

**`ngOnInit`** em [produtos-pagina.ts:57](frontend/src/app/funcionalidades/produtos/produtos-pagina.ts#L57), [notas-pagina.ts:49](frontend/src/app/funcionalidades/notas/notas-pagina.ts#L49) e [nota-detalhe-pagina.ts:55](frontend/src/app/funcionalidades/notas/nota-detalhe-pagina.ts#L55).

Faz a carga inicial e monta os fluxos reativos. Está aqui, e não no construtor, porque no momento do construtor os `@Input` ainda não foram preenchidos: a tela de detalhe depende do `id` que vem do parâmetro de rota, e lê-lo no construtor traria `undefined`.

**`ngOnDestroy`** nos mesmos três componentes.

Cada um mantém um `Subject` chamado `destruir$`, combinado com `takeUntil` em toda inscrição. Sem isso, a busca continuaria viva depois de o usuário sair da rota, e uma resposta atrasada tentaria escrever em um componente que não existe mais. Em uma tela com busca reativa, esse é um vazamento real, não teórico.

**`ngOnChanges`** em [itens-nota-tabela.ts:39](frontend/src/app/funcionalidades/notas/itens-nota-tabela.ts#L39).

Componente filho que recebe os itens por `@Input`. Quando a lista muda, recalcula os totais e troca as colunas exibidas: nota Aberta mostra a coluna de ações, nota Fechada não. A alternativa seria calcular no template, o que refaria a conta a cada ciclo de detecção de mudanças, mesmo quando nada relacionado aos itens mudou.

### 2. Uso de RxJS

**`debounceTime` + `distinctUntilChanged` + `switchMap`** na busca de produtos ([produtos-pagina.ts](frontend/src/app/funcionalidades/produtos/produtos-pagina.ts)) e no autocomplete do detalhe da nota ([nota-detalhe-pagina.ts](frontend/src/app/funcionalidades/notas/nota-detalhe-pagina.ts)).

Os três resolvem problemas diferentes. `debounceTime(350)` espera o usuário parar de digitar, em vez de disparar uma requisição por tecla. `distinctUntilChanged` ignora quando o texto final é igual ao anterior, como ao digitar e apagar uma letra. `switchMap` cancela a requisição anterior ao começar outra: se o usuário digita "tec" e depois "tecl", o resultado de "tec" não interessa mais e, pior, poderia chegar depois e sobrescrever o resultado correto. É o motivo de ser `switchMap` e não `mergeMap`.

**`catchError`** em [interceptors.ts](frontend/src/app/nucleo/interceptors.ts).

Um único ponto converte qualquer falha em um tipo `ErroApi` normalizado, exibe a mensagem e repassa o erro adiante. Também usado dentro do `forkJoin` e dentro do `switchMap` do autocomplete, e nesses dois casos a **posição importa**: dentro do `switchMap` a falha não encerra o fluxo externo, então a busca continua funcionando depois que o serviço volta. Fora dele, uma única falha mataria a busca para sempre.

**`finalize`** nos indicadores de carga de todas as telas.

Roda tanto em sucesso quanto em erro. Sem ele, uma falha na impressão deixaria o botão travado em "Imprimindo..." para sempre.

**`forkJoin`** na carga do detalhe da nota.

Dispara as chamadas de nota e catálogo em paralelo, então a tela leva o tempo da mais lenta em vez da soma das duas. O `catchError` no ramo do catálogo é essencial: `forkJoin` falha inteiro se qualquer fonte falhar, e sem ele a nota não abriria com o serviço de Estoque fora do ar.

**`takeUntil`** com o `Subject` de destruição, em toda inscrição de componente.

### 3. Outras bibliotecas utilizadas e finalidade

Backend:

| Biblioteca | Para quê |
|---|---|
| `Npgsql.EntityFrameworkCore.PostgreSQL` | Provider do PostgreSQL para o EF Core |
| `Microsoft.EntityFrameworkCore.Design` | Ferramenta de linha de comando para gerar migrations |
| `Microsoft.Extensions.Http.Polly` | Retry, timeout e circuit breaker na chamada entre os serviços |
| `QuestPDF` | Geração do PDF da nota fiscal, licença Community |
| `Serilog.AspNetCore` | Log estruturado em JSON, com correlation ID atravessando os dois serviços |
| `Swashbuckle.AspNetCore` | Swagger nos dois serviços |
| `xUnit` + `FluentAssertions` + `NSubstitute` | Testes e dublês |
| `Testcontainers.PostgreSql` | Sobe um PostgreSQL descartável nos testes de integração |
| `Microsoft.AspNetCore.Mvc.Testing` | Sobe a aplicação em memória nos testes de integração |

Frontend: além do Angular e do Material, apenas `rxjs`, que já vem com o framework.

Nada além disso foi instalado. Uma dependência de validação chegou a entrar no scaffolding inicial e foi **removida** ao perceber que não era usada: a validação vive nas entidades de domínio, e uma biblioteca de validação na borda da API duplicaria a regra em dois lugares.

### 4. Bibliotecas de componentes visuais

**Angular Material 22** (`@angular/material` e `@angular/cdk`), tema Azure/Blue.

Componentes usados: `mat-table` com `mat-paginator`, `mat-form-field` com `mat-input` e `mat-select`, `mat-autocomplete` na inclusão de produtos, `mat-dialog` no cadastro, `mat-snack-bar` nas notificações de erro e sucesso, `mat-progress-bar` e `mat-spinner` nos indicadores de processamento, além de `mat-toolbar`, `mat-icon`, `mat-button` e `mat-tooltip`.

A escolha foi por ser a biblioteca oficial do time do Angular: acompanha as versões do framework sem defasagem e traz acessibilidade e navegação por teclado por padrão.

### 5. Gerenciamento de dependências no Golang

Não aplicável. O desafio permite C# ou Go, e este projeto foi feito em **C# com .NET 8**.

O equivalente aqui é o **NuGet**, com as dependências declaradas em `PackageReference` dentro de cada `.csproj` e as versões travadas no `packages.lock` gerado no restore. A analogia direta com o Go é: `.csproj` cumpre o papel do `go.mod`, e o `dotnet restore` o do `go mod download`.

Uma diferença que vale citar: no .NET a unidade de referência é o projeto, e cada uma das quatro camadas de cada serviço tem o próprio `.csproj`. Isso é o que permite garantir na compilação que o projeto de domínio não tenha nenhuma dependência externa: se alguém tentar usar Entity Framework dentro dele, o build quebra.

### 6. Frameworks utilizados no C#

- **ASP.NET Core 8** com controllers, a base das duas APIs
- **Entity Framework Core 8** como ORM, com migrations e configuração por `IEntityTypeConfiguration`
- **Polly**, via `Microsoft.Extensions.Http.Polly`, para as políticas de resiliência
- **QuestPDF** para a geração do documento
- **Serilog** para log estruturado
- **xUnit** como framework de testes

### 7. Tratamento de erros e exceções no backend

Três camadas, e **nenhum `try/catch` em controller nenhum**.

**Exceções de domínio tipadas.** Toda violação de regra lança uma exceção que herda de `ExcecaoDeDominio` ([Estoque](backend/src/Estoque/Korp.Estoque.Domain/Excecoes/ExcecaoDeDominio.cs), [Faturamento](backend/src/Faturamento/Korp.Faturamento.Domain/Excecoes/ExcecaoDeDominio.cs)), carregando um `Codigo` estável como `SALDO_INSUFICIENTE` ou `NOTA_STATUS_INVALIDO`. O código é o contrato com o frontend: a tela trata por ele, nunca pelo texto da mensagem.

**Manipulador global** implementando `IExceptionHandler` ([Estoque](backend/src/Estoque/Korp.Estoque.Api/Middlewares/ManipuladorGlobalDeExcecoes.cs), [Faturamento](backend/src/Faturamento/Korp.Faturamento.Api/Middlewares/ManipuladorGlobalDeExcecoes.cs)). Traduz cada exceção no status HTTP correto e responde sempre em **ProblemDetails (RFC 7807)**, com o `codigo`, o `traceId` e campos extras quando úteis. Exceção não prevista vira 500 com mensagem genérica: o detalhe interno vai para o log e nunca para o cliente.

A escolha do status carrega informação acionável:

| Status | Código | O que significa para o usuário |
|---|---|---|
| 422 | `SALDO_INSUFICIENTE` | Não adianta repetir, falta estoque. Vem com o detalhamento produto a produto |
| 409 | `CONFLITO_CONCORRENCIA` | Outra nota levou o saldo. Vale tentar de novo |
| 409 | `NOTA_STATUS_INVALIDO` | A nota não está Aberta |
| 503 | `ESTOQUE_INDISPONIVEL` | Problema temporário. Vale tentar de novo |
| 500 | `INTERVENCAO_MANUAL` | Precisa de gente, não adianta insistir |

**Tradução da fronteira de rede** em [ClienteHttpDeEstoque.cs](backend/src/Faturamento/Korp.Faturamento.Infrastructure/Integracao/ClienteHttpDeEstoque.cs). Acima dessa classe ninguém sabe o que é um `HttpResponseMessage`, um 422 ou um circuito aberto. Ela converte `BrokenCircuitException`, `TimeoutRejectedException` e `HttpRequestException` em `EstoqueIndisponivelExcecao`, e a resposta 422 em `SaldoInsuficienteNoEstoqueExcecao`.

Essa distinção entre **recusa de negócio** e **falha técnica** atravessa o sistema inteiro: é ela que decide se o retry do Polly tenta de novo, e se a saga de impressão precisa compensar ou apenas reverter.

### 8. Uso de LINQ

Em dois modos, com propósitos diferentes.

**LINQ traduzido para SQL pelo EF Core**, nos repositórios ([ProdutoRepositorio.cs](backend/src/Estoque/Korp.Estoque.Infrastructure/Persistencia/ProdutoRepositorio.cs), [RepositorioDeNotas.cs](backend/src/Faturamento/Korp.Faturamento.Infrastructure/Persistencia/RepositorioDeNotas.cs)). Filtro, contagem, ordenação e paginação acontecem no banco, nunca em memória:

```csharp
var consulta = _contexto.Produtos.AsNoTracking();

if (!string.IsNullOrWhiteSpace(busca))
    consulta = consulta.Where(p =>
        EF.Functions.ILike(p.Codigo, termo) ||
        EF.Functions.ILike(p.Descricao, termo));

var total = await consulta.CountAsync(ct);

var itens = await consulta
    .OrderBy(p => p.Codigo)
    .Skip((pagina - 1) * tamanho)
    .Take(tamanho)
    .ToListAsync(ct);
```

Dois detalhes deliberados aí. `EF.Functions.ILike` vira o `ILIKE` nativo do PostgreSQL, que é *case insensitive* sem precisar de `ToLower()` dos dois lados, o que impediria o banco de usar índice. E `AsNoTracking()` em listagem, porque é leitura pura e não precisa do custo do *change tracker*.

Também em LINQ traduzido, `ObterPorIdsAsync` usa `Where(p => lista.Contains(p.Id))`, que vira `WHERE id = ANY(...)`. Com uma nota de 20 itens, é uma ida ao banco em vez de vinte.

**LINQ em memória**, sobre coleções já materializadas ou sobre o domínio:

```csharp
// Consolida quantidades antes de tocar no banco. Se a mesma nota citar o
// mesmo produto em duas linhas, o que importa e a soma: sem isso, duas
// baixas parciais passariam pela validacao separadamente e estourariam
// o estoque juntas.
var quantidadesPorProduto = dto.Itens
    .GroupBy(i => i.ProdutoId)
    .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantidade));
```

E em projeções de entidade para DTO (`Select`), em somas de totais da nota (`Sum`) e na busca de item dentro do agregado (`FirstOrDefault`), que roda sobre a coleção já carregada e não gera consulta nova.

---

## Requisitos opcionais

- [x] **Tratamento de concorrência**: lock otimista com token de versão na entidade `Produto`. Duas notas disputando o mesmo saldo resultam em exatamente uma baixa efetivada, e o saldo nunca fica negativo. Coberto por teste de integração com requisições paralelas reais.
- [ ] **Idempotência**: fora de escopo. A implementação seria um header `Idempotency-Key` com tabela de chaves processadas. O tempo foi investido em testes de integração e no tratamento de concorrência.
- [ ] **Inteligência Artificial**: fora de escopo, por não tocar o núcleo do que o desafio avalia.

## Decisões de escopo

Documentadas por escolha, não por esquecimento:

**Comunicação síncrona em vez de mensageria.** RabbitMQ com padrão Outbox foi considerado e deixado de fora. Somaria complexidade e prazo sem atender nenhum requisito que REST com Polly e compensação já não atenda. A recuperação de falha exigida é demonstrável em dois comandos.

**Sem API Gateway.** O Angular fala com os dois serviços diretamente. Um gateway acrescentaria mais um container e mais uma camada de rota sem resolver nada que o desafio peça.

**Sem autenticação.** Não faz parte do escopo descrito.

**PDF gerado sob demanda, nunca armazenado.** Nota fechada é imutável, então o documento sai idêntico toda vez. Guardar o arquivo só acrescentaria volume e risco de divergir do banco.

**Nota fiscal sem cliente e sem valores.** O desafio especifica numeração, status e produtos com quantidades. Acrescentar campos não solicitados seria escopo inventado.

---

## Testes

```bash
cd backend && dotnet test
```

Os de integração sobem um PostgreSQL real em container via Testcontainers, então o Docker precisa estar rodando.

```bash
cd frontend && npm test
```

**159 testes automatizados**, 139 no backend e 20 no frontend.

| Suíte | Testes | O que cobre |
|---|---|---|
| Estoque, domínio | 21 | Regras do agregado Produto: saldo nunca negativo, quantidade válida, baixa e estorno |
| Estoque, aplicação | 25 | Consolidação de produto repetido na nota, unicidade de código, RN09, paginação |
| Estoque, integração | 13 | HTTP e PostgreSQL reais: CRUD, atomicidade da baixa, **e o cenário de concorrência do desafio** |
| Faturamento, domínio | 21 | Máquina de estados da nota e regras de edição |
| Faturamento, aplicação | 46 | Saga de impressão, validação cumulativa de saldo, e a tradução de erro do cliente HTTP |
| Faturamento, integração | 13 | HTTP e PostgreSQL reais: numeração sob concorrência, fluxo completo, PDF, compensação |
| Frontend | 20 | Interceptors de erro e correlação, `ngOnChanges` do componente de itens, shell |

Cinco testes merecem destaque, porque provam requisitos que asserção de código não alcança:

- **`Duas_notas_disputando_a_ultima_unidade_apenas_uma_vence`**: duas requisições paralelas contra um produto com saldo 1. Exatamente uma passa, a outra recebe recusa explícita, e o saldo termina em zero. É o requisito opcional (a) do desafio.
- **`Criacoes_simultaneas_nunca_repetem_numeracao`**: 20 notas criadas em paralelo, todas com número único. Prova que a sequence do banco resolve o que `MAX(numero) + 1` não resolveria.
- **`Quando_ate_o_estorno_falha_a_nota_fica_em_processamento`**: o pior cenário da saga. A nota fica marcada como pendente em vez de voltar a Aberta, porque o saldo saiu e não voltou.
- **`Mesmo_produto_repetido_na_nota_tem_as_quantidades_somadas`**: duas linhas de 3 unidades contra um saldo de 5. Individualmente cabem, somadas não. Sem a consolidação prévia, o estoque terminaria negativo.
- **`AdicionarItem_considera_a_quantidade_ja_presente_na_nota`**: incluir 3 quando já há 4 exige saldo 7, não 3. Sem isso, inclusões sucessivas passariam uma a uma e a nota só estouraria na impressão.

---

## Vídeo de apresentação

_Link a preencher._
