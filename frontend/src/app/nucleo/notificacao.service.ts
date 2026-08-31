import { Injectable, inject } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ErroApi } from './modelos';

@Injectable({ providedIn: 'root' })
export class NotificacaoService {
  private readonly snackBar = inject(MatSnackBar);

  sucesso(mensagem: string): void {
    this.snackBar.open(mensagem, 'Fechar', {
      duration: 4000,
      panelClass: ['aviso-sucesso']
    });
  }

  /**
   * Exibe um erro vindo da API.
   *
   * A duracao varia com a gravidade: falta de saldo e recusa esperada e sai
   * rapido; servico indisponivel e problema que o usuario precisa registrar,
   * entao fica mais tempo na tela.
   */
  erro(erro: ErroApi): void {
    this.snackBar.open(this.montarTexto(erro), 'Fechar', {
      duration: erro.podeTentarNovamente ? 8000 : 6000,
      panelClass: ['aviso-erro']
    });
  }

  private montarTexto(erro: ErroApi): string {
    // Quando o backend detalha quais produtos faltaram, mostrar os numeros
    // vale muito mais do que a mensagem generica.
    if (erro.codigo === 'SALDO_INSUFICIENTE' && erro.faltas?.length) {
      const detalhes = erro.faltas
        .map(f => `${f.produtoCodigo} (tem ${f.saldoDisponivel}, precisa de ${f.quantidadeSolicitada})`)
        .join('; ');

      return `Saldo insuficiente: ${detalhes}`;
    }

    // Sem sufixo generico: as mensagens do backend ja sao acionaveis e
    // terminam orientando o usuario quando faz sentido. Acrescentar
    // "voce pode tentar novamente" produzia frases repetidas na tela.
    return erro.detalhe;
  }
}
