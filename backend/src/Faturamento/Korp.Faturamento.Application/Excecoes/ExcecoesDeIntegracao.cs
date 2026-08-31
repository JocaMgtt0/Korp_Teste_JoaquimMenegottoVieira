using Korp.Faturamento.Application.Dtos;
using Korp.Faturamento.Domain.Excecoes;

namespace Korp.Faturamento.Application.Excecoes;

/// <summary>
/// O servico de Estoque nao respondeu: fora do ar, tempo esgotado ou circuito
/// aberto. Vira HTTP 503 para o usuario.
///
/// E a excecao central do requisito obrigatorio de tratamento de falhas.
/// Quando ela acontece, a nota ja voltou para Aberta e nenhum saldo foi
/// alterado: o sistema esta consistente e o usuario pode tentar de novo.
/// </summary>
public sealed class EstoqueIndisponivelExcecao : ExcecaoDeDominio
{
    public EstoqueIndisponivelExcecao(Exception? causa = null)
        : base("ESTOQUE_INDISPONIVEL",
               "O servico de Estoque esta indisponivel no momento. " +
               "A nota permanece aberta e nenhum saldo foi alterado. Tente novamente.")
    {
        Causa = causa;
    }

    public Exception? Causa { get; }
}

/// <summary>
/// O Estoque respondeu, mas recusou a baixa por falta de saldo.
/// Vira HTTP 422 com o detalhamento produto a produto.
/// </summary>
public sealed class SaldoInsuficienteNoEstoqueExcecao : ExcecaoDeDominio
{
    public SaldoInsuficienteNoEstoqueExcecao(IReadOnlyList<FaltaDeSaldo> faltas, string? detalhe = null)
        : base("SALDO_INSUFICIENTE",
               detalhe ?? "Nao ha saldo suficiente para imprimir esta nota.")
    {
        Faltas = faltas;
    }

    public IReadOnlyList<FaltaDeSaldo> Faltas { get; }
}

/// <summary>
/// Duas notas disputaram o mesmo saldo e esta perdeu, mesmo apos as novas
/// tentativas do lado do Estoque. Vira HTTP 409.
/// </summary>
public sealed class ConflitoDeConcorrenciaExcecao : ExcecaoDeDominio
{
    public ConflitoDeConcorrenciaExcecao()
        : base("CONFLITO_CONCORRENCIA",
               "Outra operacao alterou o saldo destes produtos ao mesmo tempo. " +
               "A nota permanece aberta. Tente novamente.") { }
}

/// <summary>A baixa foi confirmada, mas o PDF nao pode ser gerado.</summary>
public sealed class FalhaGeracaoPdfExcecao : ExcecaoDeDominio
{
    public FalhaGeracaoPdfExcecao(Exception causa)
        : base("FALHA_GERACAO_PDF",
               "Nao foi possivel gerar o PDF da nota. " +
               "O saldo baixado foi devolvido ao estoque e a nota permanece aberta.")
    {
        Causa = causa;
    }

    public Exception Causa { get; }
}

/// <summary>
/// O pior cenario: a baixa foi confirmada, o PDF falhou e o estorno tambem
/// falhou. O sistema esta inconsistente e nenhuma acao automatica resolve.
///
/// A nota permanece em EmProcessamento de proposito, como marcador de que
/// existe uma operacao pendente de resolucao humana. Reconhecer que nenhuma
/// compensacao e infalivel e mais honesto do que fingir que este caso nao
/// existe.
/// </summary>
public sealed class IntervencaoManualNecessariaExcecao : ExcecaoDeDominio
{
    public IntervencaoManualNecessariaExcecao(Guid notaId, long numero)
        : base("INTERVENCAO_MANUAL",
               $"A nota {numero} ficou em estado inconsistente: o saldo foi baixado " +
               "e nao foi possivel devolve-lo automaticamente. " +
               "E necessaria verificacao manual do estoque.")
    {
        NotaId = notaId;
        Numero = numero;
    }

    public Guid NotaId { get; }
    public long Numero { get; }
}
