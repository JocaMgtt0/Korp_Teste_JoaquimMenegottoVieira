namespace Korp.Estoque.Domain.Excecoes;

/// <summary>
/// Base de todas as violacoes de regra de negocio deste servico.
///
/// Existe para que a camada de API consiga distinguir "o usuario fez algo
/// invalido" de "o sistema quebrou". A primeira vira 4xx com mensagem util,
/// a segunda vira 500 sem vazar detalhe interno.
///
/// O <see cref="Codigo"/> e um identificador estavel que o frontend pode
/// tratar sem depender do texto da mensagem, que muda com o idioma.
/// </summary>
public abstract class ExcecaoDeDominio : Exception
{
    protected ExcecaoDeDominio(string codigo, string mensagem) : base(mensagem)
    {
        Codigo = codigo;
    }

    public string Codigo { get; }
}

/// <summary>Dados de entrada que violam invariantes da entidade.</summary>
public sealed class DadosInvalidosExcecao : ExcecaoDeDominio
{
    public DadosInvalidosExcecao(string mensagem)
        : base("DADOS_INVALIDOS", mensagem) { }
}

/// <summary>Violacao da RN01: codigo de produto e unico no sistema.</summary>
public sealed class CodigoDuplicadoExcecao : ExcecaoDeDominio
{
    public CodigoDuplicadoExcecao(string codigo)
        : base("PRODUTO_CODIGO_DUPLICADO",
               $"Ja existe um produto cadastrado com o codigo '{codigo}'.")
    {
        CodigoProduto = codigo;
    }

    public string CodigoProduto { get; }
}

public sealed class ProdutoNaoEncontradoExcecao : ExcecaoDeDominio
{
    public ProdutoNaoEncontradoExcecao(Guid id)
        : base("PRODUTO_NAO_ENCONTRADO",
               $"Produto {id} nao encontrado.") { }
}

/// <summary>Violacao da RN09: produto ja usado em nota nao pode ser excluido.</summary>
public sealed class ProdutoEmUsoExcecao : ExcecaoDeDominio
{
    public ProdutoEmUsoExcecao(string codigo)
        : base("PRODUTO_EM_USO",
               $"O produto '{codigo}' ja foi utilizado em notas fiscais e nao pode ser excluido.") { }
}

/// <summary>Violacao da RN03: quantidade precisa ser inteiro maior que zero.</summary>
public sealed class QuantidadeInvalidaExcecao : ExcecaoDeDominio
{
    public QuantidadeInvalidaExcecao(int quantidade)
        : base("QUANTIDADE_INVALIDA",
               $"A quantidade deve ser maior que zero. Recebido: {quantidade}.") { }
}

/// <summary>
/// Violacao da RN02: o saldo de um produto nunca fica negativo.
///
/// Carrega os numeros envolvidos porque a API precisa devolver ao usuario
/// exatamente qual produto faltou e quanto faltou, nao apenas "erro".
/// </summary>
public sealed class SaldoInsuficienteExcecao : ExcecaoDeDominio
{
    public SaldoInsuficienteExcecao(string codigoProduto, int saldoDisponivel, int quantidadeSolicitada)
        : base("SALDO_INSUFICIENTE",
               $"O produto '{codigoProduto}' possui saldo {saldoDisponivel} " +
               $"e a operacao requer {quantidadeSolicitada} unidades.")
    {
        CodigoProduto = codigoProduto;
        SaldoDisponivel = saldoDisponivel;
        QuantidadeSolicitada = quantidadeSolicitada;
    }

    public string CodigoProduto { get; }
    public int SaldoDisponivel { get; }
    public int QuantidadeSolicitada { get; }
}
