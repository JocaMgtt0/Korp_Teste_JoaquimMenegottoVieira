using Korp.Faturamento.Application.Contratos;
using Korp.Faturamento.Domain.Entidades;
using Microsoft.EntityFrameworkCore;

namespace Korp.Faturamento.Infrastructure.Persistencia;

public class RepositorioDeNotas : IRepositorioDeNotas
{
    private readonly FaturamentoDbContext _contexto;

    public RepositorioDeNotas(FaturamentoDbContext contexto) => _contexto = contexto;

    public Task<NotaFiscal?> ObterPorIdAsync(Guid id, CancellationToken ct = default) =>
        _contexto.Notas
            .Include(n => n.Itens)
            .FirstOrDefaultAsync(n => n.Id == id, ct);

    public async Task<(IReadOnlyList<NotaFiscal> Itens, int Total)> ListarAsync(
        StatusNotaFiscal? status, int pagina, int tamanho, CancellationToken ct = default)
    {
        // Include mesmo na listagem porque o resumo mostra total de itens e
        // quantidade somada. Sem ele seriam N consultas extras, uma por nota.
        var consulta = _contexto.Notas
            .AsNoTracking()
            .Include(n => n.Itens)
            .AsQueryable();

        if (status.HasValue)
            consulta = consulta.Where(n => n.Status == status.Value);

        var total = await consulta.CountAsync(ct);

        var itens = await consulta
            .OrderByDescending(n => n.Numero)
            .Skip((pagina - 1) * tamanho)
            .Take(tamanho)
            .ToListAsync(ct);

        return (itens, total);
    }

    /// <summary>
    /// Reserva o proximo numero na sequence do PostgreSQL (RN04).
    ///
    /// O alias "Value" nao e enfeite: SqlQuery&lt;T&gt; do EF Core 8 exige que
    /// a coluna escalar retornada tenha esse nome exato.
    ///
    /// Vale notar um efeito colateral aceito de proposito: sequence nao volta
    /// atras em rollback. Se a criacao da nota falhar depois de pegar o numero,
    /// aquele valor fica perdido e a numeracao pula. E o preco de nunca repetir
    /// numero, e repetir seria muito pior do que pular.
    /// </summary>
    public async Task<long> ProximoNumeroAsync(CancellationToken ct = default)
    {
        var sql = $"SELECT nextval('{FaturamentoDbContext.SequenceNumeroNota}') AS \"Value\"";

        return await _contexto.Database
            .SqlQueryRaw<long>(sql)
            .SingleAsync(ct);
    }

    public void Adicionar(NotaFiscal nota) => _contexto.Notas.Add(nota);

    public void Remover(NotaFiscal nota) => _contexto.Notas.Remove(nota);
}

public class UnidadeDeTrabalho : IUnidadeDeTrabalho
{
    private readonly FaturamentoDbContext _contexto;

    public UnidadeDeTrabalho(FaturamentoDbContext contexto) => _contexto = contexto;

    public Task<int> SalvarAsync(CancellationToken ct = default) =>
        _contexto.SaveChangesAsync(ct);
}
