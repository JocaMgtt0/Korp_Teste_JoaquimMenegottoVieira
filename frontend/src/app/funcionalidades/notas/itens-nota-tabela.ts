import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ItemNotaFiscal } from '../../nucleo/modelos';

/**
 * Tabela de itens da nota.
 *
 * Componente de apresentacao: nao chama servico e nao conhece HTTP. Recebe os
 * itens por @Input e avisa o pai por @Output quando o usuario pede alguma acao.
 *
 * Existe para demonstrar ngOnChanges com proposito real. O ciclo de vida
 * ngOnChanges dispara toda vez que um @Input muda de referencia, e e aqui que
 * os totais sao recalculados. A alternativa seria calcular no template, o que
 * refaria a conta a cada ciclo de deteccao de mudancas, mesmo quando nada
 * relacionado aos itens mudou.
 */
@Component({
  selector: 'app-itens-nota-tabela',
  imports: [MatTableModule, MatButtonModule, MatIconModule, MatTooltipModule],
  templateUrl: './itens-nota-tabela.html'
})
export class ItensNotaTabela implements OnChanges {
  @Input({ required: true }) itens: ItemNotaFiscal[] = [];

  /** Nota fechada ou em processamento nao aceita edicao de itens (RN06, RN07). */
  @Input() somenteLeitura = false;

  @Output() remover = new EventEmitter<ItemNotaFiscal>();
  @Output() alterarQuantidade = new EventEmitter<{ item: ItemNotaFiscal; quantidade: number }>();

  totalDeItens = 0;
  quantidadeTotal = 0;

  colunas: string[] = [];

  ngOnChanges(mudancas: SimpleChanges): void {
    if (mudancas['itens']) {
      this.totalDeItens = this.itens.length;
      this.quantidadeTotal = this.itens.reduce((soma, item) => soma + item.quantidade, 0);
    }

    if (mudancas['somenteLeitura'] || mudancas['itens']) {
      this.colunas = this.somenteLeitura
        ? ['codigo', 'descricao', 'quantidade']
        : ['codigo', 'descricao', 'quantidade', 'acoes'];
    }
  }

  editarQuantidade(item: ItemNotaFiscal): void {
    const informado = prompt(`Nova quantidade para ${item.produtoCodigo}:`, String(item.quantidade));

    if (informado === null) {
      return;
    }

    const quantidade = Number(informado);

    if (!Number.isInteger(quantidade) || quantidade <= 0) {
      alert('Informe um numero inteiro maior que zero.');
      return;
    }

    if (quantidade !== item.quantidade) {
      this.alterarQuantidade.emit({ item, quantidade });
    }
  }
}
