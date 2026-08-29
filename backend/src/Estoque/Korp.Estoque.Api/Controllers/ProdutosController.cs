using Korp.Estoque.Application.Dtos;
using Korp.Estoque.Application.Servicos;
using Microsoft.AspNetCore.Mvc;

namespace Korp.Estoque.Api.Controllers;

/// <summary>
/// Cadastro de produtos.
///
/// Sem try/catch em lugar nenhum: violacao de regra vira excecao tipada no
/// dominio e o ManipuladorGlobalDeExcecoes traduz para o HTTP correto.
/// O controller so recebe, delega e devolve.
/// </summary>
[ApiController]
[Route("api/produtos")]
[Produces("application/json")]
public class ProdutosController : ControllerBase
{
    private readonly ServicoDeProdutos _servico;

    public ProdutosController(ServicoDeProdutos servico) => _servico = servico;

    /// <summary>Lista produtos, com busca por codigo ou descricao.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ResultadoPaginado<ProdutoDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ResultadoPaginado<ProdutoDto>>> Listar(
        [FromQuery] string? busca,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamanho = 20,
        CancellationToken ct = default) =>
        Ok(await _servico.ListarAsync(busca, pagina, tamanho, ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ProdutoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProdutoDto>> ObterPorId(Guid id, CancellationToken ct) =>
        Ok(await _servico.ObterPorIdAsync(id, ct));

    [HttpPost]
    [ProducesResponseType(typeof(ProdutoDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProdutoDto>> Criar(
        [FromBody] CriarProdutoDto dto, CancellationToken ct)
    {
        var criado = await _servico.CriarAsync(dto, ct);
        return CreatedAtAction(nameof(ObterPorId), new { id = criado.Id }, criado);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ProdutoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProdutoDto>> Atualizar(
        Guid id, [FromBody] AtualizarProdutoDto dto, CancellationToken ct) =>
        Ok(await _servico.AtualizarAsync(id, dto, ct));

    /// <summary>Exclui um produto que nunca foi usado em nota fiscal (RN09).</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Excluir(Guid id, CancellationToken ct)
    {
        await _servico.ExcluirAsync(id, ct);
        return NoContent();
    }

    /// <summary>
    /// Consulta o saldo de varios produtos de uma vez.
    ///
    /// Usado pelo Faturamento e pelo frontend antes de incluir item na nota.
    /// E a metade "feedback rapido" da RN12: a validacao que vale e a da
    /// impressao, feita dentro da transacao de baixa.
    /// </summary>
    [HttpPost("consultar-saldo")]
    [ProducesResponseType(typeof(IReadOnlyList<ProdutoDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ProdutoDto>>> ConsultarSaldo(
        [FromBody] IReadOnlyList<Guid> produtoIds, CancellationToken ct) =>
        Ok(await _servico.ConsultarSaldoAsync(produtoIds, ct));
}
