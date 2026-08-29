using Korp.Estoque.Application.Contratos;
using Korp.Estoque.Application.Dtos;
using Korp.Estoque.Domain.Entidades;
using Korp.Estoque.Domain.Excecoes;

namespace Korp.Estoque.Application.Servicos;

/// <summary>
/// Casos de uso de cadastro de produtos.
///
/// A regra de negocio propriamente dita vive na entidade <see cref="Produto"/>.
/// O que este servico faz e orquestrar: buscar, verificar unicidade contra o
/// banco (algo que a entidade sozinha nao tem como saber) e gravar.
/// </summary>
public class ServicoDeProdutos
{
    private readonly IProdutoRepositorio _repositorio;
    private readonly IUnidadeDeTrabalho _unidadeDeTrabalho;

    public ServicoDeProdutos(IProdutoRepositorio repositorio, IUnidadeDeTrabalho unidadeDeTrabalho)
    {
        _repositorio = repositorio;
        _unidadeDeTrabalho = unidadeDeTrabalho;
    }

    public async Task<ResultadoPaginado<ProdutoDto>> ListarAsync(
        string? busca, int pagina, int tamanho, CancellationToken ct = default)
    {
        pagina = pagina < 1 ? 1 : pagina;
        tamanho = tamanho is < 1 or > 100 ? 20 : tamanho;

        var (itens, total) = await _repositorio.ListarAsync(busca, pagina, tamanho, ct);

        // LINQ em memoria sobre o resultado ja materializado, apenas para projetar
        // as entidades em DTOs. A filtragem e a paginacao foram feitas no banco.
        var dtos = itens.Select(Projetar).ToList();

        return new ResultadoPaginado<ProdutoDto>(dtos, total, pagina, tamanho);
    }

    public async Task<ProdutoDto> ObterPorIdAsync(Guid id, CancellationToken ct = default)
    {
        var produto = await _repositorio.ObterPorIdAsync(id, ct)
                      ?? throw new ProdutoNaoEncontradoExcecao(id);

        return Projetar(produto);
    }

    public async Task<ProdutoDto> CriarAsync(CriarProdutoDto dto, CancellationToken ct = default)
    {
        var codigo = (dto.Codigo ?? string.Empty).Trim();

        // RN01. A unicidade nao cabe na entidade porque depende do conjunto
        // inteiro de produtos, que so o repositorio conhece. Existe tambem
        // um indice unico no banco como rede de seguranca contra corrida.
        if (await _repositorio.ExisteComCodigoAsync(codigo, ct))
            throw new CodigoDuplicadoExcecao(codigo);

        var produto = Produto.Criar(codigo, dto.Descricao, dto.Saldo);

        _repositorio.Adicionar(produto);
        await _unidadeDeTrabalho.SalvarAsync(ct);

        return Projetar(produto);
    }

    public async Task<ProdutoDto> AtualizarAsync(
        Guid id, AtualizarProdutoDto dto, CancellationToken ct = default)
    {
        var produto = await _repositorio.ObterPorIdAsync(id, ct)
                      ?? throw new ProdutoNaoEncontradoExcecao(id);

        produto.AlterarDescricao(dto.Descricao);
        produto.AjustarSaldo(dto.Saldo);

        await _unidadeDeTrabalho.SalvarAsync(ct);

        return Projetar(produto);
    }

    public async Task ExcluirAsync(Guid id, CancellationToken ct = default)
    {
        var produto = await _repositorio.ObterPorIdAsync(id, ct)
                      ?? throw new ProdutoNaoEncontradoExcecao(id);

        // RN09: produto que ja participou de nota fiscal tem historico
        // e nao pode desaparecer.
        if (await _repositorio.PossuiMovimentacaoAsync(id, ct))
            throw new ProdutoEmUsoExcecao(produto.Codigo);

        _repositorio.Remover(produto);
        await _unidadeDeTrabalho.SalvarAsync(ct);
    }

    /// <summary>
    /// Consulta de saldo usada pelo frontend antes de incluir um item na nota.
    /// E a metade "feedback rapido" da RN12: a validacao definitiva acontece
    /// no momento da impressao, dentro da transacao de baixa.
    /// </summary>
    public async Task<IReadOnlyList<ProdutoDto>> ConsultarSaldoAsync(
        IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var produtos = await _repositorio.ObterPorIdsAsync(ids, ct);
        return produtos.Select(Projetar).ToList();
    }

    private static ProdutoDto Projetar(Produto p) =>
        new(p.Id, p.Codigo, p.Descricao, p.Saldo, p.CriadoEm, p.AtualizadoEm);
}
