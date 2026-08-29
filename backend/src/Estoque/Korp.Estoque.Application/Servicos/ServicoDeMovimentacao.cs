using Korp.Estoque.Application.Contratos;
using Korp.Estoque.Application.Dtos;
using Korp.Estoque.Domain.Excecoes;

namespace Korp.Estoque.Application.Servicos;

/// <summary>
/// Baixa e estorno de saldo, acionados pelo servico de Faturamento.
///
/// Este e o ponto do sistema onde tres exigencias se encontram:
///
/// RN10  a operacao e atomica: ou todos os itens da nota baixam, ou nenhum.
///       Garantido pela transacao explicita.
/// RN02  o saldo nunca fica negativo. Garantido pela entidade Produto.
/// RN12  o saldo e revalidado no momento da baixa, e nao no momento em que
///       o item foi incluido na nota. Entre um e outro o mundo pode ter mudado.
///
/// O tratamento de concorrencia (retry em caso de conflito otimista) fica na
/// implementacao de ExecutarEmTransacaoAsync, em Infrastructure, porque
/// depende de detalhe do Entity Framework.
/// </summary>
public class ServicoDeMovimentacao
{
    private readonly IProdutoRepositorio _repositorio;
    private readonly IUnidadeDeTrabalho _unidadeDeTrabalho;

    public ServicoDeMovimentacao(IProdutoRepositorio repositorio, IUnidadeDeTrabalho unidadeDeTrabalho)
    {
        _repositorio = repositorio;
        _unidadeDeTrabalho = unidadeDeTrabalho;
    }

    public Task BaixarAsync(MovimentacaoEstoqueDto dto, CancellationToken ct = default) =>
        AplicarAsync(dto, estorno: false, ct);

    public Task EstornarAsync(MovimentacaoEstoqueDto dto, CancellationToken ct = default) =>
        AplicarAsync(dto, estorno: true, ct);

    private async Task AplicarAsync(MovimentacaoEstoqueDto dto, bool estorno, CancellationToken ct)
    {
        if (dto.Itens is null || dto.Itens.Count == 0)
            throw new DadosInvalidosExcecao("A movimentacao precisa conter ao menos um item.");

        // Consolida antes de tocar no banco: se a mesma nota citar o mesmo
        // produto em duas linhas, o que importa e a soma. Sem isso, duas
        // baixas parciais poderiam passar pela validacao de saldo separadamente
        // e estourar o estoque juntas.
        var quantidadesPorProduto = dto.Itens
            .GroupBy(i => i.ProdutoId)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantidade));

        await _unidadeDeTrabalho.ExecutarEmTransacaoAsync(async () =>
        {
            var produtos = await _repositorio.ObterPorIdsAsync(quantidadesPorProduto.Keys, ct);
            var produtosPorId = produtos.ToDictionary(p => p.Id);

            var idInexistente = quantidadesPorProduto.Keys
                .FirstOrDefault(id => !produtosPorId.ContainsKey(id));

            if (idInexistente != default)
                throw new ProdutoNaoEncontradoExcecao(idInexistente);

            foreach (var (produtoId, quantidade) in quantidadesPorProduto)
            {
                var produto = produtosPorId[produtoId];

                if (estorno)
                    produto.Estornar(quantidade, dto.NotaId);
                else
                    produto.Baixar(quantidade, dto.NotaId);
            }

            await _unidadeDeTrabalho.SalvarAsync(ct);
        }, ct);
    }
}
