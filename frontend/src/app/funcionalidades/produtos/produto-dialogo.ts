import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { finalize } from 'rxjs';
import { Produto } from '../../nucleo/modelos';
import { NotificacaoService } from '../../nucleo/notificacao.service';
import { ProdutosService } from '../../nucleo/produtos.service';

export type ResultadoDialogoProduto = 'salvo' | undefined;

@Component({
  selector: 'app-produto-dialogo',
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  templateUrl: './produto-dialogo.html'
})
export class ProdutoDialogo {
  private readonly fb = inject(FormBuilder);
  private readonly servico = inject(ProdutosService);
  private readonly notificacao = inject(NotificacaoService);
  private readonly referencia = inject(MatDialogRef<ProdutoDialogo, ResultadoDialogoProduto>);

  /** null significa criacao; preenchido significa edicao. */
  readonly produto = inject<Produto | null>(MAT_DIALOG_DATA);

  readonly salvando = signal(false);
  readonly edicao = this.produto !== null;

  readonly formulario = this.fb.nonNullable.group({
    codigo: [
      { value: this.produto?.codigo ?? '', disabled: this.edicao },
      [Validators.required, Validators.maxLength(50)]
    ],
    descricao: [this.produto?.descricao ?? '', [Validators.required, Validators.maxLength(200)]],
    saldo: [this.produto?.saldo ?? 0, [Validators.required, Validators.min(0)]]
  });

  salvar(): void {
    if (this.formulario.invalid) {
      this.formulario.markAllAsTouched();
      return;
    }

    this.salvando.set(true);
    const valores = this.formulario.getRawValue();

    // O codigo e imutavel apos a criacao (RN01), por isso o campo aparece
    // desabilitado na edicao e nao e enviado na atualizacao.
    const operacao = this.edicao
      ? this.servico.atualizar(this.produto!.id, {
          descricao: valores.descricao,
          saldo: Number(valores.saldo)
        })
      : this.servico.criar({
          codigo: valores.codigo,
          descricao: valores.descricao,
          saldo: Number(valores.saldo)
        });

    operacao
      .pipe(finalize(() => this.salvando.set(false)))
      .subscribe({
        next: salvo => {
          this.notificacao.sucesso(
            this.edicao ? `Produto ${salvo.codigo} atualizado.` : `Produto ${salvo.codigo} criado.`
          );
          this.referencia.close('salvo');
        },
        // Codigo duplicado (409) chega aqui. O interceptor ja avisou o
        // usuario; manter o dialogo aberto permite corrigir sem redigitar tudo.
        error: () => { }
      });
  }

  cancelar(): void {
    this.referencia.close();
  }
}
