using Korp.Faturamento.Domain.Excecoes;

namespace Korp.Faturamento.Domain.Entidades;

/// <summary>
/// Linha de uma nota fiscal.
///
/// Guarda o identificador do produto e tambem uma **copia** do codigo e da
/// descricao no momento da inclusao (RN11). Isso nao e redundancia: e o que
/// mantem os dois servicos desacoplados de verdade.
///
/// Dois motivos:
///
/// 1. O Faturamento consegue exibir e imprimir a nota inteira mesmo com o
///    servico de Estoque fora do ar. Se guardasse so o identificador e
///    precisasse buscar a descricao a cada leitura, a independencia entre os
///    servicos seria apenas aparente.
///
/// 2. Documento fiscal registra o que foi vendido naquele dia. Se a descricao
///    do produto mudar no cadastro amanha, a nota emitida ontem precisa
///    continuar mostrando o texto de ontem.
///
/// Este padrao tem nome: projecao local (ou snapshot) de dados de outro
/// servico, e e comum em sistemas distribuidos reais.
/// </summary>
public class ItemNotaFiscal
{
    private ItemNotaFiscal() { }

    internal ItemNotaFiscal(
        Guid notaFiscalId, Guid produtoId, string produtoCodigo,
        string produtoDescricao, int quantidade)
    {
        if (quantidade <= 0)
            throw new QuantidadeInvalidaExcecao(quantidade);

        if (string.IsNullOrWhiteSpace(produtoCodigo))
            throw new DadosInvalidosExcecao("O codigo do produto e obrigatorio no item.");

        Id = Guid.NewGuid();
        NotaFiscalId = notaFiscalId;
        ProdutoId = produtoId;
        ProdutoCodigo = produtoCodigo.Trim();
        ProdutoDescricao = (produtoDescricao ?? string.Empty).Trim();
        Quantidade = quantidade;
    }

    public Guid Id { get; private set; }
    public Guid NotaFiscalId { get; private set; }
    public Guid ProdutoId { get; private set; }
    public string ProdutoCodigo { get; private set; } = null!;
    public string ProdutoDescricao { get; private set; } = null!;
    public int Quantidade { get; private set; }

    internal void AlterarQuantidade(int quantidade)
    {
        if (quantidade <= 0)
            throw new QuantidadeInvalidaExcecao(quantidade);

        Quantidade = quantidade;
    }

    internal void SomarQuantidade(int quantidade)
    {
        if (quantidade <= 0)
            throw new QuantidadeInvalidaExcecao(quantidade);

        Quantidade += quantidade;
    }
}
