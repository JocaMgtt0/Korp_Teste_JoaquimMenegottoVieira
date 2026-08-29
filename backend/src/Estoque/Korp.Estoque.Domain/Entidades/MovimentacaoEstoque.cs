namespace Korp.Estoque.Domain.Entidades;

public enum TipoMovimentacao
{
    Baixa = 1,
    Estorno = 2
}

/// <summary>
/// Registro imutavel de cada alteracao de saldo provocada por uma nota fiscal.
///
/// Serve a tres propositos:
/// 1. Trilha de auditoria: responde "por que o saldo deste produto mudou".
/// 2. Sustenta a RN09, que proibe excluir produto ja usado em nota.
/// 3. Torna o estorno verificavel: da para provar que a compensacao ocorreu.
///
/// <see cref="NotaId"/> e uma referencia logica ao servico de Faturamento.
/// Nao existe chave estrangeira, porque os dois bancos sao fisicamente
/// separados. Essa ausencia e intencional, nao um esquecimento.
/// </summary>
public class MovimentacaoEstoque
{
    private MovimentacaoEstoque() { }

    private MovimentacaoEstoque(
        Guid produtoId, Guid notaId, TipoMovimentacao tipo,
        int quantidade, int saldoAnterior, int saldoPosterior)
    {
        Id = Guid.NewGuid();
        ProdutoId = produtoId;
        NotaId = notaId;
        Tipo = tipo;
        Quantidade = quantidade;
        SaldoAnterior = saldoAnterior;
        SaldoPosterior = saldoPosterior;
        OcorridoEm = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid ProdutoId { get; private set; }
    public Guid NotaId { get; private set; }
    public TipoMovimentacao Tipo { get; private set; }
    public int Quantidade { get; private set; }
    public int SaldoAnterior { get; private set; }
    public int SaldoPosterior { get; private set; }
    public DateTime OcorridoEm { get; private set; }

    internal static MovimentacaoEstoque DeBaixa(
        Guid produtoId, Guid notaId, int quantidade, int saldoAnterior, int saldoPosterior) =>
        new(produtoId, notaId, TipoMovimentacao.Baixa, quantidade, saldoAnterior, saldoPosterior);

    internal static MovimentacaoEstoque DeEstorno(
        Guid produtoId, Guid notaId, int quantidade, int saldoAnterior, int saldoPosterior) =>
        new(produtoId, notaId, TipoMovimentacao.Estorno, quantidade, saldoAnterior, saldoPosterior);
}
