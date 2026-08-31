import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API } from './api.config';
import { NotaFiscal, NotaFiscalResumo, ResultadoPaginado, StatusNota } from './modelos';

@Injectable({ providedIn: 'root' })
export class NotasService {
  private readonly http = inject(HttpClient);
  private readonly base = `${API.faturamento}/api/notas`;

  listar(
    status: StatusNota | '',
    pagina: number,
    tamanho: number
  ): Observable<ResultadoPaginado<NotaFiscalResumo>> {
    let parametros = new HttpParams()
      .set('pagina', pagina)
      .set('tamanho', tamanho);

    if (status) {
      parametros = parametros.set('status', status);
    }

    return this.http.get<ResultadoPaginado<NotaFiscalResumo>>(this.base, { params: parametros });
  }

  obter(id: string): Observable<NotaFiscal> {
    return this.http.get<NotaFiscal>(`${this.base}/${id}`);
  }

  criar(): Observable<NotaFiscal> {
    return this.http.post<NotaFiscal>(this.base, {});
  }

  excluir(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}`);
  }

  adicionarItem(notaId: string, produtoId: string, quantidade: number): Observable<NotaFiscal> {
    return this.http.post<NotaFiscal>(`${this.base}/${notaId}/itens`, { produtoId, quantidade });
  }

  alterarQuantidade(notaId: string, itemId: string, quantidade: number): Observable<NotaFiscal> {
    return this.http.put<NotaFiscal>(`${this.base}/${notaId}/itens/${itemId}`, { quantidade });
  }

  removerItem(notaId: string, itemId: string): Observable<NotaFiscal> {
    return this.http.delete<NotaFiscal>(`${this.base}/${notaId}/itens/${itemId}`);
  }

  /**
   * Dispara a impressao: baixa o estoque, gera o PDF e fecha a nota.
   * Devolve a nota atualizada para a tela refletir o novo status.
   */
  imprimir(notaId: string): Observable<NotaFiscal> {
    return this.http.post<NotaFiscal>(`${this.base}/${notaId}/imprimir`, {});
  }

  /** URL do PDF. Aberta em nova aba, sem passar pelo HttpClient. */
  urlDoPdf(notaId: string): string {
    return `${this.base}/${notaId}/pdf`;
  }
}
