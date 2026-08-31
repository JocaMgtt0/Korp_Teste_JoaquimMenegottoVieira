/**
 * Contratos das duas APIs.
 *
 * Espelham os DTOs do backend. Manter os nomes iguais aos do C# evita uma
 * camada de traducao que so existiria para renomear campo.
 */

export interface Produto {
  id: string;
  codigo: string;
  descricao: string;
  saldo: number;
  criadoEm: string;
  atualizadoEm: string;
}

export interface CriarProduto {
  codigo: string;
  descricao: string;
  saldo: number;
}

export interface AtualizarProduto {
  descricao: string;
  saldo: number;
}

export interface ResultadoPaginado<T> {
  itens: T[];
  total: number;
  pagina: number;
  tamanho: number;
  totalPaginas: number;
}

export type StatusNota = 'Aberta' | 'EmProcessamento' | 'Fechada';

export interface ItemNotaFiscal {
  id: string;
  produtoId: string;
  produtoCodigo: string;
  produtoDescricao: string;
  quantidade: number;
}

export interface NotaFiscal {
  id: string;
  numero: number;
  status: StatusNota;
  criadaEm: string;
  fechadaEm: string | null;
  itens: ItemNotaFiscal[];
  totalDeItens: number;
  quantidadeTotal: number;
}

export interface NotaFiscalResumo {
  id: string;
  numero: number;
  status: StatusNota;
  criadaEm: string;
  fechadaEm: string | null;
  totalDeItens: number;
  quantidadeTotal: number;
}

/** Produto que faltou saldo, detalhado pelo backend em respostas 422. */
export interface FaltaDeSaldo {
  produtoCodigo: string;
  saldoDisponivel: number;
  quantidadeSolicitada: number;
}

/**
 * Erro normalizado pelo interceptor.
 *
 * O backend responde sempre em ProblemDetails (RFC 7807) com um campo
 * "codigo" estavel. A tela trata pelo codigo, nunca pelo texto da mensagem,
 * que pode mudar.
 */
export interface ErroApi {
  codigo: string;
  titulo: string;
  detalhe: string;
  status: number;
  faltas?: FaltaDeSaldo[];
  /** Indica se faz sentido oferecer "tentar novamente" ao usuario. */
  podeTentarNovamente: boolean;
}
