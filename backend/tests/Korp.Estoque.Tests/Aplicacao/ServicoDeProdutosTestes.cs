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
/// Testes do caso de uso de cadastro de produtos.
///
/// A validacao de formato vive na entidade. O que se verifica aqui e o que
/// so o caso de uso sabe fazer: consultar o repositorio antes de decidir, e
/// gravar apenas quando a operacao e valida.
/// </summary>
public class ServicoDeProdutosTestes
{
    private readonly IProdutoRepositorio _repositorio = Substitute.For<IProdutoRepositorio>();
    private readonly IUnidadeDeTrabalho _unidadeDeTrabalho = Substitute.For<IUnidadeDeTrabalho>();
    private readonly ServicoDeProdutos _servico;

    public ServicoDeProdutosTestes() =>
        _servico = new ServicoDeProdutos(_repositorio, _unidadeDeTrabalho);

    // ---------- Criacao ----------

    [Fact]
    public async Task Criar_verifica_unicidade_antes_de_gravar()
    {
        _repositorio.ExisteComCodigoAsync("PRD-001", Arg.Any<CancellationToken>()).Returns(false);

        var dto = await _servico.CriarAsync(new CriarProdutoDto("PRD-001", "Teclado", 10));

        dto.Codigo.Should().Be("PRD-001");
        dto.Saldo.Should().Be(10);

        _repositorio.Received(1).Adicionar(Arg.Any<Produto>());
        await _unidadeDeTrabalho.Received(1).SalvarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Criar_com_codigo_ja_existente_e_recusado_sem_gravar()
    {
        _repositorio.ExisteComCodigoAsync("PRD-001", Arg.Any<CancellationToken>()).Returns(true);

        var acao = async () => await _servico.CriarAsync(new CriarProdutoDto("PRD-001", "Teclado", 10));

        // RN01. A unicidade nao cabe na entidade porque depende do conjunto
        // inteiro de produtos, que so o repositorio conhece.
        (await acao.Should().ThrowAsync<CodigoDuplicadoExcecao>())
            .Which.CodigoProduto.Should().Be("PRD-001");

        _repositorio.DidNotReceive().Adicionar(Arg.Any<Produto>());
        await _unidadeDeTrabalho.DidNotReceive().SalvarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Criar_verifica_unicidade_com_o_codigo_sem_espacos()
    {
        _repositorio.ExisteComCodigoAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        await _servico.CriarAsync(new CriarProdutoDto("  PRD-001  ", "Teclado", 5));

        // Consultar com espacos deixaria passar um duplicado que o indice
        // unico do banco recusaria depois, com erro bem menos claro.
        await _repositorio.Received(1).ExisteComCodigoAsync("PRD-001", Arg.Any<CancellationToken>());
    }

    // ---------- Atualizacao ----------

    [Fact]
    public async Task Atualizar_altera_descricao_e_saldo()
    {
        var produto = Produto.Criar("PRD-001", "Teclado", 10);
        _repositorio.ObterPorIdAsync(produto.Id, Arg.Any<CancellationToken>()).Returns(produto);

        var dto = await _servico.AtualizarAsync(produto.Id, new AtualizarProdutoDto("Teclado RGB", 20));

        dto.Descricao.Should().Be("Teclado RGB");
        dto.Saldo.Should().Be(20);

        // O codigo nao aparece no DTO de atualizacao: pela RN01 ele e imutavel.
        dto.Codigo.Should().Be("PRD-001");

        await _unidadeDeTrabalho.Received(1).SalvarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Atualizar_produto_inexistente_e_recusado()
    {
        _repositorio.ObterPorIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Produto?)null);

        var acao = async () => await _servico.AtualizarAsync(
            Guid.NewGuid(), new AtualizarProdutoDto("Novo nome", 5));

        await acao.Should().ThrowAsync<ProdutoNaoEncontradoExcecao>();
        await _unidadeDeTrabalho.DidNotReceive().SalvarAsync(Arg.Any<CancellationToken>());
    }

    // ---------- Exclusao (RN09) ----------

    [Fact]
    public async Task Excluir_produto_nunca_movimentado_e_permitido()
    {
        var produto = Produto.Criar("PRD-001", "Teclado", 10);
        _repositorio.ObterPorIdAsync(produto.Id, Arg.Any<CancellationToken>()).Returns(produto);
        _repositorio.PossuiMovimentacaoAsync(produto.Id, Arg.Any<CancellationToken>()).Returns(false);

        await _servico.ExcluirAsync(produto.Id);

        _repositorio.Received(1).Remover(produto);
        await _unidadeDeTrabalho.Received(1).SalvarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Excluir_produto_ja_usado_em_nota_e_recusado()
    {
        var produto = Produto.Criar("PRD-001", "Teclado", 10);
        _repositorio.ObterPorIdAsync(produto.Id, Arg.Any<CancellationToken>()).Returns(produto);
        _repositorio.PossuiMovimentacaoAsync(produto.Id, Arg.Any<CancellationToken>()).Returns(true);

        var acao = async () => await _servico.ExcluirAsync(produto.Id);

        // RN09: produto com historico nao desaparece, senao notas ja emitidas
        // apontariam para um produto que nao existe mais.
        await acao.Should().ThrowAsync<ProdutoEmUsoExcecao>();

        _repositorio.DidNotReceive().Remover(Arg.Any<Produto>());
    }

    // ---------- Listagem ----------

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(3, 3)]
    public async Task Listagem_corrige_pagina_invalida(int informada, int esperada)
    {
        _repositorio.ListarAsync(Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns((new List<Produto>(), 0));

        var resultado = await _servico.ListarAsync(null, informada, 10);

        resultado.Pagina.Should().Be(esperada);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(500, 20)]
    [InlineData(50, 50)]
    public async Task Listagem_limita_o_tamanho_da_pagina(int informado, int esperado)
    {
        _repositorio.ListarAsync(Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns((new List<Produto>(), 0));

        var resultado = await _servico.ListarAsync(null, 1, informado);

        // Teto de 100 itens por pagina: sem ele, um cliente poderia pedir a
        // tabela inteira em uma requisicao.
        resultado.Tamanho.Should().Be(esperado);
    }

    [Fact]
    public async Task Listagem_calcula_o_total_de_paginas()
    {
        var produtos = Enumerable.Range(1, 10)
            .Select(i => Produto.Criar($"PRD-{i:D3}", $"Produto {i}", i))
            .ToList();

        _repositorio.ListarAsync(Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns((produtos, 25));

        var resultado = await _servico.ListarAsync(null, 1, 10);

        resultado.Total.Should().Be(25);
        resultado.TotalPaginas.Should().Be(3);
        resultado.Itens.Should().HaveCount(10);
    }

    [Fact]
    public async Task ConsultarSaldo_projeta_apenas_os_produtos_encontrados()
    {
        var a = Produto.Criar("PRD-001", "A", 7);
        var b = Produto.Criar("PRD-002", "B", 3);

        _repositorio.ObterPorIdsAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Produto> { a, b });

        var saldos = await _servico.ConsultarSaldoAsync(new[] { a.Id, b.Id, Guid.NewGuid() });

        saldos.Should().HaveCount(2);
        saldos.Single(p => p.Codigo == "PRD-001").Saldo.Should().Be(7);
    }
}
