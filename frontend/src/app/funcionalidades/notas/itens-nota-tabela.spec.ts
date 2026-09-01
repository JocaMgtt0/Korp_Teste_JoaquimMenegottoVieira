import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ItemNotaFiscal } from '../../nucleo/modelos';
import { ItensNotaTabela } from './itens-nota-tabela';

/**
 * Testes do componente de itens da nota.
 *
 * O foco e o ngOnChanges: e ele que recalcula os totais e decide quais colunas
 * aparecem. Como o componente nao chama servico nem conhece HTTP, os testes
 * sao diretos e rapidos.
 */
describe('ItensNotaTabela', () => {
  let fixture: ComponentFixture<ItensNotaTabela>;
  let componente: ItensNotaTabela;

  const item = (codigo: string, quantidade: number): ItemNotaFiscal => ({
    id: crypto.randomUUID(),
    produtoId: crypto.randomUUID(),
    produtoCodigo: codigo,
    produtoDescricao: `Produto ${codigo}`,
    quantidade
  });

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [ItensNotaTabela] }).compileComponents();

    fixture = TestBed.createComponent(ItensNotaTabela);
    componente = fixture.componentInstance;
  });

  it('calcula os totais quando os itens mudam', () => {
    fixture.componentRef.setInput('itens', [item('PRD-001', 2), item('PRD-002', 5)]);
    fixture.detectChanges();

    expect(componente.totalDeItens).toBe(2);
    expect(componente.quantidadeTotal).toBe(7);
  });

  it('recalcula os totais a cada nova lista recebida', () => {
    fixture.componentRef.setInput('itens', [item('PRD-001', 2)]);
    fixture.detectChanges();
    expect(componente.quantidadeTotal).toBe(2);

    fixture.componentRef.setInput('itens', [item('PRD-001', 2), item('PRD-002', 3)]);
    fixture.detectChanges();

    // A prova de que o ngOnChanges dispara de novo, e nao apenas na primeira vez.
    expect(componente.totalDeItens).toBe(2);
    expect(componente.quantidadeTotal).toBe(5);
  });

  it('zera os totais quando a nota fica sem itens', () => {
    fixture.componentRef.setInput('itens', [item('PRD-001', 4)]);
    fixture.detectChanges();

    fixture.componentRef.setInput('itens', []);
    fixture.detectChanges();

    expect(componente.totalDeItens).toBe(0);
    expect(componente.quantidadeTotal).toBe(0);
  });

  it('mostra a coluna de acoes quando a nota e editavel', () => {
    fixture.componentRef.setInput('itens', [item('PRD-001', 1)]);
    fixture.componentRef.setInput('somenteLeitura', false);
    fixture.detectChanges();

    expect(componente.colunas).toContain('acoes');
  });

  it('esconde a coluna de acoes quando a nota esta fechada', () => {
    fixture.componentRef.setInput('itens', [item('PRD-001', 1)]);
    fixture.componentRef.setInput('somenteLeitura', true);
    fixture.detectChanges();

    // RN06: nota fechada e imutavel, entao nem oferecer o botao faz sentido.
    expect(componente.colunas).not.toContain('acoes');
    expect(componente.colunas).toEqual(['codigo', 'descricao', 'quantidade']);
  });

  it('exibe mensagem propria quando nao ha itens', () => {
    fixture.componentRef.setInput('itens', []);
    fixture.detectChanges();

    const texto = (fixture.nativeElement as HTMLElement).textContent ?? '';

    expect(texto).toContain('Nenhum produto na nota');
  });

  it('renderiza uma linha por item, com codigo e quantidade', () => {
    fixture.componentRef.setInput('itens', [item('PRD-001', 2), item('PRD-002', 5)]);
    fixture.detectChanges();

    const texto = (fixture.nativeElement as HTMLElement).textContent ?? '';

    expect(texto).toContain('PRD-001');
    expect(texto).toContain('PRD-002');
    expect(texto).toContain('Quantidade total');
  });

  it('emite o item ao pedir remocao', () => {
    const linha = item('PRD-001', 3);
    fixture.componentRef.setInput('itens', [linha]);
    fixture.detectChanges();

    let removido: ItemNotaFiscal | undefined;
    componente.remover.subscribe(i => removido = i);

    componente.remover.emit(linha);

    expect(removido).toBe(linha);
  });
});
