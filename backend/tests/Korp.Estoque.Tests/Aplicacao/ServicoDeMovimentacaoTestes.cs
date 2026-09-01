using FluentAssertions;
using Korp.Estoque.Application.Contratos;
using Korp.Estoque.Application.Dtos;
using Korp.Estoque.Application.Servicos;
using Korp.Estoque.Domain.Entidades;
using Korp.Estoque.Domain.Excecoes;
using NSubstitute;
using Xunit;

namespace Korp.Estoque.Tests.Aplicacao;

/// <summary>
/// Testes do caso de uso de baixa e estorno.
///
/// A regra de saldo vive na entidade e ja tem os proprios testes. O que se
/// verifica aqui e a **orquestracao**: consolidar quantidades antes de tocar
/// no banco, carregar todos os produtos de uma vez e envolver tudo em
/// transacao.
/// </summary>
public class ServicoDeMovimentacaoTestes
{
    private readonly IProdutoRepositorio _repositorio = Substitute.For<IProdutoRepositorio>();
    private readonly IUnidadeDeTrabalho _unidadeDeTrabalho = Substitute.For<IUnidadeDeTrabalho>();
    private readonly ServicoDeMovimentacao _servico;

    public ServicoDeMovimentacaoTestes()
    {
        // A implementacao real abre transacao e trata conflito de concorrencia.
        // No teste unitario ela apenas executa a operacao recebida, para que o
        // foco fique na logica do caso de uso.
        _unidadeDeTrabalho
            .ExecutarEmTransacaoAsync(Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>())
            .Returns(chamada => ((Func<Task>)chamada[0]).Invoke());

        _servico = new ServicoDeMovimentacao(_repositorio, _unidadeDeTrabalho);
    }

    private Produto RegistrarProduto(string codigo, int saldo)
    {
        var produto = Produto.Criar(codigo, $"Produto {codigo}", saldo);

        _repositorio
            .ObterPorIdsAsync(Arg.Is<IEnumerable<Guid>>(ids => ids.Contains(produto.Id)),
                              Arg.Any<CancellationToken>())
            .Returns(new List<Produto> { produto });

        return produto;
    }

    [Fact]
    public async Task Baixa_reduz_o_saldo_e_grava()
    {
        var produto = RegistrarProduto("PRD-001", 10);

        await _servico.BaixarAsync(new MovimentacaoEstoqueDto(
            Guid.NewGuid(), new[] { new ItemMovimentacaoDto(produto.Id, 2) }));

        produto.Saldo.Should().Be(8);
        await _unidadeDeTrabalho.Received(1).SalvarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Baixa_roda_dentro_de_transacao()
    {
        var produto = RegistrarProduto("PRD-001", 10);

        await _servico.BaixarAsync(new MovimentacaoEstoqueDto(
            Guid.NewGuid(), new[] { new ItemMovimentacaoDto(produto.Id, 1) }));

        // RN10: a atomicidade depende da transacao explicita, entao vale
        // garantir que o caso de uso nao deixe de abri-la.
        await _unidadeDeTrabalho.Received(1).ExecutarEmTransacaoAsync(
            Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// O teste mais importante desta classe.
    ///
    /// Se a mesma nota citar o mesmo produto em duas linhas, elas precisam ser
    /// somadas ANTES da validacao de saldo. Sem a consolidacao, duas baixas de
    /// 3 unidades passariam individualmente por um saldo de 5 e o estoque
    /// terminaria negativo, ou a segunda falharia com o estado ja alterado.
    /// </summary>
    [Fact]
    public async Task Mesmo_produto_repetido_na_nota_tem_as_quantidades_somadas()
    {
        var produto = RegistrarProduto("PRD-001", 10);

        await _servico.BaixarAsync(new MovimentacaoEstoqueDto(Guid.NewGuid(), new[]
        {
            new ItemMovimentacaoDto(produto.Id, 3),
            new ItemMovimentacaoDto(produto.Id, 4)
        }));

        produto.Saldo.Should().Be(3);

        // Uma unica movimentacao de 7, e nao duas de 3 e 4.
        produto.Movimentacoes.Should().ContainSingle();
        produto.Movimentacoes.Single().Quantidade.Should().Be(7);
    }

    [Fact]
    public async Task Produto_repetido_com_soma_acima_do_saldo_e_recusado()
    {
        var produto = RegistrarProduto("PRD-001", 5);

        var acao = async () => await _servico.BaixarAsync(new MovimentacaoEstoqueDto(
            Guid.NewGuid(), new[]
            {
                new ItemMovimentacaoDto(produto.Id, 3),
                new ItemMovimentacaoDto(produto.Id, 3)
            }));

        // Individualmente as duas linhas caberiam no saldo 5. Somadas, nao.
        // E exatamente esse o bug que a consolidacao previne.
        await acao.Should().ThrowAsync<SaldoInsuficienteExcecao>();

        produto.Saldo.Should().Be(5);
        produto.Movimentacoes.Should().BeEmpty();
    }

    [Fact]
    public async Task Movimentacao_sem_itens_e_recusada()
    {
        var acao = async () => await _servico.BaixarAsync(
            new MovimentacaoEstoqueDto(Guid.NewGuid(), Array.Empty<ItemMovimentacaoDto>()));

        await acao.Should().ThrowAsync<DadosInvalidosExcecao>();

        await _unidadeDeTrabalho.DidNotReceiveWithAnyArgs()
            .ExecutarEmTransacaoAsync(default!, default);
    }

    [Fact]
    public async Task Produto_inexistente_interrompe_a_operacao()
    {
        var idFantasma = Guid.NewGuid();

        _repositorio.ObterPorIdsAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Produto>());

        var acao = async () => await _servico.BaixarAsync(new MovimentacaoEstoqueDto(
            Guid.NewGuid(), new[] { new ItemMovimentacaoDto(idFantasma, 1) }));

        await acao.Should().ThrowAsync<ProdutoNaoEncontradoExcecao>();
        await _unidadeDeTrabalho.DidNotReceive().SalvarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Produtos_sao_carregados_em_uma_unica_consulta()
    {
        var a = Produto.Criar("PRD-001", "A", 10);
        var b = Produto.Criar("PRD-002", "B", 10);

        _repositorio.ObterPorIdsAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Produto> { a, b });

        await _servico.BaixarAsync(new MovimentacaoEstoqueDto(Guid.NewGuid(), new[]
        {
            new ItemMovimentacaoDto(a.Id, 1),
            new ItemMovimentacaoDto(b.Id, 2)
        }));

        // Uma chamada, e nao uma por item. Com nota de 20 itens, a diferenca
        // entre 1 e 20 idas ao banco.
        await _repositorio.Received(1)
            .ObterPorIdsAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>());

        a.Saldo.Should().Be(9);
        b.Saldo.Should().Be(8);
    }

    [Fact]
    public async Task Estorno_devolve_a_quantidade_ao_saldo()
    {
        var produto = RegistrarProduto("PRD-001", 6);

        await _servico.EstornarAsync(new MovimentacaoEstoqueDto(
            Guid.NewGuid(), new[] { new ItemMovimentacaoDto(produto.Id, 4) }));

        produto.Saldo.Should().Be(10);
        produto.Movimentacoes.Single().Tipo.Should().Be(TipoMovimentacao.Estorno);
    }

    [Fact]
    public async Task Estorno_tambem_consolida_produto_repetido()
    {
        var produto = RegistrarProduto("PRD-001", 0);

        await _servico.EstornarAsync(new MovimentacaoEstoqueDto(Guid.NewGuid(), new[]
        {
            new ItemMovimentacaoDto(produto.Id, 2),
            new ItemMovimentacaoDto(produto.Id, 3)
        }));

        produto.Saldo.Should().Be(5);
        produto.Movimentacoes.Should().ContainSingle();
    }
}
