namespace Korp.Faturamento.Domain.Excecoes;

/// <summary>
/// Base de todas as violacoes de regra de negocio deste servico.
/// O <see cref="Codigo"/> e um identificador estavel que o frontend trata
/// sem depender do texto da mensagem.
/// </summary>
public abstract class ExcecaoDeDominio : Exception
{
    protected ExcecaoDeDominio(string codigo, string mensagem) : base(mensagem)
    {
        Codigo = codigo;
    }

    public string Codigo { get; }
}

public sealed class DadosInvalidosExcecao : ExcecaoDeDominio
{
    public DadosInvalidosExcecao(string mensagem)
        : base("DADOS_INVALIDOS", mensagem) { }
}

public sealed class NotaNaoEncontradaExcecao : ExcecaoDeDominio
{
    public NotaNaoEncontradaExcecao(Guid id)
        : base("NOTA_NAO_ENCONTRADA", $"Nota fiscal {id} nao encontrada.") { }
}

public sealed class ItemNaoEncontradoExcecao : ExcecaoDeDominio
{
    public ItemNaoEncontradoExcecao(Guid itemId)
        : base("ITEM_NAO_ENCONTRADO", $"Item {itemId} nao encontrado nesta nota.") { }
}

/// <summary>
/// Violacao das RN06 e RN07: transicao de estado que a maquina nao permite.
///
/// Cobre tanto "nota Fechada nao pode ser editada" quanto
/// "so nota Aberta pode ser impressa".
/// </summary>
public sealed class StatusInvalidoExcecao : ExcecaoDeDominio
{
    public StatusInvalidoExcecao(string operacao, string statusAtual)
        : base("NOTA_STATUS_INVALIDO",
               $"Nao e permitido {operacao} uma nota com status {statusAtual}.")
    {
        StatusAtual = statusAtual;
    }

    public string StatusAtual { get; }
}

/// <summary>Violacao da RN08: nota sem itens nao pode ser impressa.</summary>
public sealed class NotaSemItensExcecao : ExcecaoDeDominio
{
    public NotaSemItensExcecao(long numero)
        : base("NOTA_SEM_ITENS",
               $"A nota {numero} nao possui itens e nao pode ser impressa.") { }
}

/// <summary>Violacao da RN03: quantidade precisa ser inteiro maior que zero.</summary>
public sealed class QuantidadeInvalidaExcecao : ExcecaoDeDominio
{
    public QuantidadeInvalidaExcecao(int quantidade)
        : base("QUANTIDADE_INVALIDA",
               $"A quantidade deve ser maior que zero. Recebido: {quantidade}.") { }
}
