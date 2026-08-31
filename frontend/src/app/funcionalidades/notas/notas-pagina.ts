import { DatePipe } from '@angular/common';
import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Router } from '@angular/router';
import { Subject, finalize, startWith, switchMap, takeUntil } from 'rxjs';
import { NotaFiscalResumo, StatusNota } from '../../nucleo/modelos';
import { NotasService } from '../../nucleo/notas.service';
import { NotificacaoService } from '../../nucleo/notificacao.service';

@Component({
  selector: 'app-notas-pagina',
  imports: [
    DatePipe, ReactiveFormsModule, MatTableModule, MatButtonModule, MatIconModule,
    MatFormFieldModule, MatSelectModule, MatPaginatorModule, MatProgressBarModule,
    MatChipsModule, MatTooltipModule
  ],
  templateUrl: './notas-pagina.html',
  styleUrl: './notas-pagina.scss'
})
export class NotasPagina implements OnInit, OnDestroy {
  private readonly servico = inject(NotasService);
  private readonly notificacao = inject(NotificacaoService);
  private readonly router = inject(Router);

  private readonly destruir$ = new Subject<void>();
  private readonly recarregar$ = new Subject<void>();

  readonly filtroStatus = new FormControl<StatusNota | ''>('', { nonNullable: true });

  readonly notas = signal<NotaFiscalResumo[]>([]);
  readonly total = signal(0);
  readonly carregando = signal(false);
  readonly criando = signal(false);

  pagina = 1;
  tamanho = 10;

  readonly colunas = ['numero', 'status', 'itens', 'quantidade', 'criadaEm', 'acoes'];

  ngOnInit(): void {
    this.filtroStatus.valueChanges
      .pipe(takeUntil(this.destruir$))
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
            .listar(this.filtroStatus.value, this.pagina, this.tamanho)
            .pipe(finalize(() => this.carregando.set(false)));
        }),
        takeUntil(this.destruir$)
      )
      .subscribe({
        next: resultado => {
          this.notas.set(resultado.itens);
          this.total.set(resultado.total);
        },
        error: () => this.notas.set([])
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

  criar(): void {
    this.criando.set(true);

    this.servico.criar()
      .pipe(finalize(() => this.criando.set(false)), takeUntil(this.destruir$))
      .subscribe({
        next: nota => {
          this.notificacao.sucesso(`Nota ${nota.numero} criada.`);
          this.router.navigate(['/notas', nota.id]);
        },
        error: () => { }
      });
  }

  abrir(nota: NotaFiscalResumo): void {
    this.router.navigate(['/notas', nota.id]);
  }

  excluir(nota: NotaFiscalResumo, evento: MouseEvent): void {
    // A linha inteira e clicavel, entao o clique no botao precisa parar aqui
    // para nao abrir o detalhe junto.
    evento.stopPropagation();

    if (!confirm(`Excluir a nota ${nota.numero}?`)) {
      return;
    }

    this.servico.excluir(nota.id)
      .pipe(takeUntil(this.destruir$))
      .subscribe({
        next: () => {
          this.notificacao.sucesso(`Nota ${nota.numero} excluida.`);
          this.recarregar$.next();
        },
        error: () => { }
      });
  }

  corDoStatus(status: StatusNota): string {
    switch (status) {
      case 'Aberta': return 'aberta';
      case 'EmProcessamento': return 'processando';
      case 'Fechada': return 'fechada';
    }
  }
}
