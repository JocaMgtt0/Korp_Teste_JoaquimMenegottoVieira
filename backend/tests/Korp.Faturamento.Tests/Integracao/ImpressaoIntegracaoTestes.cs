using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Korp.Faturamento.Application.Dtos;
using Korp.Faturamento.Application.Excecoes;
using Xunit;

namespace Korp.Faturamento.Tests.Integracao;

/// <summary>
/// Saga de impressao exercitada de ponta a ponta: HTTP de entrada,
/// PostgreSQL real de saida, servico de Estoque simulado.
///
/// Cada teste verifica o desfecho no BANCO, relendo a nota por HTTP depois da
/// operacao. Conferir apenas o codigo de resposta provaria pouco: o que
/// importa e que o estado persistido esteja correto, porque e nele que o
/// usuario vai esbarrar na proxima tentativa.
/// </summary>
public class ImpressaoIntegracaoTestes : IClassFixture<FabricaDeApiDeFaturamento>
{
    private readonly FabricaDeApiDeFaturamento _fabrica;
    private readonly HttpClient _cliente;

    public ImpressaoIntegracaoTestes(FabricaDeApiDeFaturamento fabrica)
    {
        _fabrica = fabrica;
        _cliente = fabrica.CreateClient();

        _fabrica.Estoque.Reiniciar();
        _fabrica.GeradorDePdf.Falha = null;
    }

    private async Task<NotaFiscalDto> CriarNotaComItemAsync(int quantidade = 2)
    {
        var produtoId = Guid.NewGuid();

        _fabrica.Estoque.Catalogo.Add(
            new SaldoProdutoDto(produtoId, $"PRD-{produtoId.ToString("N")[..6]}", "Produto de teste", 100));

        var nota = await (await _cliente.PostAsync("/api/notas", null))
            .Content.ReadFromJsonAsync<NotaFiscalDto>();

        var resposta = await _cliente.PostAsJsonAsync(
            $"/api/notas/{nota!.Id}/itens", new AdicionarItemDto(produtoId, quantidade));

        return (await resposta.Content.ReadFromJsonAsync<NotaFiscalDto>())!;
    }

    private async Task<NotaFiscalDto> RelerAsync(Guid id) =>
        (await _cliente.GetFromJsonAsync<NotaFiscalDto>($"/api/notas/{id}"))!;

    // ---------- Numeracao sequencial ----------

    [Fact]
    public async Task Nota_nasce_aberta_com_numero_da_sequence()
    {
        var resposta = await _cliente.PostAsync("/api/notas", null);
        resposta.StatusCode.Should().Be(HttpStatusCode.Created);

        var nota = await resposta.Content.ReadFromJsonAsync<NotaFiscalDto>();

        nota!.Status.Should().Be("Aberta");
        nota.Numero.Should().BeGreaterThan(0);
        nota.Itens.Should().BeEmpty();
    }

    [Fact]
    public async Task Criacoes_simultaneas_nunca_repetem_numeracao()
    {
        const int simultaneas = 20;

        var tarefas = Enumerable.Range(0, simultaneas)
            .Select(_ => _fabrica.CreateClient().PostAsync("/api/notas", null));

        var respostas = await Task.WhenAll(tarefas);

        var numeros = new List<long>();
        foreach (var r in respostas)
        {
            r.StatusCode.Should().Be(HttpStatusCode.Created);
            numeros.Add((await r.Content.ReadFromJsonAsync<NotaFiscalDto>())!.Numero);
        }

        // A prova da RN04. Com MAX(numero) + 1 no lugar da sequence, varias
        // dessas requisicoes leriam o mesmo maximo e tentariam gravar o mesmo
        // numero: o teste falharia aqui ou no indice unico do banco.
        numeros.Should().OnlyHaveUniqueItems();
        numeros.Should().HaveCount(simultaneas);
    }

    // ---------- Caminho feliz ----------

    [Fact]
    public async Task Impressao_bem_sucedida_fecha_a_nota_e_baixa_o_estoque()
    {
        var nota = await CriarNotaComItemAsync(quantidade: 3);

        var resposta = await _cliente.PostAsync($"/api/notas/{nota.Id}/imprimir", null);

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);

        var persistida = await RelerAsync(nota.Id);
        persistida.Status.Should().Be("Fechada");
        persistida.FechadaEm.Should().NotBeNull();

        _fabrica.Estoque.Baixas.Should().ContainSingle();
        _fabrica.Estoque.Baixas[0].Itens.Single().Quantidade.Should().Be(3);
        _fabrica.Estoque.Estornos.Should().BeEmpty();
    }

    [Fact]
    public async Task Pdf_de_nota_fechada_e_um_arquivo_valido()
    {
        var nota = await CriarNotaComItemAsync();
        await _cliente.PostAsync($"/api/notas/{nota.Id}/imprimir", null);

        var resposta = await _cliente.GetAsync($"/api/notas/{nota.Id}/pdf");

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        resposta.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");

        var bytes = await resposta.Content.ReadAsByteArrayAsync();

        // Assinatura de arquivo PDF. Prova que o QuestPDF gerou um documento
        // de verdade, e nao um array vazio ou uma mensagem de erro.
        bytes.Should().HaveCountGreaterThan(1000);
        System.Text.Encoding.ASCII.GetString(bytes[..4]).Should().Be("%PDF");
    }

    [Fact]
    public async Task Nota_fechada_nao_pode_ser_impressa_de_novo()
    {
        var nota = await CriarNotaComItemAsync();
        await _cliente.PostAsync($"/api/notas/{nota.Id}/imprimir", null);

        var segunda = await _cliente.PostAsync($"/api/notas/{nota.Id}/imprimir", null);

        segunda.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await segunda.Content.ReadAsStringAsync()).Should().Contain("NOTA_STATUS_INVALIDO");

        // A guarda impediu qualquer segunda baixa: o estoque nao pode ser
        // debitado duas vezes pela mesma nota.
        _fabrica.Estoque.Baixas.Should().ContainSingle();
    }

    [Fact]
    public async Task Nota_sem_itens_nao_pode_ser_impressa()
    {
        var nota = await (await _cliente.PostAsync("/api/notas", null))
            .Content.ReadFromJsonAsync<NotaFiscalDto>();

        var resposta = await _cliente.PostAsync($"/api/notas/{nota!.Id}/imprimir", null);

        resposta.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await RelerAsync(nota.Id)).Status.Should().Be("Aberta");
        _fabrica.Estoque.Baixas.Should().BeEmpty();
    }

    // ---------- Requisito obrigatorio: falha e recuperacao ----------

    [Fact]
    public async Task Estoque_fora_do_ar_devolve_503_e_mantem_a_nota_aberta()
    {
        var nota = await CriarNotaComItemAsync();
        _fabrica.Estoque.FalhaNaBaixa = new EstoqueIndisponivelExcecao();

        var resposta = await _cliente.PostAsync($"/api/notas/{nota.Id}/imprimir", null);

        resposta.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await resposta.Content.ReadAsStringAsync()).Should().Contain("ESTOQUE_INDISPONIVEL");

        // O ponto central do requisito: o estado persistido volta a ser
        // consistente, e a nota pode ser impressa quando o servico voltar.
        var persistida = await RelerAsync(nota.Id);
        persistida.Status.Should().Be("Aberta");
        persistida.FechadaEm.Should().BeNull();

        // Nao houve baixa, entao estornar aqui devolveria saldo que nunca saiu.
        _fabrica.Estoque.Estornos.Should().BeEmpty();
    }

    [Fact]
    public async Task Nota_recusada_por_indisponibilidade_imprime_quando_o_servico_volta()
    {
        var nota = await CriarNotaComItemAsync();

        _fabrica.Estoque.FalhaNaBaixa = new EstoqueIndisponivelExcecao();
        await _cliente.PostAsync($"/api/notas/{nota.Id}/imprimir", null);
        (await RelerAsync(nota.Id)).Status.Should().Be("Aberta");

        // O servico volta.
        _fabrica.Estoque.FalhaNaBaixa = null;

        var segunda = await _cliente.PostAsync($"/api/notas/{nota.Id}/imprimir", null);

        segunda.StatusCode.Should().Be(HttpStatusCode.OK);
        (await RelerAsync(nota.Id)).Status.Should().Be("Fechada");
        _fabrica.Estoque.Baixas.Should().ContainSingle();
    }

    [Fact]
    public async Task Saldo_insuficiente_devolve_422_com_detalhamento_e_mantem_a_nota_aberta()
    {
        var nota = await CriarNotaComItemAsync();

        _fabrica.Estoque.FalhaNaBaixa = new SaldoInsuficienteNoEstoqueExcecao(
            new[] { new FaltaDeSaldo("PRD-001", 1, 2) });

        var resposta = await _cliente.PostAsync($"/api/notas/{nota.Id}/imprimir", null);

        resposta.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var corpo = await resposta.Content.ReadAsStringAsync();
        corpo.Should().Contain("SALDO_INSUFICIENTE");
        corpo.Should().Contain("PRD-001");
        corpo.Should().Contain("saldoDisponivel");

        (await RelerAsync(nota.Id)).Status.Should().Be("Aberta");
    }

    [Fact]
    public async Task Conflito_de_concorrencia_devolve_409_e_mantem_a_nota_aberta()
    {
        var nota = await CriarNotaComItemAsync();
        _fabrica.Estoque.FalhaNaBaixa = new ConflitoDeConcorrenciaExcecao();

        var resposta = await _cliente.PostAsync($"/api/notas/{nota.Id}/imprimir", null);

        resposta.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await RelerAsync(nota.Id)).Status.Should().Be("Aberta");
    }

    // ---------- Compensacao ----------

    [Fact]
    public async Task Falha_no_pdf_apos_a_baixa_dispara_estorno_e_reabre_a_nota()
    {
        var nota = await CriarNotaComItemAsync(quantidade: 4);
        _fabrica.GeradorDePdf.Falha = new InvalidOperationException("fonte indisponivel");

        var resposta = await _cliente.PostAsync($"/api/notas/{nota.Id}/imprimir", null);

        resposta.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await resposta.Content.ReadAsStringAsync()).Should().Contain("FALHA_GERACAO_PDF");

        // A baixa ja tinha sido confirmada, entao aqui a compensacao e
        // obrigatoria: sem ela, o estoque ficaria debitado por uma nota que
        // continua aberta.
        _fabrica.Estoque.Baixas.Should().ContainSingle();
        _fabrica.Estoque.Estornos.Should().ContainSingle();
        _fabrica.Estoque.Estornos[0].Itens.Single().Quantidade.Should().Be(4);

        (await RelerAsync(nota.Id)).Status.Should().Be("Aberta");
    }

    [Fact]
    public async Task Quando_ate_o_estorno_falha_a_nota_fica_em_processamento()
    {
        var nota = await CriarNotaComItemAsync();

        _fabrica.GeradorDePdf.Falha = new InvalidOperationException("fonte indisponivel");
        _fabrica.Estoque.FalhaNoEstorno = new EstoqueIndisponivelExcecao();

        var resposta = await _cliente.PostAsync($"/api/notas/{nota.Id}/imprimir", null);

        resposta.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await resposta.Content.ReadAsStringAsync()).Should().Contain("INTERVENCAO_MANUAL");

        // Nenhuma compensacao e infalivel. O saldo saiu e nao voltou, e a nota
        // permanece EmProcessamento como marcador da pendencia. Devolve-la
        // para Aberta esconderia a inconsistencia e permitiria uma segunda
        // baixa sobre a primeira.
        (await RelerAsync(nota.Id)).Status.Should().Be("EmProcessamento");
    }

    [Fact]
    public async Task Nota_em_processamento_nao_aceita_edicao_nem_nova_impressao()
    {
        var nota = await CriarNotaComItemAsync();

        _fabrica.GeradorDePdf.Falha = new InvalidOperationException("falha");
        _fabrica.Estoque.FalhaNoEstorno = new EstoqueIndisponivelExcecao();
        await _cliente.PostAsync($"/api/notas/{nota.Id}/imprimir", null);

        (await RelerAsync(nota.Id)).Status.Should().Be("EmProcessamento");

        _fabrica.GeradorDePdf.Falha = null;
        _fabrica.Estoque.FalhaNoEstorno = null;

        var novaImpressao = await _cliente.PostAsync($"/api/notas/{nota.Id}/imprimir", null);
        novaImpressao.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var exclusao = await _cliente.DeleteAsync($"/api/notas/{nota.Id}");
        exclusao.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
