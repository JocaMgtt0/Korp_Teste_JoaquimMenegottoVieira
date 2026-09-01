import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';

/**
 * Testes do shell da aplicacao.
 *
 * O componente usa routerLink na barra de navegacao, entao precisa do router
 * configurado no TestBed. Sem provideRouter, o proprio createComponent falha
 * com NG0201 ao tentar resolver ActivatedRoute.
 */
describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([])]
    }).compileComponents();
  });

  it('cria o componente raiz', () => {
    const fixture = TestBed.createComponent(App);

    expect(fixture.componentInstance).toBeTruthy();
  });

  it('exibe o titulo da aplicacao na barra superior', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    await fixture.whenStable();

    const elemento = fixture.nativeElement as HTMLElement;

    expect(elemento.textContent).toContain('Emissao de Notas Fiscais');
  });

  it('oferece navegacao para notas e para produtos', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    await fixture.whenStable();

    const elemento = fixture.nativeElement as HTMLElement;
    const destinos = Array.from(elemento.querySelectorAll('a'))
      .map(a => a.getAttribute('href'));

    expect(destinos).toContain('/notas');
    expect(destinos).toContain('/produtos');
  });
});
