using Korp.Estoque.Domain.Entidades;

namespace Korp.Estoque.Application.Contratos;

/// <summary>
/// Contrato de persistencia de produtos.
///
/// A interface vive na camada de Application e a implementacao vive em
/// Infrastructure. E essa inversao que mantem o nucleo da aplicacao sem
/// nenhuma referencia a Entity Framework, e o que permite testar os casos
/// de uso com um duble em vez de um banco.
/// </summary>
public interface IProdutoRepositorio
{
    Task<Produto?> ObterPorIdAsync(Guid id, CancellationToken ct = default);

    Task<Produto?> ObterPorCodigoAsync(string codigo, CancellationToken ct = default);

    /// <summary>
    /// Carrega varios produtos de uma vez, para a baixa de uma nota inteira.
    /// Evita o problema de N+1 consultas quando a nota tem muitos itens.
    /// </summary>
    Task<IReadOnlyList<Produto>> ObterPorIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default);

    Task<(IReadOnlyList<Produto> Itens, int Total)> ListarAsync(
        string? busca, int pagina, int tamanho, CancellationToken ct = default);

    Task<bool> ExisteComCodigoAsync(string codigo, CancellationToken ct = default);

    /// <summary>Sustenta a RN09: produto ja movimentado nao pode ser excluido.</summary>
    Task<bool> PossuiMovimentacaoAsync(Guid produtoId, CancellationToken ct = default);

    void Adicionar(Produto produto);

    void Remover(Produto produto);
}

/// <summary>
/// Unidade de trabalho. Isola a camada de aplicacao do DbContext, mantendo
/// o "quando" da gravacao sob controle do caso de uso.
/// </summary>
public interface IUnidadeDeTrabalho
{
    Task<int> SalvarAsync(CancellationToken ct = default);

    /// <summary>
    /// Executa a operacao dentro de uma transacao explicita, com nova tentativa
    /// automatica em caso de conflito de concorrencia otimista.
    /// </summary>
    Task ExecutarEmTransacaoAsync(Func<Task> operacao, CancellationToken ct = default);
}
