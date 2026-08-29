using Korp.Estoque.Application.Dtos;
using Korp.Estoque.Application.Servicos;
using Microsoft.AspNetCore.Mvc;

namespace Korp.Estoque.Api.Controllers;

/// <summary>
/// Baixa e estorno de saldo. Estes dois endpoints existem para o servico de
/// Faturamento, nao para o usuario final.
///
/// Sao o unico caminho pelo qual outro servico altera o estoque, e e por isso
/// que o Estoque continua sendo o dono exclusivo do saldo mesmo participando
/// de uma operacao que comeca em outro lugar.
/// </summary>
[ApiController]
[Route("api/produtos")]
[Produces("application/json")]
public class MovimentacoesController : ControllerBase
{
    private readonly ServicoDeMovimentacao _servico;

    public MovimentacoesController(ServicoDeMovimentacao servico) => _servico = servico;

    /// <summary>
    /// Da baixa no saldo dos produtos de uma nota fiscal.
    ///
    /// Atomica (RN10): ou todos os itens baixam, ou nenhum.
    /// Retorna 422 se faltar saldo em qualquer item, e 409 se houver conflito
    /// de concorrencia que nao se resolveu apos as novas tentativas.
    /// </summary>
    [HttpPost("baixa")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Baixar(
        [FromBody] MovimentacaoEstoqueDto dto, CancellationToken ct)
    {
        await _servico.BaixarAsync(dto, ct);
        return NoContent();
    }

    /// <summary>
    /// Devolve ao estoque o que uma nota havia baixado.
    ///
    /// E a compensacao acionada pelo Faturamento quando a impressao falha
    /// depois que a baixa ja foi confirmada. Como os dois servicos tem bancos
    /// separados, nao existe transacao distribuida: desfazer e a unica saida.
    /// </summary>
    [HttpPost("estorno")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Estornar(
        [FromBody] MovimentacaoEstoqueDto dto, CancellationToken ct)
    {
        await _servico.EstornarAsync(dto, ct);
        return NoContent();
    }
}
