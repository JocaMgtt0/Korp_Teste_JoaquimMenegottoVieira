import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { correlacaoInterceptor, erroInterceptor } from './nucleo/interceptors';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),

    // withComponentInputBinding liga parametros de rota a inputs do
    // componente, o que evita injetar ActivatedRoute so para ler um id.
    provideRouter(routes, withComponentInputBinding()),

    // Sem provideAnimationsAsync: a partir do Angular 20 o Material usa
    // animacoes em CSS e o pacote @angular/animations deixou de ser
    // dependencia obrigatoria. Mante-lo exigiria instalar um pacote que
    // nada mais consome.

    // A ordem importa: correlacao primeiro para que o cabecalho ja exista
    // quando o erroInterceptor registrar a falha.
    provideHttpClient(withInterceptors([correlacaoInterceptor, erroInterceptor]))
  ]
};
