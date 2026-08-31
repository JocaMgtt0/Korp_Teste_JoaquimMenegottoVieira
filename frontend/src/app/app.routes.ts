import { Routes } from '@angular/router';

/**
 * Rotas com carregamento sob demanda.
 *
 * loadComponent gera um bundle separado por tela, entao a aplicacao abre
 * carregando apenas o necessario para a primeira rota.
 */
export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'notas' },

  {
    path: 'produtos',
    title: 'Produtos',
    loadComponent: () =>
      import('./funcionalidades/produtos/produtos-pagina').then(m => m.ProdutosPagina)
  },

  {
    path: 'notas',
    title: 'Notas fiscais',
    loadComponent: () =>
      import('./funcionalidades/notas/notas-pagina').then(m => m.NotasPagina)
  },

  {
    path: 'notas/:id',
    title: 'Detalhe da nota',
    loadComponent: () =>
      import('./funcionalidades/notas/nota-detalhe-pagina').then(m => m.NotaDetalhePagina)
  },

  { path: '**', redirectTo: 'notas' }
];
