using Korp.Estoque.Application.Contratos;
using Korp.Estoque.Domain.Entidades;
using Microsoft.EntityFrameworkCore;

namespace Korp.Estoque.Infrastructure.Persistencia;

/// <summary>
/// Implementacao do repositorio sobre Entity Framework Core.
///
/// Todas as consultas sao escritas em LINQ e traduzidas para SQL pelo provider
/// do Npgsql. Nenhuma delas materializa a tabela inteira em memoria: filtro,
/// ordenacao, contagem e paginacao acontecem no banco.
/// </summary>
public class ProdutoRepositorio : IProdutoRepositorio
{
    private readonly EstoqueDbContext _contexto;

    public ProdutoRepositorio(EstoqueDbContext contexto) => _contexto = contexto;

    public Task<Produto?> ObterPorIdAsync(Guid id, CancellationToken ct = default) =>
        _contexto.Produtos.FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<Produto?> ObterPorCodigoAsync(string codigo, CancellationToken ct = default) =>
        _contexto.Produtos.FirstOrDefaultAsync(p => p.Codigo == codigo, ct);

    public async Task<IReadOnlyList<Produto>> ObterPorIdsAsync(
        IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var lista = ids.Distinct().ToList();

        // Uma unica consulta com WHERE id = ANY(...), em vez de uma consulta
        // por produto. Com uma nota de 20 itens, a diferenca e 1 ida ao banco
        // contra 20.
        return await _contexto.Produtos
            .Where(p => lista.Contains(p.Id))
            .ToListAsync(ct);
    }

    public async Task<(IReadOnlyList<Produto> Itens, int Total)> ListarAsync(
        string? busca, int pagina, int tamanho, CancellationToken ct = default)
    {
        // AsNoTracking porque listagem e leitura pura: nao precisa do overhead
        // do change tracker para entidades que ninguem vai alterar.
        var consulta = _contexto.Produtos.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(busca))
        {
            var termo = $"%{busca.Trim()}%";

            // EF.Functions.ILike vira ILIKE do PostgreSQL, que e case
            // insensitive nativamente. Fazer ToLower() dos dois lados
            // funcionaria, mas impediria o banco de usar indice.
            consulta = consulta.Where(p =>
                EF.Functions.ILike(p.Codigo, termo) ||
                EF.Functions.ILike(p.Descricao, termo));
        }

        var total = await consulta.CountAsync(ct);

        var itens = await consulta
            .OrderBy(p => p.Codigo)
            .Skip((pagina - 1) * tamanho)
            .Take(tamanho)
            .ToListAsync(ct);

        return (itens, total);
    }

    public Task<bool> ExisteComCodigoAsync(string codigo, CancellationToken ct = default) =>
        _contexto.Produtos.AnyAsync(p => p.Codigo == codigo, ct);

    public Task<bool> PossuiMovimentacaoAsync(Guid produtoId, CancellationToken ct = default) =>
        _contexto.Movimentacoes.AnyAsync(m => m.ProdutoId == produtoId, ct);

    public void Adicionar(Produto produto) => _contexto.Produtos.Add(produto);

    public void Remover(Produto produto) => _contexto.Produtos.Remove(produto);
}
