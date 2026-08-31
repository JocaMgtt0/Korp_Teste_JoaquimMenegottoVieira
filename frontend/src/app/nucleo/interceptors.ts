import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { ErroApi } from './modelos';
import { NotificacaoService } from './notificacao.service';

/**
 * Anexa um identificador de correlacao a toda requisicao.
 *
 * O mesmo valor atravessa o Angular, o Faturamento e o Estoque, e aparece no
 * log estruturado dos dois servicos. Quando o usuario relata "deu erro ao
 * imprimir a nota 7", esse identificador liga a tela a linha exata do log,
 * em vez de obrigar a caçar por horario.
 */
export const correlacaoInterceptor: HttpInterceptorFn = (requisicao, proximo) => {
  const correlacao = crypto.randomUUID();

  return proximo(requisicao.clone({
    setHeaders: { 'X-Correlation-Id': correlacao }
  }));
};

/**
 * Tratamento global de erro HTTP.
 *
 * Uso de catchError: um unico ponto converte qualquer falha de rede ou
 * resposta ProblemDetails do backend em ErroApi, exibe a mensagem ao usuario
 * e repassa o erro adiante para quem quiser reagir de forma especifica.
 *
 * Sem isso, cada componente precisaria repetir a mesma logica de tratamento,
 * e a mensagem exibida variaria de tela para tela.
 */
export const erroInterceptor: HttpInterceptorFn = (requisicao, proximo) => {
  const notificacao = inject(NotificacaoService);

  return proximo(requisicao).pipe(
    catchError((resposta: HttpErrorResponse) => {
      const erro = normalizar(resposta);
      notificacao.erro(erro);
      return throwError(() => erro);
    })
  );
};

function normalizar(resposta: HttpErrorResponse): ErroApi {
  // status 0 significa que a requisicao nem chegou ao servidor: servico fora
  // do ar, CORS bloqueado ou rede indisponivel.
  if (resposta.status === 0) {
    return {
      codigo: 'SEM_CONEXAO',
      titulo: 'Sem conexao',
      detalhe: 'Nao foi possivel falar com o servidor. Verifique se os servicos estao no ar.',
      status: 0,
      podeTentarNovamente: true
    };
  }

  const corpo = resposta.error ?? {};

  return {
    codigo: corpo.codigo ?? 'ERRO_DESCONHECIDO',
    titulo: corpo.title ?? 'Erro',
    detalhe: corpo.detail ?? 'Ocorreu uma falha ao processar a requisicao.',
    status: resposta.status,
    faltas: corpo.faltas,
    // 503 e 409 sao transitorios por natureza: o servico pode voltar, ou a
    // outra operacao concorrente ja terminou. 422 nao: sem saldo continuara
    // sem saldo, e oferecer "tentar novamente" ali seria enganar o usuario.
    podeTentarNovamente: resposta.status === 503 || resposta.status === 409
  };
}
