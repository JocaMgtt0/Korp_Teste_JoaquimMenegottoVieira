using Korp.Faturamento.Application.Dtos;
using Korp.Faturamento.Domain.Entidades;

namespace Korp.Faturamento.Application.Contratos;

public interface IRepositorioDeNotas
{
    Task<NotaFiscal?> ObterPorIdAsync(Guid id, CancellationToken ct = default);

    Task<(IReadOnlyList<NotaFiscal> Itens, int Total)> ListarAsync(
        StatusNotaFiscal? status, int pagina, int tamanho, CancellationToken ct = default);

    /// <summary>
    /// Reserva o proximo numero na sequence do banco (RN04).
    ///
    /// Sequence e nao MAX(numero) + 1: sequence e atomica e nunca devolve o
    /// mesmo valor duas vezes, mesmo com dez requisicoes simultaneas. Com MAX
    /// duas notas concorrentes leriam o mesmo maximo e tentariam gravar o
    /// mesmo numero.
    /// </summary>
    Task<long> ProximoNumeroAsync(CancellationToken ct = default);

    void Adicionar(NotaFiscal nota);

    void Remover(NotaFiscal nota);
}

public interface IUnidadeDeTrabalho
{
    Task<int> SalvarAsync(CancellationToken ct = default);
}

/// <summary>
/// Contrato de comunicacao com o servico de Estoque.
///
/// A interface vive aqui, em Application, e a implementacao com HttpClient e
/// Polly vive em Infrastructure. E essa inversao que permite testar o caso de
/// uso de impressao inteiro, incluindo os caminhos de falha, sem subir
/// servidor nenhum: basta um duble que lanca a excecao desejada.
///
/// As implementacoes devem traduzir falha de rede em
/// <see cref="Excecoes.EstoqueIndisponivelExcecao"/> e recusa de negocio em
/// <see cref="Excecoes.SaldoInsuficienteNoEstoqueExcecao"/>. O caso de uso
/// depende dessa distincao para decidir se compensa ou apenas reverte.
/// </summary>
public interface IServicoDeEstoque
{
    Task BaixarAsync(Guid notaId, IReadOnlyList<ItemMovimentacao> itens, CancellationToken ct = default);

    Task EstornarAsync(Guid notaId, IReadOnlyList<ItemMovimentacao> itens, CancellationToken ct = default);

    Task<IReadOnlyList<SaldoProdutoDto>> ConsultarSaldoAsync(
        IReadOnlyList<Guid> produtoIds, CancellationToken ct = default);
}

public interface IGeradorDePdf
{
    byte[] Gerar(NotaFiscal nota);
}
