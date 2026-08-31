using Korp.Faturamento.Application.Contratos;
using Korp.Faturamento.Application.Dtos;
using Korp.Faturamento.Application.Excecoes;
using Korp.Faturamento.Domain.Entidades;
using Korp.Faturamento.Domain.Excecoes;

namespace Korp.Faturamento.Application.Servicos;

/// <summary>
/// Casos de uso de cadastro e edicao de notas fiscais.
///
/// A regra de negocio vive na entidade <see cref="NotaFiscal"/>. Este servico
/// orquestra: reserva o numero na sequence, busca os dados do produto no
/// servico de Estoque e grava.
/// </summary>
public class ServicoDeNotas
{
    private readonly IRepositorioDeNotas _repositorio;
    private readonly IUnidadeDeTrabalho _unidadeDeTrabalho;
    private readonly IServicoDeEstoque _estoque;

    public ServicoDeNotas(
        IRepositorioDeNotas repositorio,
        IUnidadeDeTrabalho unidadeDeTrabalho,
        IServicoDeEstoque estoque)
    {
        _repositorio = repositorio;
        _unidadeDeTrabalho = unidadeDeTrabalho;
        _estoque = estoque;
    }

    public async Task<ResultadoPaginado<NotaFiscalResumoDto>> ListarAsync(
        string? status, int pagina, int tamanho, CancellationToken ct = default)
    {
        pagina = pagina < 1 ? 1 : pagina;
        tamanho = tamanho is < 1 or > 100 ? 20 : tamanho;

        StatusNotaFiscal? filtro = null;

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<StatusNotaFiscal>(status, ignoreCase: true, out var convertido))
                throw new DadosInvalidosExcecao($"Status '{status}' invalido.");

            filtro = convertido;
        }

        var (notas, total) = await _repositorio.ListarAsync(filtro, pagina, tamanho, ct);

        var dtos = notas
            .Select(n => new NotaFiscalResumoDto(
                n.Id,
                n.Numero,
                n.Status.ToString(),
                n.CriadaEm,
                n.FechadaEm,
                n.Itens.Count,
                n.Itens.Sum(i => i.Quantidade)))
            .ToList();

        return new ResultadoPaginado<NotaFiscalResumoDto>(dtos, total, pagina, tamanho);
    }

    public async Task<NotaFiscalDto> ObterPorIdAsync(Guid id, CancellationToken ct = default)
    {
        var nota = await BuscarAsync(id, ct);
        return Projetar(nota);
    }

    /// <summary>
    /// Cria uma nota Aberta com o proximo numero da sequence (RN04).
    /// </summary>
    public async Task<NotaFiscalDto> CriarAsync(CancellationToken ct = default)
    {
        var numero = await _repositorio.ProximoNumeroAsync(ct);
        var nota = NotaFiscal.Criar(numero);

        _repositorio.Adicionar(nota);
        await _unidadeDeTrabalho.SalvarAsync(ct);

        return Projetar(nota);
    }

    public async Task ExcluirAsync(Guid id, CancellationToken ct = default)
    {
        var nota = await BuscarAsync(id, ct);

        // RN07: so nota Aberta pode ser excluida. Fechada e documento emitido.
        nota.GarantirQuePodeSerExcluida();

        _repositorio.Remover(nota);
        await _unidadeDeTrabalho.SalvarAsync(ct);
    }

    /// <summary>
    /// Inclui um produto na nota.
    ///
    /// Consulta o servico de Estoque por dois motivos: obter codigo e descricao
    /// para gravar o snapshot no item (RN11), e conferir se ha saldo agora
    /// (RN12, metade "feedback rapido"). A validacao que realmente decide e a
    /// da impressao, feita dentro da transacao de baixa.
    /// </summary>
    public async Task<NotaFiscalDto> AdicionarItemAsync(
        Guid notaId, AdicionarItemDto dto, CancellationToken ct = default)
    {
        if (dto.Quantidade <= 0)
            throw new QuantidadeInvalidaExcecao(dto.Quantidade);

        var nota = await BuscarAsync(notaId, ct);

        var saldos = await _estoque.ConsultarSaldoAsync(new[] { dto.ProdutoId }, ct);
        var produto = saldos.FirstOrDefault(p => p.Id == dto.ProdutoId)
                      ?? throw new DadosInvalidosExcecao(
                          $"Produto {dto.ProdutoId} nao encontrado no servico de Estoque.");

        // Considera o que ja esta na nota: incluir 3 quando ja ha 4 exige 7
        // de saldo, nao 3. Sem isso, sucessivas inclusoes passariam uma a uma
        // e so estourariam na impressao.
        var jaNaNota = nota.Itens
            .Where(i => i.ProdutoId == dto.ProdutoId)
            .Sum(i => i.Quantidade);

        var totalPretendido = jaNaNota + dto.Quantidade;

        if (totalPretendido > produto.Saldo)
        {
            throw new SaldoInsuficienteNoEstoqueExcecao(
                new[] { new FaltaDeSaldo(produto.Codigo, produto.Saldo, totalPretendido) },
                $"O produto '{produto.Codigo}' possui saldo {produto.Saldo} " +
                $"e a nota passaria a exigir {totalPretendido} unidades.");
        }

        nota.AdicionarItem(dto.ProdutoId, produto.Codigo, produto.Descricao, dto.Quantidade);
        await _unidadeDeTrabalho.SalvarAsync(ct);

        return Projetar(nota);
    }

    public async Task<NotaFiscalDto> AlterarQuantidadeItemAsync(
        Guid notaId, Guid itemId, AlterarQuantidadeDto dto, CancellationToken ct = default)
    {
        var nota = await BuscarAsync(notaId, ct);

        nota.AlterarQuantidadeItem(itemId, dto.Quantidade);
        await _unidadeDeTrabalho.SalvarAsync(ct);

        return Projetar(nota);
    }

    public async Task<NotaFiscalDto> RemoverItemAsync(
        Guid notaId, Guid itemId, CancellationToken ct = default)
    {
        var nota = await BuscarAsync(notaId, ct);

        nota.RemoverItem(itemId);
        await _unidadeDeTrabalho.SalvarAsync(ct);

        return Projetar(nota);
    }

    private async Task<NotaFiscal> BuscarAsync(Guid id, CancellationToken ct) =>
        await _repositorio.ObterPorIdAsync(id, ct)
        ?? throw new NotaNaoEncontradaExcecao(id);

    internal static NotaFiscalDto Projetar(NotaFiscal nota) =>
        new(nota.Id,
            nota.Numero,
            nota.Status.ToString(),
            nota.CriadaEm,
            nota.FechadaEm,
            nota.Itens
                .Select(i => new ItemNotaFiscalDto(
                    i.Id, i.ProdutoId, i.ProdutoCodigo, i.ProdutoDescricao, i.Quantidade))
                .ToList());
}
