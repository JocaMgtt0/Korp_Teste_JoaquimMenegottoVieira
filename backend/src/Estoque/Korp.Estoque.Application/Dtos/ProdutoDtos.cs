namespace Korp.Estoque.Application.Dtos;

public record ProdutoDto(
    Guid Id,
    string Codigo,
    string Descricao,
    int Saldo,
    DateTime CriadoEm,
    DateTime AtualizadoEm);

public record CriarProdutoDto(string Codigo, string Descricao, int Saldo);

/// <summary>
/// Alteracao de produto. O codigo nao aparece aqui de proposito:
/// pela RN01 ele e imutavel apos a criacao.
/// </summary>
public record AtualizarProdutoDto(string Descricao, int Saldo);

public record ItemMovimentacaoDto(Guid ProdutoId, int Quantidade);

/// <summary>
/// Requisicao de baixa vinda do servico de Faturamento.
/// Pela RN10 a operacao e atomica: ou todos os itens baixam, ou nenhum.
/// </summary>
public record MovimentacaoEstoqueDto(Guid NotaId, IReadOnlyList<ItemMovimentacaoDto> Itens);

public record ResultadoPaginado<T>(IReadOnlyList<T> Itens, int Total, int Pagina, int Tamanho)
{
    public int TotalPaginas => Tamanho <= 0 ? 0 : (int)Math.Ceiling(Total / (double)Tamanho);
}
