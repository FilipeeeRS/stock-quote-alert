# Stock Quote Alert

Aplicação de **console em C# (.NET 8)** que monitora continuamente a cotação de um ativo da **B3** e envia um **e-mail** de alerta quando o preço cruza limites de referência:

- Preço **acima** da linha de venda → alerta de **VENDA**
- Preço **abaixo** da linha de compra → alerta de **COMPRA**

A fonte de cotações é a API pública [brapi.dev](https://brapi.dev).

---

## Uso

```bash
stock-quote-alert <ATIVO> <PRECO_VENDA> <PRECO_COMPRA>
```

Exemplo (igual ao enunciado):

```bash
stock-quote-alert PETR4 22.67 22.59
```

| Parâmetro      | Significado                                                  |
|----------------|-------------------------------------------------------------|
| `ATIVO`        | Ticker na B3 (ex.: `PETR4`)                                  |
| `PRECO_VENDA`  | Linha azul — acima dela, dispara alerta de **venda**        |
| `PRECO_COMPRA` | Linha vermelha — abaixo dela, dispara alerta de **compra**  |

> Aceita ponto ou vírgula como separador decimal. O preço de venda deve ser maior que o de compra.

O programa roda em loop até receber `Ctrl+C`.

---

## Configuração

As informações sensíveis (destinatário e SMTP) ficam em um arquivo JSON **separado dos argumentos**, conforme o enunciado.

1. Copie o exemplo:

   ```bash
   cp src/StockQuoteAlert/config.example.json src/StockQuoteAlert/config.json
   ```

2. Edite `config.json`:

   ```json
   {
     "alertRecipient": "destinatario@exemplo.com",
     "smtp": {
       "host": "smtp.gmail.com",
       "port": 587,
       "useSsl": true,
       "username": "sua-conta@gmail.com",
       "password": "sua-senha-de-app",
       "fromAddress": "sua-conta@gmail.com",
       "fromName": "Stock Quote Alert"
     },
     "api": {
       "baseUrl": "https://brapi.dev/api/quote/",
       "token": ""
     },
     "pollIntervalSeconds": 30,
     "alertCooldownMinutes": 15
   }
   ```

| Campo                  | Descrição                                                                 |
|------------------------|---------------------------------------------------------------------------|
| `alertRecipient`       | E-mail que receberá os alertas                                            |
| `smtp.*`               | Servidor SMTP de envio                                                     |
| `api.token`            | Token brapi.dev (opcional; `PETR4`, `VALE3`, `MGLU3`, `ITUB4` funcionam sem token) |
| `pollIntervalSeconds`  | Intervalo entre consultas (padrão 30s)                                    |
| `alertCooldownMinutes` | Tempo mínimo entre dois alertas do **mesmo** tipo (evita spam)            |

O caminho do config pode ser sobrescrito pela variável de ambiente `STOCK_ALERT_CONFIG`. Caso contrário, busca `config.json` ao lado do executável.

> ⚠️ `config.json` está no `.gitignore` por conter credenciais. Apenas `config.example.json` é versionado.

---

## Como rodar

```bash
# restaurar e compilar
dotnet build

# executar
dotnet run --project src/StockQuoteAlert -- PETR4 22.67 22.59

# rodar os testes
dotnet test
```

---

## Decisões de projeto

- **Separação por responsabilidade**: cotação (`IQuoteProvider`), notificação (`INotifier`), configuração, CLI e monitoramento ficam em camadas independentes.
- **Programação para interfaces**: `IQuoteProvider` e `INotifier` permitem trocar a API de cotação ou o canal de notificação sem tocar na lógica central, e tornam o `StockMonitor` testável com *fakes* (sem rede nem SMTP).
- **Detecção por cruzamento + cooldown**: o alerta é disparado quando o preço *cruza* um limite, não a cada leitura dentro da faixa. Um cooldown configurável evita enxurrada de e-mails quando o preço oscila junto ao limite.
- **Robustez do loop**: falhas de rede/timeout/parse são registradas e o monitoramento continua, em vez de derrubar o processo.
- **Clock injetável** no monitor para testar o cooldown de forma determinística.
- **Encerramento gracioso** via `Ctrl+C` (`CancellationToken`).

## Estrutura

```
src/StockQuoteAlert/
  Program.cs                     # composição e ponto de entrada
  Cli/CliArguments.cs            # parsing/validação dos argumentos
  Configuration/                 # AppSettings + ConfigLoader (JSON)
  Quotes/                        # IQuoteProvider + BrapiQuoteProvider
  Notifications/                 # INotifier + EmailNotifier (SMTP)
  Monitoring/StockMonitor.cs     # loop e lógica de alerta
tests/StockQuoteAlert.Tests/     # testes (xUnit)
```

## Possíveis evoluções

- Suporte a múltiplos ativos numa só execução.
- Logging estruturado (Serilog) e injeção de dependência via `Microsoft.Extensions.*`.
- Retry com *backoff* exponencial nas chamadas HTTP.
- Notificação por outros canais (Telegram, webhook) implementando `INotifier`.
