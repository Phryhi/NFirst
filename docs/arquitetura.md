# Arquitetura

## Visão geral

O NFirst é uma aplicação única em ASP.NET Core (.NET 10) que atende a API e a interface web. A interface é composta por arquivos estáticos servidos pela própria aplicação, e os dados são armazenados em um banco SQLite local, isto é, não há servidor de banco de dados separado.

Os critérios usados para aceitar ou descartar uma nota estão descritos em [regras-de-negocio.md](regras-de-negocio.md).

## Camadas

| Camada | Local | Responsabilidade |
| :--- | :--- | :--- |
| Interface web | `wwwroot/` | Permite selecionar a pasta, envia os arquivos para a API, exibe o resultado em JSON e baixa os arquivos gerados. |
| Controller | `Controllers/` | Recebe as requisições HTTP, aciona o service e monta a resposta, incluindo a mensagem de resumo do envio. |
| Service | `Services/` | Concentra a seleção dos arquivos, a validação das notas, o controle de duplicidade, a gravação e a exportação das chaves. |
| Dados | `Data/`, `Models/`, `Migrations/` | Define a entidade da nota fiscal, o acesso ao banco (Entity Framework Core) e o histórico de alterações do banco. |
| Configuração | `Program.cs` | Registra os serviços, aplica as migrations e define o pipeline da aplicação. |

A comunicação entre as camadas acontece em um único sentido: a interface chama o controller, o controller chama o service, e o service acessa o banco de dados e a pasta `Temp/`.

```mermaid
flowchart LR
    W["Interface web<br/>wwwroot"] --> C["NotasController"]
    C --> S["NotaService"]
    S --> D["AppDbContext"]
    D --> B[("Banco SQLite")]
    S --> T["Pasta Temp"]
```

## Fluxo de um envio

O diagrama abaixo resume o caminho de um envio, da seleção da pasta até o download dos arquivos.

```mermaid
flowchart TD
    A(["Usuário seleciona a pasta<br/>e clica em Enviar"]) --> B["Interface envia os arquivos<br/>POST /Notas/EnviarArquivos"]
    B --> C["1. Seleção dos arquivos<br/>extensão .xml e nome compatível"]
    C --> D["2. Validação de cada XML, em paralelo<br/>chave, autorização, modelo, CPF e valor"]
    D --> E{"Nota válida<br/>e chave inédita?"}
    E -- Sim --> F["3. Salva as notas<br/>no banco de dados"]
    E -- Não --> G["Nota descartada ou contada<br/>como já processada"]
    F --> H["4. Gera os arquivos .txt<br/>na pasta Temp"]
    H --> I["Retorna a mensagem de resumo,<br/>os arquivos e as notas em JSON"]
    G --> I
    I --> J(["Interface exibe o resultado"])
    J --> K(["Usuário clica em Baixar TXT"])
    K --> L["GET /Notas/Download<br/>devolve os arquivos da pasta Temp"]

    classDef web fill:#dbeafe,stroke:#2563eb,color:#1e293b
    classDef api fill:#d1fae5,stroke:#059669,color:#1e293b
    classDef dados fill:#fef3c7,stroke:#d97706,color:#1e293b
    classDef decisao fill:#ede9fe,stroke:#7c3aed,color:#1e293b

    class A,B,J,K web
    class C,D,G,I,L api
    class F,H dados
    class E decisao
```

| Cor | Significado |
| :--- | :--- |
| Azul | Interface web e ações do usuário |
| Verde | Processamento na API |
| Laranja | Banco de dados e arquivos gerados |
| Roxo | Ponto de decisão |

As etapas numeradas de 1 a 4 correspondem às seções de [regras-de-negocio.md](regras-de-negocio.md). A decisão da nota é tomada para cada arquivo, e uma nota descartada não interrompe o processamento das demais.

## Endpoints

| Rota | Descrição | Usado pela interface |
| :--- | :--- | :---: |
| `POST /Notas/EnviarArquivos` | Valida os arquivos XML, salva as notas válidas, gera os arquivos de exportação e retorna a mensagem de resumo, os nomes dos arquivos e as notas em JSON. | Sim |
| `GET /Notas/Download/{nome}` | Retorna um arquivo gerado, a partir do nome informado na resposta do envio. Retorna o erro 404 se o arquivo não existir. | Sim |
| `POST /Notas/ProcessarArquivos` | Valida os arquivos XML, salva as notas válidas e retorna a lista de notas. Não gera os arquivos de exportação. Apesar do nome, também grava no banco de dados. | Não |
| `GET /Notas` | Retorna todas as notas salvas. | Não |
| `POST /Notas` | Salva uma nota enviada em JSON, sem passar pelas validações dos arquivos XML. | Não |

A resposta de `EnviarArquivos` possui três campos:

| Campo | Descrição |
| :--- | :--- |
| `mensagem` | Resumo do envio, com a quantidade de arquivos recebidos, de notas já processadas e de notas salvas. |
| `arquivos` | Nomes dos arquivos de exportação gerados. |
| `notas` | Lista das notas válidas, com `chave`, `serie` e `mesAno`. |

Todos os endpoints estão documentados no Swagger, disponível em ambiente de desenvolvimento.

## Configuração da aplicação

No `Program.cs`, a aplicação executa as seguintes etapas:

| Ordem | Etapa | Descrição |
| :---: | :--- | :--- |
| 1 | Registro dos serviços | Controllers, `AppDbContext` com SQLite, `NotaService` e Swagger. |
| 2 | Migrations | As migrations pendentes são aplicadas na inicialização, e o banco é criado na primeira execução. |
| 3 | Swagger | Habilitado somente no ambiente `Development`. |
| 4 | Redirecionamento HTTPS | Redireciona as requisições HTTP para HTTPS quando a porta HTTPS está ativa. |
| 5 | Arquivos estáticos | Serve a interface web da pasta `wwwroot/`. |
| 6 | Controllers | Mapeia os endpoints da API. |

## Decisões de projeto

| Decisão | Motivo |
| :--- | :--- |
| Aplicação única, com API e interface web | Permite executar tudo com um único comando, sem configurar servidores separados. |
| SQLite como banco de dados | Dispensa a instalação de um servidor de banco de dados. |
| Migrations aplicadas na inicialização | A pessoa que executa o projeto não precisa rodar comandos adicionais para criar o banco. |
| Validação dos arquivos em paralelo | Reduz o tempo de processamento de pastas com muitos arquivos. As chaves já existentes são carregadas do banco enquanto os arquivos são lidos. |
| Descarte de arquivos pelo nome | Evita abrir arquivos que, pelo nome, já se sabe que não são NFC-e. |
| Leitura segura do XML | A leitura recusa XML com definição de tipo de documento (DTD), o que reduz riscos de segurança no processamento de arquivos enviados. |
| Gravação das notas em lote | As notas novas são gravadas de uma só vez, ao final da validação. |
| Download restrito à pasta `Temp/` | O endpoint utiliza apenas o nome do arquivo informado, impedindo o acesso a arquivos de outras pastas. |
| Swagger somente em desenvolvimento | Evita expor a documentação da API fora do ambiente de desenvolvimento. |
