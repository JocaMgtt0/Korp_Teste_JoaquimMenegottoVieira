/**
 * Enderecos dos dois microsservicos.
 *
 * O Angular fala com cada servico diretamente, sem API Gateway. Foi decisao
 * consciente para nao inflar o escopo do teste: um gateway acrescentaria mais
 * um container, mais uma configuracao de rota e nenhum requisito atendido.
 *
 * Os enderecos sao absolutos e apontam para localhost porque quem faz a
 * chamada e o navegador, na maquina do usuario, e nao o container do nginx.
 * De dentro da rede do Docker os servicos se enxergam por nome, mas isso nao
 * vale para o codigo que roda no browser.
 */
export const API = {
  estoque: 'http://localhost:5001',
  faturamento: 'http://localhost:5002'
} as const;
