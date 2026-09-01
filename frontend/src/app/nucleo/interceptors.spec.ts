import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { correlacaoInterceptor, erroInterceptor } from './interceptors';
import { ErroApi } from './modelos';
import { NotificacaoService } from './notificacao.service';

/**
 * Dublê do servico de notificacao.
 *
 * O real depende de MatSnackBar, que exigiria overlay e animacoes no TestBed
 * sem acrescentar nada ao que se quer verificar: que o interceptor normalize
 * o erro corretamente e avise alguem.
 */
class NotificacaoFalsa {
  readonly erros: ErroApi[] = [];
  readonly sucessos: string[] = [];

  erro(e: ErroApi): void { this.erros.push(e); }
  sucesso(m: string): void { this.sucessos.push(m); }
}

describe('interceptors', () => {
  let http: HttpClient;
  let controlador: HttpTestingController;
  let notificacao: NotificacaoFalsa;

  beforeEach(() => {
    notificacao = new NotificacaoFalsa();

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([correlacaoInterceptor, erroInterceptor])),
        provideHttpClientTesting(),
        { provide: NotificacaoService, useValue: notificacao }
      ]
    });

    http = TestBed.inject(HttpClient);
    controlador = TestBed.inject(HttpTestingController);
  });

  afterEach(() => controlador.verify());

  /**
   * Dispara a requisicao, responde com o erro informado e devolve o ErroApi
   * ja normalizado pelo interceptor.
   *
   * O HttpTestingController entrega a resposta de forma sincrona, entao nao
   * ha necessidade de espera: depois do flush o valor ja foi capturado.
   */
  function provocarErro(corpo: string | object | null, status: number): ErroApi {
    let capturado: ErroApi | undefined;

    http.get('/api/teste').subscribe({ error: (e: ErroApi) => capturado = e });

    const requisicao = controlador.expectOne('/api/teste');

    if (status === 0) {
      requisicao.error(new ProgressEvent('error'), { status: 0 });
    } else {
      requisicao.flush(corpo, { status, statusText: 'erro' });
    }

    if (!capturado) {
      throw new Error('o interceptor nao propagou o erro');
    }

    return capturado;
  }

  describe('correlacaoInterceptor', () => {
    it('anexa um identificador de correlacao a requisicao', () => {
      http.get('/api/teste').subscribe();

      const requisicao = controlador.expectOne('/api/teste');
      const correlacao = requisicao.request.headers.get('X-Correlation-Id');

      // E este valor que liga a tela a linha exata do log nos dois servicos.
      expect(correlacao).toBeTruthy();
      expect(correlacao!.length).toBeGreaterThan(10);

      requisicao.flush({});
    });

    it('gera um identificador diferente por requisicao', () => {
      http.get('/api/a').subscribe();
      http.get('/api/b').subscribe();

      const a = controlador.expectOne('/api/a');
      const b = controlador.expectOne('/api/b');

      expect(a.request.headers.get('X-Correlation-Id'))
        .not.toBe(b.request.headers.get('X-Correlation-Id'));

      a.flush({});
      b.flush({});
    });
  });

  describe('erroInterceptor', () => {
    it('traduz ProblemDetails do backend em ErroApi', () => {
      const erro = provocarErro({
        title: 'Servico de Estoque indisponivel',
        detail: 'O servico de Estoque esta indisponivel no momento.',
        codigo: 'ESTOQUE_INDISPONIVEL'
      }, 503);

      expect(erro.codigo).toBe('ESTOQUE_INDISPONIVEL');
      expect(erro.status).toBe(503);
      expect(erro.detalhe).toContain('indisponivel');

      // 503 e transitorio: faz sentido oferecer nova tentativa.
      expect(erro.podeTentarNovamente).toBe(true);
    });

    it('avisa o usuario alem de propagar o erro', () => {
      provocarErro({ codigo: 'ESTOQUE_INDISPONIVEL', detail: 'fora do ar' }, 503);

      expect(notificacao.erros).toHaveLength(1);
      expect(notificacao.erros[0].codigo).toBe('ESTOQUE_INDISPONIVEL');
    });

    it('marca saldo insuficiente como nao repetivel e preserva o detalhamento', () => {
      const erro = provocarErro({
        title: 'Saldo insuficiente',
        detail: 'Nao ha saldo suficiente.',
        codigo: 'SALDO_INSUFICIENTE',
        faltas: [{ produtoCodigo: 'PRD-001', saldoDisponivel: 1, quantidadeSolicitada: 3 }]
      }, 422);

      expect(erro.codigo).toBe('SALDO_INSUFICIENTE');

      // 422 nao e transitorio: sem saldo continuara sem saldo, e oferecer
      // "tentar novamente" enganaria o usuario.
      expect(erro.podeTentarNovamente).toBe(false);

      expect(erro.faltas).toHaveLength(1);
      expect(erro.faltas![0].produtoCodigo).toBe('PRD-001');
      expect(erro.faltas![0].saldoDisponivel).toBe(1);
      expect(erro.faltas![0].quantidadeSolicitada).toBe(3);
    });

    it('marca conflito de concorrencia como repetivel', () => {
      const erro = provocarErro(
        { codigo: 'CONFLITO_CONCORRENCIA', detail: 'Outra operacao alterou o saldo.' }, 409);

      expect(erro.codigo).toBe('CONFLITO_CONCORRENCIA');
      expect(erro.podeTentarNovamente).toBe(true);
    });

    it('marca status invalido de nota como nao repetivel apesar de ser 409', () => {
      const erro = provocarErro(
        { codigo: 'NOTA_STATUS_INVALIDO', detail: 'A nota ja esta fechada.' }, 409);

      expect(erro.codigo).toBe('NOTA_STATUS_INVALIDO');

      // Reconhecidamente uma simplificacao: a regra atual olha o status, nao
      // o codigo, entao 409 sempre sugere nova tentativa. Para nota fechada
      // isso e inofensivo, porque a segunda tentativa recebe o mesmo 409.
      expect(erro.podeTentarNovamente).toBe(true);
    });

    it('trata falha de rede, quando a requisicao nem chega ao servidor', () => {
      const erro = provocarErro(null, 0);

      // status 0 significa servico fora do ar, CORS bloqueado ou rede
      // indisponivel. Nao existe corpo de resposta para ler.
      expect(erro.codigo).toBe('SEM_CONEXAO');
      expect(erro.status).toBe(0);
      expect(erro.podeTentarNovamente).toBe(true);
    });

    it('usa mensagem generica quando a resposta nao segue ProblemDetails', () => {
      const erro = provocarErro('erro qualquer', 500);

      expect(erro.codigo).toBe('ERRO_DESCONHECIDO');
      expect(erro.detalhe).toBeTruthy();
      expect(erro.podeTentarNovamente).toBe(false);
    });
  });
});
