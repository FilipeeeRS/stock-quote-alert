# Stock Quote Alert

Aplicação em C# (.NET 8) que monitora cotações da B3 e avisa por e-mail quando o preço fica interessante para compra ou venda. As cotações vêm da API pública [brapi.dev](https://brapi.dev).

O projeto tem dois modos:

| Modo | Para quê |
|------|----------|
| **Site** (Etapa 4) | A página que o usuário acessa: digita o ativo e o e-mail, sem cadastro |
| **Inscrições** (Etapa 2) | Os mesmos recursos por linha de comando, para testar |
| **Original** (Etapa 1) | Monitora **um** ativo, com os limites que você digita na linha de comando |

---

## O site

```bash
dotnet run --project src/StockQuoteAlert.Web
```

Abra <http://localhost:5000>. A página tem uma barra no meio: digite o ativo, veja a cotação e os limites, informe o e-mail e pronto — **sem criar conta, sem senha**.

O worker sobe junto com o site, no mesmo processo. Não precisa rodar nada separado.

### As páginas e a API

| Endereço | O que faz |
|----------|-----------|
| `/` | A página principal |
| `/cancelar?token=…` | Página do botão "Cancelar" do e-mail |
| `GET /api/cotacao/{ativo}` | Cotação + limites calculados |
| `POST /api/inscricoes` | Cria a inscrição |
| `POST /api/cancelar` | Cancela pelo token |

### O visual

Tema escuro, com um terreno 3D animado ao fundo: a câmera avança para sempre sobre uma malha que ondula. É feito em WebGL, calculado por pixel dentro do shader — sem biblioteca de 3D, sem nada externo. Se o navegador não tiver WebGL ou o shader não compilar, o canvas some sozinho e fica o fundo escuro; a página continua funcionando igual.

Os arquivos ficam em `wwwroot/`:

| Arquivo | O que é |
|---------|---------|
| `index.html` | A página principal |
| `cancelar.html` | A página do botão "Cancelar" do e-mail |
| `estilo.css` | Cores, campos, botões e cartões — compartilhado pelas duas |
| `cena.js` | A cena 3D de fundo — compartilhada pelas duas |

Cuidados tomados: a animação para quando a aba fica escondida (não gastar bateria), a resolução é limitada a 1,5× (calcular por pixel fica caro em tela 4K), e quem tiver "reduzir movimento" ligado no sistema recebe a cena parada em vez de animada.

Dois cuidados que valem explicar:

**A busca tem cache de 5 minutos.** Sem isso, cada pesquisa de cada visitante gastaria uma requisição da cota mensal da brapi, e alguém apertando F5 esvaziaria a cota sozinho. Como a cotação gratuita já vem com ~30 minutos de atraso, o cache não esconde nada.

**O link de cancelar não cancela ao abrir.** Ele mostra uma tela pedindo confirmação. Isso porque antivírus e servidores de e-mail costumam abrir os links das mensagens sozinhos para checar segurança — se cancelássemos na abertura, gente seria descadastrada sem nunca ter clicado.

---

## Modo original (Etapa 1)

Rode passando o ativo, o preço de venda e o preço de compra:

```bash
dotnet run --project src/StockQuoteAlert -- PETR4 22.67 22.59
```

O programa monitora em loop (a cada 30s por padrão) e envia o e-mail quando o preço cruza um dos limites. Para encerrar, `Ctrl+C`.

---

## Modo inscrições (Etapa 2)

Aqui ninguém digita preço. A pessoa informa só o **ativo** e o **e-mail**, e o sistema decide sozinho quando avisar, comparando o preço de hoje com o histórico recente do próprio ativo.

### Cadastrar um aviso

```bash
dotnet run --project src/StockQuoteAlert -- inscrever PETR4 voce@email.com
```

A saída mostra um **token de cancelamento** — é o código secreto que vai no botão "Cancelar" do e-mail.

### Ver o que está cadastrado

```bash
dotnet run --project src/StockQuoteAlert -- listar
```

Mostra as inscrições, em que faixa cada uma está, e os limites já calculados por ativo.

### Rodar o worker

```bash
dotnet run --project src/StockQuoteAlert -- monitorar
```

Ele repete uma rodada a cada 5 minutos até você apertar `Ctrl+C`. Para fazer uma rodada só e sair (bom para testar):

```bash
dotnet run --project src/StockQuoteAlert -- monitorar --uma-vez
```

### Cancelar

```bash
dotnet run --project src/StockQuoteAlert -- cancelar <token>
```

---

## Como os limites são calculados

Em vez de um preço fixo digitado à mão, o sistema pega os **últimos 6 meses** de fechamento do ativo e usa percentis:

- Abaixo do **percentil 20** → está barato → **sinal de compra**
- Acima do **percentil 80** → está caro → **sinal de venda**
- No meio → faixa neutra, não avisa

A vantagem sobre um limite fixo: se a ação sai de R$ 20 e passa a valer R$ 120, a janela acompanha e, depois de alguns meses, R$ 120 vira o novo normal. Um número digitado nunca se ajustaria sozinho.

O cálculo é refeito a cada 24 horas (o histórico é diário, refazer a cada rodada não mudaria nada).

> Isto **não é recomendação de investimento**. É uma comparação estatística com o passado recente, e o passado não garante o futuro.

---

## Limites da API gratuita

Vale saber antes de planejar:

- **Sem token**, a brapi só atende **PETR4, VALE3, ITUB4 e MGLU3**. Qualquer outro ativo responde `401`. Para os demais, crie uma conta gratuita em [brapi.dev](https://brapi.dev) e preencha `api.token`.
- O plano gratuito dá **15.000 requisições/mês**, com **1 ativo por chamada**.
- A cotação vem com **cerca de 30 minutos de atraso** no plano gratuito — por isso consultar a cada 30 segundos não adiantaria nada.

Com rodadas de 5 minutos e consultando só durante o pregão, cabem **uns 8 ativos distintos** por mês. O número de *inscritos* não pesa: 500 pessoas acompanhando PETR4 custam uma consulta, não 500.

---

## Configuração

```bash
cp src/StockQuoteAlert/config.example.json src/StockQuoteAlert/config.json
```

Depois preencha o `config.json`. O arquivo de exemplo tem um comentário explicando cada campo.

Observações:

- Se usar Gmail, `password` precisa ser uma **senha de app**, não a senha normal da conta.
- Os comandos `inscrever`, `listar` e `cancelar` funcionam **mesmo sem `config.json`** — eles só mexem no banco. Assim dá para testar o banco antes de configurar e-mail.
- `config.json` e a pasta `data/` estão no `.gitignore`: um tem senha, o outro tem os e-mails dos inscritos.

---

## Rodar os testes

```bash
dotnet test
```

São 61 testes, nenhum depende de rede ou envia e-mail de verdade. Cobrem a validação de parâmetros e de entrada, o cálculo dos limites, a regra de alerta e o worker completo (com um banco SQLite temporário).

---

## Como o projeto está organizado

| Pasta | O que faz |
|-------|-----------|
| `Cli/` | Argumentos e comandos da linha de comando |
| `Configuration/` | Lê o `config.json` |
| `Quotes/` | Busca cotação e histórico na brapi.dev |
| `Analysis/` | Calcula os limites a partir do histórico |
| `Data/` | Banco SQLite: inscrições, ativos e avisos enviados |
| `Notifications/` | Envia os e-mails (SMTP) |
| `Monitoring/` | Regra de alerta, horário de pregão e a rodada do worker |
| `Validation/` | Regras de ativo e e-mail, compartilhadas pelo site e pelo CLI |
| `Program.cs` | Decide qual modo rodar |

O site fica num projeto separado, `src/StockQuoteAlert.Web`, que só adiciona as páginas e a API — toda a lógica vem do projeto acima. Assim o programa de console da Etapa 1 continua sendo um console puro.

Cotação e envio ficam atrás de interfaces (`IQuoteProvider`, `INotifier`, `ISubscriberNotifier`), o que permite testar sem rede e trocar de provedor sem mexer no resto.

### O banco

| Tabela | Guarda |
|--------|--------|
| `Inscricoes` | ativo, e-mail, token de cancelamento e o estado do alerta |
| `Ativos` | última cotação e limites calculados, compartilhados por todos os inscritos |
| `Avisos` | histórico do que já foi enviado |

Valores em dinheiro são gravados como **texto** no formato invariante (`"41.21"`), nunca como ponto flutuante — `float`/`double` não representam `0,10` exatamente. Datas vão em **UTC**, e viram horário de Brasília só na hora de mostrar.

---

## Detalhes da lógica

- **Alerta por cruzamento:** o e-mail sai quando o preço *muda de faixa*, não a cada leitura — assim você não recebe dezenas de e-mails iguais.
- **Cooldown:** intervalo mínimo entre dois avisos do mesmo tipo, configurável.
- **Estado no banco:** reiniciar o worker **não** reenvia avisos já mandados. Na Etapa 1 o estado vivia na memória e sumia.
- **Uma consulta por ativo:** vários inscritos no mesmo ativo custam uma única chamada à API.
- **Resiliência:** falha de rede, ativo inválido ou erro de envio são registrados e o processo continua. Se um e-mail falha, o estado não é marcado como enviado e a próxima rodada tenta de novo.
- **Fora do pregão o worker dorme**, para não gastar cota à toa. Feriados da B3 não são tratados — o custo de errar é só alguma consulta desperdiçada.
