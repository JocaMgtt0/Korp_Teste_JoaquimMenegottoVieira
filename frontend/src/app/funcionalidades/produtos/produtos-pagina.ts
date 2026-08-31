import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Subject, debounceTime, distinctUntilChanged, finalize, startWith, switchMap, takeUntil } from 'rxjs';
import { Produto } from '../../nucleo/modelos';
import { NotificacaoService } from '../../nucleo/notificacao.service';
import { ProdutosService } from '../../nucleo/produtos.service';
import { ProdutoDialogo, ResultadoDialogoProduto } from './produto-dialogo';

@Component({
  selector: 'app-produtos-pagina',
  imports: [
    ReactiveFormsModule, MatTableModule, MatButtonModule, MatIconModule,
    MatFormFieldModule, MatInputModule, MatPaginatorModule, MatProgressBarModule,
    MatDialogModule, MatTooltipModule
  ],
  templateUrl: './produtos-pagina.html',
  styleUrl: './produtos-pagina.scss'
})
export class ProdutosPagina implements OnInit, OnDestroy {
  private readonly servico = inject(ProdutosService);
  private readonly notificacao = inject(NotificacaoService);
  private readonly dialogo = inject(MatDialog);

  /**
   * Subject de destruicao usado com takeUntil.
   *
   * Emite uma vez em ngOnDestroy e encerra todas as inscricoes da tela.
   * Sem isso, a busca continuaria viva depois que o usuario navegasse para
   * outra rota, e uma resposta atrasada tentaria atualizar um componente
   * que nao existe mais.
   */
  private readonly destruir$ = new Subject<void>();

  /** Dispara uma nova carga sem alterar o texto da busca. */
  private readonly recarregar$ = new Subject<void>();

  readonly busca = new FormControl('', { nonNullable: true });

  readonly produtos = signal<Produto[]>([]);
  readonly total = signal(0);
  readonly carregando = signal(false);

  pagina = 1;
  tamanho = 10;

  readonly colunas = ['codigo', 'descricao', 'saldo', 'acoes'];

  ngOnInit(): void {
    // Carga inicial e busca reativa no mesmo fluxo.
    //
    // debounceTime(350)         espera o usuario parar de digitar, em vez de
    //                           disparar uma requisicao por tecla
    // distinctUntilChanged()    ignora quando o texto final e igual ao anterior,
    //                           como ao digitar e apagar uma letra
    // switchMap()               cancela a requisicao anterior ao comecar outra.
    //                           E o operador certo aqui: se o usuario digita
    //                           "tec" e depois "tecl", o resultado de "tec" nao
    //                           interessa mais e, pior, poderia chegar depois e
    //                           sobrescrever o resultado correto
    this.busca.valueChanges
      .pipe(
        debounceTime(350),
        distinctUntilChanged(),
        startWith(this.busca.value),
        takeUntil(this.destruir$)
      )
      .subscribe(() => {
        this.pagina = 1;
        this.recarregar$.next();
      });

    this.recarregar$
      .pipe(
        startWith(undefined),
        switchMap(() => {
          this.carregando.set(true);

          return this.servico
            .listar(this.busca.value, this.pagina, this.tamanho)
            // finalize roda tanto em sucesso quanto em erro, entao a barra de
            // progresso nunca fica presa na tela apos uma falha.
            .pipe(finalize(() => this.carregando.set(false)));
        }),
        takeUntil(this.destruir$)
      )
      .subscribe({
        next: resultado => {
          this.produtos.set(resultado.itens);
          this.total.set(resultado.total);
        },
        // O interceptor ja notificou o usuario. Aqui so evitamos que o erro
        // encerre o fluxo e deixe a tela sem reagir a buscas seguintes.
        error: () => this.produtos.set([])
      });
  }

  ngOnDestroy(): void {
    this.destruir$.next();
    this.destruir$.complete();
  }

  trocarPagina(evento: PageEvent): void {
    this.pagina = evento.pageIndex + 1;
    this.tamanho = evento.pageSize;
    this.recarregar$.next();
  }

  novo(): void {
    this.abrirDialogo(null);
  }

  editar(produto: Produto): void {
    this.abrirDialogo(produto);
  }

  excluir(produto: Produto): void {
    if (!confirm(`Excluir o produto ${produto.codigo}?`)) {
      return;
    }

    this.servico.excluir(produto.id)
      .pipe(takeUntil(this.destruir$))
      .subscribe({
        next: () => {
          this.notificacao.sucesso(`Produto ${produto.codigo} excluido.`);
          this.recarregar$.next();
        },
        // Erro esperado aqui: RN09, produto ja usado em nota fiscal.
        // O interceptor ja mostrou a mensagem explicando o motivo.
        error: () => { }
      });
  }

  private abrirDialogo(produto: Produto | null): void {
    this.dialogo
      .open<ProdutoDialogo, Produto | null, ResultadoDialogoProduto>(ProdutoDialogo, {
        width: '460px',
        data: produto
      })
      .afterClosed()
      .pipe(takeUntil(this.destruir$))
      .subscribe(resultado => {
        if (resultado === 'salvo') {
          this.recarregar$.next();
        }
      });
  }
}
