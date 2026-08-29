using Korp.Estoque.Domain.Excecoes;

namespace Korp.Estoque.Domain.Entidades;

/// <summary>
/// Produto do catalogo e seu saldo em estoque.
///
/// Esta entidade e a dona exclusiva do saldo em todo o sistema. O servico de
/// Faturamento nunca escreve neste dado: ele pede uma baixa por HTTP e quem
/// decide se a operacao e valida e este agregado.
///
/// Todas as propriedades tem set privado e toda mudanca passa por um metodo
/// que valida as invariantes. Nao existe caminho para deixar um Produto em
/// estado invalido, nem a partir da camada de aplicacao.
/// </summary>
public class Produto
{
    public const int TamanhoMaximoCodigo = 50;
    public const int TamanhoMaximoDescricao = 200;

    private readonly List<MovimentacaoEstoque> _movimentacoes = new();

    /// <summary>Construtor exigido pelo EF Core para materializar do banco.</summary>
    private Produto() { }

    private Produto(string codigo, string descricao, int saldoInicial)
    {
        Id = Guid.NewGuid();
        Codigo = codigo;
        Descricao = descricao;
        Saldo = saldoInicial;
        Versao = 1;
        CriadoEm = DateTime.UtcNow;
        AtualizadoEm = CriadoEm;
    }

    public Guid Id { get; private set; }

    /// <summary>RN01: unico no sistema e imutavel apos a criacao.</summary>
    public string Codigo { get; private set; } = null!;

    public string Descricao { get; private set; } = null!;

    /// <summary>RN02: nunca fica negativo.</summary>
    public int Saldo { get; private set; }

    /// <summary>
    /// Token de concorrencia otimista. Incrementado a cada mudanca de estado.
    ///
    /// O EF Core inclui o valor original deste campo no WHERE do UPDATE. Se
    /// outra transacao alterou o produto no meio do caminho, o UPDATE afeta
    /// zero linhas e o EF lanca DbUpdateConcurrencyException. E assim que o
    /// cenario de duas notas disputando o mesmo saldo e resolvido sem lock
    /// pessimista no banco.
    /// </summary>
    public int Versao { get; private set; }

    public DateTime CriadoEm { get; private set; }
    public DateTime AtualizadoEm { get; private set; }

    public IReadOnlyCollection<MovimentacaoEstoque> Movimentacoes => _movimentacoes.AsReadOnly();

    public static Produto Criar(string codigo, string descricao, int saldoInicial)
    {
        codigo = (codigo ?? string.Empty).Trim();
        descricao = (descricao ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(codigo))
            throw new DadosInvalidosExcecao("O codigo do produto e obrigatorio.");

        if (codigo.Length > TamanhoMaximoCodigo)
            throw new DadosInvalidosExcecao(
                $"O codigo do produto deve ter no maximo {TamanhoMaximoCodigo} caracteres.");

        if (string.IsNullOrWhiteSpace(descricao))
            throw new DadosInvalidosExcecao("A descricao do produto e obrigatoria.");

        if (descricao.Length > TamanhoMaximoDescricao)
            throw new DadosInvalidosExcecao(
                $"A descricao do produto deve ter no maximo {TamanhoMaximoDescricao} caracteres.");

        if (saldoInicial < 0)
            throw new DadosInvalidosExcecao("O saldo inicial nao pode ser negativo.");

        return new Produto(codigo, descricao, saldoInicial);
    }

    public void AlterarDescricao(string descricao)
    {
        descricao = (descricao ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(descricao))
            throw new DadosInvalidosExcecao("A descricao do produto e obrigatoria.");

        if (descricao.Length > TamanhoMaximoDescricao)
            throw new DadosInvalidosExcecao(
                $"A descricao do produto deve ter no maximo {TamanhoMaximoDescricao} caracteres.");

        Descricao = descricao;
        RegistrarMudanca();
    }

    /// <summary>
    /// Ajuste manual de saldo, feito pela tela de cadastro.
    /// Nao gera movimentacao porque nao decorre de nota fiscal.
    /// </summary>
    public void AjustarSaldo(int novoSaldo)
    {
        if (novoSaldo < 0)
            throw new DadosInvalidosExcecao("O saldo nao pode ser negativo.");

        Saldo = novoSaldo;
        RegistrarMudanca();
    }

    /// <summary>
    /// Baixa provocada pela impressao de uma nota fiscal.
    /// RN02 e RN03 sao verificadas aqui, no agregado, e nao na camada de servico.
    /// </summary>
    public MovimentacaoEstoque Baixar(int quantidade, Guid notaId)
    {
        if (quantidade <= 0)
            throw new QuantidadeInvalidaExcecao(quantidade);

        if (quantidade > Saldo)
            throw new SaldoInsuficienteExcecao(Codigo, Saldo, quantidade);

        var saldoAnterior = Saldo;
        Saldo -= quantidade;
        RegistrarMudanca();

        var movimentacao = MovimentacaoEstoque.DeBaixa(Id, notaId, quantidade, saldoAnterior, Saldo);
        _movimentacoes.Add(movimentacao);
        return movimentacao;
    }

    /// <summary>
    /// Devolve ao estoque uma quantidade baixada anteriormente.
    ///
    /// E a compensacao usada quando a impressao falha depois que a baixa ja
    /// foi confirmada. Como os dois servicos tem bancos separados, nao existe
    /// transacao distribuida: desfazer o efeito e a unica saida.
    /// </summary>
    public MovimentacaoEstoque Estornar(int quantidade, Guid notaId)
    {
        if (quantidade <= 0)
            throw new QuantidadeInvalidaExcecao(quantidade);

        var saldoAnterior = Saldo;
        Saldo += quantidade;
        RegistrarMudanca();

        var movimentacao = MovimentacaoEstoque.DeEstorno(Id, notaId, quantidade, saldoAnterior, Saldo);
        _movimentacoes.Add(movimentacao);
        return movimentacao;
    }

    private void RegistrarMudanca()
    {
        Versao++;
        AtualizadoEm = DateTime.UtcNow;
    }
}
