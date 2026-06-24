# Stock Quote Alert

Aplicação de console em C# (.NET 8) que monitora a cotação de um ativo da B3 e envia um e-mail de alerta quando o preço passa de certos limites:

- Preço **acima** da linha de venda → alerta de **venda**
- Preço **abaixo** da linha de compra → alerta de **compra**

As cotações vêm da API pública [brapi.dev](https://brapi.dev).

## Como usar

Rode passando o ativo, o preço de venda e o preço de compra:

```bash
dotnet run --project src/StockQuoteAlert -- PETR4 22.67 22.59
```

O programa fica monitorando em loop (a cada 30s por padrão) e envia o e-mail quando o preço cruza um dos limites. Para encerrar, use `Ctrl+C`.

## Configuração

O e-mail de destino e os dados de SMTP ficam num arquivo JSON separado. Para criar o seu:

1. Copie o exemplo:
   ```bash
   cp src/StockQuoteAlert/config.example.json src/StockQuoteAlert/config.json
   ```
2. Abra o `config.json` e preencha com seus dados:

   ```json
   {
     "alertRecipient": "seu-email@gmail.com",
     "smtp": {
       "host": "smtp.gmail.com",
       "port": 587,
       "useSsl": true,
       "username": "seu-email@gmail.com",
       "password": "sua-senha-de-app",
       "fromAddress": "seu-email@gmail.com",
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

Observações:
- Se usar Gmail, o campo `password` precisa ser uma **senha de app** (não a senha normal da conta).
- O `api.token` pode ficar vazio para ativos comuns como PETR4 e VALE3.
- O `config.json` está no `.gitignore` porque contém senha; só o `config.example.json` vai para o repositório.

## Rodar os testes

```bash
dotnet test
```

Os testes cobrem a validação dos parâmetros e a lógica de alerta (cruzamento de limites, faixa neutra e cooldown). Não dependem de rede nem enviam e-mail de verdade.

## Como o projeto está organizado

O código é separado em camadas, cada uma com sua responsabilidade:

| Pasta | O que faz |
|-------|-----------|
| `Cli/` | Lê e valida os parâmetros da linha de comando |
| `Configuration/` | Lê o arquivo de configuração JSON |
| `Quotes/` | Busca a cotação na brapi.dev |
| `Notifications/` | Envia o alerta por e-mail (SMTP) |
| `Monitoring/` | Loop de monitoramento e lógica de alerta |
| `Program.cs` | Junta tudo e inicia a aplicação |

As partes de cotação e de e-mail ficam atrás de interfaces (`IQuoteProvider` e `INotifier`), o que facilita os testes e permite trocar a fonte de cotação ou o canal de aviso sem mexer no resto.

## Detalhes da lógica

- **Alerta por cruzamento:** o e-mail é enviado quando o preço *cruza* um limite, não a cada leitura — assim você não recebe dezenas de e-mails iguais enquanto o preço continua fora da faixa.
- **Cooldown:** um intervalo mínimo entre alertas do mesmo tipo, configurável, para evitar spam.
- **Resiliência:** falhas de rede ou da API são tratadas para não derrubar a aplicação.
