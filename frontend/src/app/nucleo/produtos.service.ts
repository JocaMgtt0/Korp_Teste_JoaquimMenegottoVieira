import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API } from './api.config';
import { AtualizarProduto, CriarProduto, Produto, ResultadoPaginado } from './modelos';

@Injectable({ providedIn: 'root' })
export class ProdutosService {
  private readonly http = inject(HttpClient);
  private readonly base = `${API.estoque}/api/produtos`;

  listar(busca: string, pagina: number, tamanho: number): Observable<ResultadoPaginado<Produto>> {
    let parametros = new HttpParams()
      .set('pagina', pagina)
      .set('tamanho', tamanho);

    if (busca.trim()) {
      parametros = parametros.set('busca', busca.trim());
    }

    return this.http.get<ResultadoPaginado<Produto>>(this.base, { params: parametros });
  }

  obter(id: string): Observable<Produto> {
    return this.http.get<Produto>(`${this.base}/${id}`);
  }

  criar(produto: CriarProduto): Observable<Produto> {
    return this.http.post<Produto>(this.base, produto);
  }

  atualizar(id: string, produto: AtualizarProduto): Observable<Produto> {
    return this.http.put<Produto>(`${this.base}/${id}`, produto);
  }

  excluir(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}`);
  }
}
