namespace Korp.Faturamento.Application.Dtos;

public record ItemNotaFiscalDto(
    Guid Id,
    Guid ProdutoId,
    string ProdutoCodigo,
    string ProdutoDescricao,
    int Quantidade);

public record NotaFiscalDto(
    Guid Id,
    long Numero,
    string Status,
    DateTime CriadaEm,
    DateTime? FechadaEm,
    IReadOnlyList<ItemNotaFiscalDto> Itens)
{
    public int TotalDeItens => Itens.Count;
    public int QuantidadeTotal => Itens.Sum(i => i.Quantidade);
}

/// <summary>
/// Resumo usado na listagem. Nao carrega os itens: a tela de lista mostra
/// apenas numero, status e totais, entao trazer as linhas seria desperdicio.
/// </summary>
public record NotaFiscalResumoDto(
    Guid Id,
    long Numero,
    string Status,
    DateTime CriadaEm,
    DateTime? FechadaEm,
    int TotalDeItens,
    int QuantidadeTotal);

public record AdicionarItemDto(Guid ProdutoId, int Quantidade);

public record AlterarQuantidadeDto(int Quantidade);

/// <summary>Item enviado ao servico de Estoque para baixa ou estorno.</summary>
public record ItemMovimentacao(Guid ProdutoId, int Quantidade);

/// <summary>Saldo consultado no servico de Estoque.</summary>
public record SaldoProdutoDto(Guid Id, string Codigo, string Descricao, int Saldo);

/// <summary>Produto que faltou saldo, detalhado por item.</summary>
public record FaltaDeSaldo(string ProdutoCodigo, int SaldoDisponivel, int QuantidadeSolicitada);

public record ResultadoImpressao(Guid NotaId, long Numero, byte[] Pdf);

public record ResultadoPaginado<T>(IReadOnlyList<T> Itens, int Total, int Pagina, int Tamanho)
{
    public int TotalPaginas => Tamanho <= 0 ? 0 : (int)Math.Ceiling(Total / (double)Tamanho);
}
