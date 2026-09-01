using FluentAssertions;
using Korp.Faturamento.Application.Contratos;
using Korp.Faturamento.Application.Dtos;
using Korp.Faturamento.Application.Excecoes;
using Korp.Faturamento.Application.Servicos;
using Korp.Faturamento.Domain.Entidades;
using Korp.Faturamento.Domain.Excecoes;
using NSubstitute;
using Xunit;

namespace Korp.Faturamento.Tests.Aplicacao;

/// <summary>
/// Testes do caso de uso de cadastro e edicao de notas.
///
/// A maquina de estados vive na entidade e ja tem os proprios testes. O foco
/// aqui e o que so o caso de uso faz: reservar o numero na sequence, buscar
/// os dados do produto no servico de Estoque para gravar o snapshot, e a
/// validacao previa de saldo.
/// </summary>
public class ServicoDeNotasTestes
{
    private readonly IRepositorioDeNotas _repositorio = Substitute.For<IRepositorioDeNotas>();
    private readonly IUnidadeDeTrabalho _unidadeDeTrabalho = Substitute.For<IUnidadeDeTrabalho>();
    private readonly IServicoDeEstoque _estoque = Substitute.For<IServicoDeEstoque>();
    private readonly ServicoDeNotas _servico;

    private static readonly Guid ProdutoId = Guid.NewGuid();

    public ServicoDeNotasTestes()
    {
        _servico = new ServicoDeNotas(_repositorio, _unidadeDeTrabalho, _estoque);

        _repositorio.ProximoNumeroAsync(Arg.Any<CancellationToken>()).Returns(1L);
    }

    private void ProdutoNoEstoque(int saldo, string codigo = "PRD-001", string descricao = "Teclado")
    {
        IReadOnlyList<SaldoProdutoDto> resposta =
            new[] { new SaldoProdutoDto(ProdutoId, codigo, descricao, saldo) };

        _estoque.ConsultarSaldoAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(resposta);
    }

    private NotaFiscal RegistrarNota(long numero = 1)
    {
        var nota = NotaFiscal.Criar(numero);
        _repositorio.ObterPorIdAsync(nota.Id, Arg.Any<CancellationToken>()).Returns(nota);
        return nota;
    }

    // ---------- Criacao ----------

    [Fact]
    public async Task Criar_reserva_o_numero_na_sequence()
    {
        _repositorio.ProximoNumeroAsync(Arg.Any<CancellationToken>()).Returns(42L);

        var dto = await _servico.CriarAsync();

        dto.Numero.Should().Be(42);
        dto.Status.Should().Be("Aberta");

        // RN04: o numero vem da sequence, nao de MAX(numero) + 1.
        await _repositorio.Received(1).ProximoNumeroAsync(Arg.Any<CancellationToken>());
        _repositorio.Received(1).Adicionar(Arg.Any<NotaFiscal>());
    }

    // ---------- Inclusao de item ----------

    [Fact]
    public async Task AdicionarItem_grava_o_snapshot_do_produto()
    {
        var nota = RegistrarNota();
        ProdutoNoEstoque(saldo: 50, codigo: "PRD-007", descricao: "Hub USB-C");

        var dto = await _servico.AdicionarItemAsync(nota.Id, new AdicionarItemDto(ProdutoId, 3));

        var item = dto.Itens.Single();

        // RN11: o item guarda copia de codigo e descricao, para a nota poder
        // ser exibida e impressa com o servico de Estoque fora do ar.
        item.ProdutoCodigo.Should().Be("PRD-007");
        item.ProdutoDescricao.Should().Be("Hub USB-C");
        item.Quantidade.Should().Be(3);

        await _unidadeDeTrabalho.Received(1).SalvarAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task AdicionarItem_com_quantidade_nao_positiva_nem_consulta_o_estoque(int quantidade)
    {
        var nota = RegistrarNota();

        var acao = async () => await _servico.AdicionarItemAsync(
            nota.Id, new AdicionarItemDto(ProdutoId, quantidade));

        await acao.Should().ThrowAsync<QuantidadeInvalidaExcecao>();

        // Recusa barata primeiro: nao gasta uma chamada de rede para descobrir
        // que a entrada era invalida.
        await _estoque.DidNotReceiveWithAnyArgs().ConsultarSaldoAsync(default!, default);
    }

    [Fact]
    public async Task AdicionarItem_com_produto_inexistente_no_estoque_e_recusado()
    {
        var nota = RegistrarNota();

        _estoque.ConsultarSaldoAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<SaldoProdutoDto>());

        var acao = async () => await _servico.AdicionarItemAsync(
            nota.Id, new AdicionarItemDto(ProdutoId, 1));

        await acao.Should().ThrowAsync<DadosInvalidosExcecao>();
        await _unidadeDeTrabalho.DidNotReceive().SalvarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdicionarItem_acima_do_saldo_e_recusado_com_detalhamento()
    {
        var nota = RegistrarNota();
        ProdutoNoEstoque(saldo: 2);

        var acao = async () => await _servico.AdicionarItemAsync(
            nota.Id, new AdicionarItemDto(ProdutoId, 5));

        var excecao = (await acao.Should().ThrowAsync<SaldoInsuficienteNoEstoqueExcecao>()).Which;

        excecao.Faltas.Should().ContainSingle();
        excecao.Faltas[0].SaldoDisponivel.Should().Be(2);
        excecao.Faltas[0].QuantidadeSolicitada.Should().Be(5);
    }

    /// <summary>
    /// O teste mais importante desta classe.
    ///
    /// A validacao precisa considerar o que JA esta na nota. Incluir 3 quando
    /// ja ha 4 exige saldo 7, nao 3. Sem isso, inclusoes sucessivas passariam
    /// uma a uma e a nota so estouraria na impressao, quando o usuario ja
    /// perdeu tempo montando o documento inteiro.
    /// </summary>
    [Fact]
    public async Task AdicionarItem_considera_a_quantidade_ja_presente_na_nota()
    {
        var nota = RegistrarNota();
        ProdutoNoEstoque(saldo: 5);

        await _servico.AdicionarItemAsync(nota.Id, new AdicionarItemDto(ProdutoId, 4));

        // Isolada, a segunda inclusao de 3 caberia no saldo 5.
        // Somada a de 4 que ja esta na nota, nao cabe.
        var acao = async () => await _servico.AdicionarItemAsync(
            nota.Id, new AdicionarItemDto(ProdutoId, 3));

        var excecao = (await acao.Should().ThrowAsync<SaldoInsuficienteNoEstoqueExcecao>()).Which;

        excecao.Faltas[0].QuantidadeSolicitada.Should().Be(7);
        excecao.Faltas[0].SaldoDisponivel.Should().Be(5);

        // A nota permanece com a quantidade que era valida.
        nota.Itens.Single().Quantidade.Should().Be(4);
    }

    [Fact]
    public async Task AdicionarItem_repetido_dentro_do_saldo_soma_na_mesma_linha()
    {
        var nota = RegistrarNota();
        ProdutoNoEstoque(saldo: 10);

        await _servico.AdicionarItemAsync(nota.Id, new AdicionarItemDto(ProdutoId, 2));
        var dto = await _servico.AdicionarItemAsync(nota.Id, new AdicionarItemDto(ProdutoId, 3));

        dto.Itens.Should().ContainSingle();
        dto.Itens.Single().Quantidade.Should().Be(5);
    }

    [Fact]
    public async Task AdicionarItem_em_nota_fechada_e_recusado()
    {
        var nota = RegistrarNota();
        ProdutoNoEstoque(saldo: 10);

        await _servico.AdicionarItemAsync(nota.Id, new AdicionarItemDto(ProdutoId, 1));
        nota.IniciarProcessamento();
        nota.ConfirmarImpressao();

        var acao = async () => await _servico.AdicionarItemAsync(
            nota.Id, new AdicionarItemDto(ProdutoId, 1));

        await acao.Should().ThrowAsync<StatusInvalidoExcecao>();
    }

    // ---------- Exclusao e itens ----------

    [Fact]
    public async Task Excluir_nota_aberta_e_permitido()
    {
        var nota = RegistrarNota();

        await _servico.ExcluirAsync(nota.Id);

        _repositorio.Received(1).Remover(nota);
        await _unidadeDeTrabalho.Received(1).SalvarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Excluir_nota_fechada_e_recusado()
    {
        var nota = RegistrarNota();
        ProdutoNoEstoque(saldo: 10);
        await _servico.AdicionarItemAsync(nota.Id, new AdicionarItemDto(ProdutoId, 1));
        nota.IniciarProcessamento();
        nota.ConfirmarImpressao();

        var acao = async () => await _servico.ExcluirAsync(nota.Id);

        // RN07: nota fechada e documento emitido, nao se apaga.
        await acao.Should().ThrowAsync<StatusInvalidoExcecao>();
        _repositorio.DidNotReceive().Remover(Arg.Any<NotaFiscal>());
    }

    [Fact]
    public async Task RemoverItem_tira_a_linha_e_grava()
    {
        var nota = RegistrarNota();
        ProdutoNoEstoque(saldo: 10);
        var dto = await _servico.AdicionarItemAsync(nota.Id, new AdicionarItemDto(ProdutoId, 2));

        var atualizada = await _servico.RemoverItemAsync(nota.Id, dto.Itens.Single().Id);

        atualizada.Itens.Should().BeEmpty();
    }

    [Fact]
    public async Task Operacoes_em_nota_inexistente_sao_recusadas()
    {
        _repositorio.ObterPorIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((NotaFiscal?)null);

        var acao = async () => await _servico.ObterPorIdAsync(Guid.NewGuid());

        await acao.Should().ThrowAsync<NotaNaoEncontradaExcecao>();
    }

    // ---------- Listagem ----------

    [Fact]
    public async Task Listagem_com_status_invalido_e_recusada()
    {
        var acao = async () => await _servico.ListarAsync("Inexistente", 1, 10);

        await acao.Should().ThrowAsync<DadosInvalidosExcecao>();
    }

    [Theory]
    [InlineData("Aberta")]
    [InlineData("aberta")]
    [InlineData("FECHADA")]
    public async Task Listagem_aceita_status_sem_diferenciar_maiusculas(string status)
    {
        _repositorio.ListarAsync(Arg.Any<StatusNotaFiscal?>(), Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns((new List<NotaFiscal>(), 0));

        var acao = async () => await _servico.ListarAsync(status, 1, 10);

        await acao.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Listagem_projeta_totais_de_cada_nota()
    {
        var nota = NotaFiscal.Criar(1);
        nota.AdicionarItem(Guid.NewGuid(), "PRD-001", "A", 2);
        nota.AdicionarItem(Guid.NewGuid(), "PRD-002", "B", 5);

        _repositorio.ListarAsync(Arg.Any<StatusNotaFiscal?>(), Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns((new List<NotaFiscal> { nota }, 1));

        var resultado = await _servico.ListarAsync(null, 1, 10);

        var resumo = resultado.Itens.Single();
        resumo.TotalDeItens.Should().Be(2);
        resumo.QuantidadeTotal.Should().Be(7);
    }
}
