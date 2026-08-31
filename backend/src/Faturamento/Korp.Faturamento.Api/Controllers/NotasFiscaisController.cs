using Korp.Faturamento.Application.Dtos;
using Korp.Faturamento.Application.Servicos;
using Microsoft.AspNetCore.Mvc;

namespace Korp.Faturamento.Api.Controllers;

[ApiController]
[Route("api/notas")]
[Produces("application/json")]
public class NotasFiscaisController : ControllerBase
{
    private readonly ServicoDeNotas _servicoDeNotas;
    private readonly ServicoDeImpressao _servicoDeImpressao;

    public NotasFiscaisController(
        ServicoDeNotas servicoDeNotas, ServicoDeImpressao servicoDeImpressao)
    {
        _servicoDeNotas = servicoDeNotas;
        _servicoDeImpressao = servicoDeImpressao;
    }

    /// <summary>Lista notas, opcionalmente filtrando por status.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ResultadoPaginado<NotaFiscalResumoDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ResultadoPaginado<NotaFiscalResumoDto>>> Listar(
        [FromQuery] string? status,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamanho = 20,
        CancellationToken ct = default) =>
        Ok(await _servicoDeNotas.ListarAsync(status, pagina, tamanho, ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(NotaFiscalDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NotaFiscalDto>> ObterPorId(Guid id, CancellationToken ct) =>
        Ok(await _servicoDeNotas.ObterPorIdAsync(id, ct));

    /// <summary>Cria uma nota Aberta com o proximo numero sequencial.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(NotaFiscalDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<NotaFiscalDto>> Criar(CancellationToken ct)
    {
        var nota = await _servicoDeNotas.CriarAsync(ct);
        return CreatedAtAction(nameof(ObterPorId), new { id = nota.Id }, nota);
    }

    /// <summary>Exclui uma nota Aberta. Nota Fechada e documento emitido (RN07).</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Excluir(Guid id, CancellationToken ct)
    {
        await _servicoDeNotas.ExcluirAsync(id, ct);
        return NoContent();
    }

    // ---------- Itens ----------

    [HttpPost("{id:guid}/itens")]
    [ProducesResponseType(typeof(NotaFiscalDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<NotaFiscalDto>> AdicionarItem(
        Guid id, [FromBody] AdicionarItemDto dto, CancellationToken ct) =>
        Ok(await _servicoDeNotas.AdicionarItemAsync(id, dto, ct));

    [HttpPut("{id:guid}/itens/{itemId:guid}")]
    [ProducesResponseType(typeof(NotaFiscalDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NotaFiscalDto>> AlterarQuantidade(
        Guid id, Guid itemId, [FromBody] AlterarQuantidadeDto dto, CancellationToken ct) =>
        Ok(await _servicoDeNotas.AlterarQuantidadeItemAsync(id, itemId, dto, ct));

    [HttpDelete("{id:guid}/itens/{itemId:guid}")]
    [ProducesResponseType(typeof(NotaFiscalDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NotaFiscalDto>> RemoverItem(
        Guid id, Guid itemId, CancellationToken ct) =>
        Ok(await _servicoDeNotas.RemoverItemAsync(id, itemId, ct));

    // ---------- Impressao ----------

    /// <summary>
    /// Imprime a nota: da baixa no estoque, gera o PDF e fecha a nota.
    ///
    /// Esta e a operacao distribuida do sistema. Os codigos de erro possiveis
    /// dizem ao frontend o que fazer:
    ///
    ///   422 SALDO_INSUFICIENTE     nao adianta repetir
    ///   409 CONFLITO_CONCORRENCIA  vale tentar de novo
    ///   409 NOTA_STATUS_INVALIDO   a nota nao esta Aberta
    ///   503 ESTOQUE_INDISPONIVEL   vale tentar de novo mais tarde
    ///
    /// Em todos eles a nota volta para Aberta e nenhum saldo fica alterado.
    /// </summary>
    [HttpPost("{id:guid}/imprimir")]
    [ProducesResponseType(typeof(NotaFiscalDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<NotaFiscalDto>> Imprimir(Guid id, CancellationToken ct)
    {
        await _servicoDeImpressao.ImprimirAsync(id, ct);

        // Devolve a nota atualizada, e nao o PDF, para o frontend poder
        // atualizar a tela com o novo status. O download do arquivo e feito
        // em seguida pelo endpoint /pdf.
        return Ok(await _servicoDeNotas.ObterPorIdAsync(id, ct));
    }

    /// <summary>Baixa o PDF de uma nota fechada. Gerado sob demanda.</summary>
    [HttpGet("{id:guid}/pdf")]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> BaixarPdf(Guid id, CancellationToken ct)
    {
        var resultado = await _servicoDeImpressao.ObterPdfAsync(id, ct);

        return File(resultado.Pdf, "application/pdf", $"nota-{resultado.Numero:D6}.pdf");
    }
}
