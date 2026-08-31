import { DatePipe } from '@angular/common';
import { Component, Input, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Router } from '@angular/router';
import {
  Subject, catchError, debounceTime, distinctUntilChanged, finalize, forkJoin, map, of,
  switchMap, takeUntil
} from 'rxjs';
import { ItemNotaFiscal, NotaFiscal, Produto } from '../../nucleo/modelos';
import { NotasService } from '../../nucleo/notas.service';
import { NotificacaoService } from '../../nucleo/notificacao.service';
import { ProdutosService } from '../../nucleo/produtos.service';
import { ItensNotaTabela } from './itens-nota-tabela';

@Component({
  selector: 'app-nota-detalhe-pagina',
  imports: [
    DatePipe, ReactiveFormsModule, ItensNotaTabela, MatButtonModule, MatIconModule,
    MatFormFieldModule, MatInputModule, MatAutocompleteModule, MatProgressBarModule,
    MatProgressSpinnerModule, MatTooltipModule
  ],
  templateUrl: './nota-detalhe-pagina.html',
  styleUrl: './nota-detalhe-pagina.scss'
})
export class NotaDetalhePagina implements OnInit, OnDestroy {
  private readonly notasService = inject(NotasService);
  private readonly produtosService = inject(ProdutosService);
  private readonly notificacao = inject(NotificacaoService);
  private readonly router = inject(Router);

  private readonly destruir$ = new Subject<void>();

  /** Vem do parametro de rota via withComponentInputBinding. */
  @Input({ required: true }) id!: string;

  readonly nota = signal<NotaFiscal | null>(null);
  readonly carregando = signal(false);
  readonly imprimindo = signal(false);
  readonly adicionando = signal(false);

  readonly buscaProduto = new FormControl('', { nonNullable: true });
  readonly quantidade = new FormControl(1, { nonNullable: true });
  readonly sugestoes = signal<Produto[]>([]);

  produtoSelecionado: Produto | null = null;

  ngOnInit(): void {
    this.carregar();
    this.configurarAutocomplete();
  }

  ngOnDestroy(): void {
    this.destruir$.next();
    this.destruir$.complete();
  }

  /**
   * Carga inicial da tela.
   *
   * forkJoin dispara as duas chamadas em paralelo e so emite quando ambas
   * respondem. Encadeadas, a tela levaria a soma dos dois tempos; em paralelo,
   * leva o tempo da mais lenta.
   *
   * O catchError no ramo dos produtos e essencial, nao decorativo: forkJoin
   * falha inteiro se QUALQUER fonte falhar. Sem ele, com o servico de Estoque
   * fora do ar a tela da nota nem abriria, apesar de a nota guardar copia do
   * codigo e da descricao de cada item exatamente para nao depender do outro
   * servico. Convertendo a falha em lista vazia, perde-se apenas a sugestao
   * de produtos para inclusao, e a nota continua legivel e imprimivel assim
   * que o Estoque voltar.
   */
  private carregar(): void {
    this.carregando.set(true);

    forkJoin({
      nota: this.notasService.obter(this.id),
      produtos: this.produtosService.listar('', 1, 100).pipe(
        map(r => r.itens),
        catchError(() => of<Produto[]>([]))
      )
    })
      .pipe(finalize(() => this.carregando.set(false)), takeUntil(this.destruir$))
      .subscribe({
        next: ({ nota, produtos }) => {
          this.nota.set(nota);
          this.sugestoes.set(produtos);
        },
        error: () => this.router.navigate(['/notas'])
      });
  }

  /**
   * Autocomplete de produtos.
   *
   * Mesmo trio da tela de produtos: debounceTime evita uma requisicao por
   * tecla, distinctUntilChanged ignora texto repetido e switchMap cancela a
   * busca anterior, garantindo que uma resposta atrasada nunca sobrescreva
   * a mais recente.
   */
  private configurarAutocomplete(): void {
    this.buscaProduto.valueChanges
      .pipe(
        debounceTime(300),
        distinctUntilChanged(),
        // O catchError fica DENTRO do switchMap de proposito. Se ficasse
        // fora, uma falha encerraria o fluxo externo e a busca pararia de
        // funcionar para sempre, mesmo depois de o Estoque voltar.
        switchMap(termo =>
          this.produtosService.listar(termo ?? '', 1, 20).pipe(
            map(resultado => resultado.itens),
            catchError(() => of<Produto[]>([]))
          )
        ),
        takeUntil(this.destruir$)
      )
      .subscribe(produtos => this.sugestoes.set(produtos));
  }

  exibirProduto(produto: Produto | string | null): string {
    if (!produto || typeof produto === 'string') {
      return typeof produto === 'string' ? produto : '';
    }

    return `${produto.codigo} - ${produto.descricao}`;
  }

  selecionar(produto: Produto): void {
    this.produtoSelecionado = produto;
  }

  get podeEditar(): boolean {
    return this.nota()?.status === 'Aberta';
  }

  get podeImprimir(): boolean {
    const atual = this.nota();
    return atual?.status === 'Aberta' && atual.itens.length > 0;
  }

  adicionarItem(): void {
    const atual = this.nota();
    const produto = this.produtoSelecionado;

    if (!atual || !produto) {
      this.notificacao.sucesso('Selecione um produto da lista.');
      return;
    }

    const quantidade = Number(this.quantidade.value);

    if (!Number.isInteger(quantidade) || quantidade <= 0) {
      this.notificacao.sucesso('A quantidade deve ser um inteiro maior que zero.');
      return;
    }

    this.adicionando.set(true);

    this.notasService.adicionarItem(atual.id, produto.id, quantidade)
      .pipe(finalize(() => this.adicionando.set(false)), takeUntil(this.destruir$))
      .subscribe({
        next: atualizada => {
          this.nota.set(atualizada);
          this.buscaProduto.setValue('');
          this.quantidade.setValue(1);
          this.produtoSelecionado = null;
        },
        // 422 de saldo insuficiente cai aqui. O interceptor ja mostrou quais
        // produtos faltaram e quanto faltou.
        error: () => { }
      });
  }

  removerItem(item: ItemNotaFiscal): void {
    const atual = this.nota();
    if (!atual) return;

    this.notasService.removerItem(atual.id, item.id)
      .pipe(takeUntil(this.destruir$))
      .subscribe({
        next: atualizada => this.nota.set(atualizada),
        error: () => { }
      });
  }

  alterarQuantidade(evento: { item: ItemNotaFiscal; quantidade: number }): void {
    const atual = this.nota();
    if (!atual) return;

    this.notasService.alterarQuantidade(atual.id, evento.item.id, evento.quantidade)
      .pipe(takeUntil(this.destruir$))
      .subscribe({
        next: atualizada => this.nota.set(atualizada),
        error: () => { }
      });
  }

  /**
   * Dispara a impressao.
   *
   * finalize garante que o indicador de processamento desliga em qualquer
   * desfecho: sucesso, saldo insuficiente ou servico de Estoque fora do ar.
   * Sem ele, uma falha deixaria o botao travado em "Imprimindo..." para sempre.
   */
  imprimir(): void {
    const atual = this.nota();
    if (!atual) return;

    this.imprimindo.set(true);

    this.notasService.imprimir(atual.id)
      .pipe(finalize(() => this.imprimindo.set(false)), takeUntil(this.destruir$))
      .subscribe({
        next: atualizada => {
          this.nota.set(atualizada);
          this.notificacao.sucesso(`Nota ${atualizada.numero} impressa e fechada.`);
          window.open(this.notasService.urlDoPdf(atualizada.id), '_blank');
        },
        // Toda falha ja foi comunicada pelo interceptor. A nota continua
        // Aberta no servidor, e o estado precisa ser refletido na tela.
        //
        // Recarrega SOMENTE a nota, e nao carregar(), porque carregar() busca
        // tambem o catalogo de produtos. Com o Estoque fora do ar essa segunda
        // chamada falha e o aviso generico de conexao sobrescreveria na tela a
        // mensagem util que explica que a nota permanece Aberta.
        error: () => this.recarregarSomenteNota()
      });
  }

  private recarregarSomenteNota(): void {
    this.notasService.obter(this.id)
      .pipe(takeUntil(this.destruir$))
      .subscribe({
        next: atualizada => this.nota.set(atualizada),
        error: () => { }
      });
  }

  baixarPdf(): void {
    const atual = this.nota();
    if (!atual) return;

    window.open(this.notasService.urlDoPdf(atual.id), '_blank');
  }

  voltar(): void {
    this.router.navigate(['/notas']);
  }
}
