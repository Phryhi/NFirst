# NFirst

## Sobre 
O NFirst é uma API ASP.NET Core que tem como finalidade receber arquivos XML de notas fiscais e identificar quais são válidas para doação, isto é, as NFC-e emitidas sem CPF do consumidor. As notas válidas são armazenadas no banco de
dados e retornadas em formato JSON, assim facilitando a doação das notas a instituições de caridade.

## Funcionalidades

| Funcionalidade | Descrição |
| :--- | :--- |
| Recebimento de arquivos | Recebe um ou mais arquivos XML de notas fiscais e ignora os que não têm extensão `.xml`. |
| Validação da chave de acesso | Verifica se a chave possui 44 dígitos, se é do modelo 65 (NFC-e), se a UF e o mês são válidos e se o dígito verificador está correto. |
| Identificação de notas válidas | Lê o conteúdo do XML e considera válida a NFC-e emitida sem CPF do consumidor, extraindo o valor total da nota. |
| Controle de duplicidade | Identifica notas já processadas e evita que sejam armazenadas novamente. |
| Armazenamento | Salva no banco de dados a chave e o valor total das notas válidas. |
| Exportação das chaves | Gera arquivos em formato JSON com as chaves das notas válidas, disponíveis para download. |

## Tecnologias

| Tecnologia | Versão |
| :--- | :--- |
| C# | 14.0 |
| .NET | 10.0 |
| ASP.NET Core | 10.0 |
| Entity Framework Core | 10.0.12 |
| SQLite (Microsoft.EntityFrameworkCore.Sqlite) | 10.0.12 |
| Swagger (Swashbuckle.AspNetCore) | 9.0.6 |

## Pré-requisitos

Antes de começar, é necessário ter instalado:

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0): plataforma usada para compilar e executar o projeto.
- [Git](https://git-scm.com/downloads): necessário para clonar o repositório.

Para verificar se as instalações foram concluídas, execute no terminal:

```bash
dotnet --version
git --version
```

O comando `dotnet --version` deve retornar uma versão iniciada em `10.`.

Não é necessário instalar servidor de banco de dados: o projeto utiliza SQLite, e o arquivo do banco (`nfirst.db`) é criado automaticamente na primeira execução.

Opcionalmente, recomenda-se um editor de código, como o [Visual Studio Code](https://code.visualstudio.com/) ou o [Visual Studio](https://visualstudio.microsoft.com/).

## Instalação

1. Clone o repositório:

   ```bash
   git clone https://github.com/Phryhi/NFirst.git
   cd NFirst
   ```

2. Execute a aplicação:

   ```bash
   dotnet run
   ```

   Na primeira execução, o banco de dados (`nfirst.db`) é criado e as migrations são aplicadas automaticamente.

3. Acesse a aplicação no navegador:

   | Recurso | Endereço |
   | :--- | :--- |
   | Interface web (frontend) | http://localhost:5189 |
   | Documentação da API (Swagger) | http://localhost:5189/swagger |

## Como usar

1. Acesse `http://localhost:5189`.
2. Clique em **Selecionar pasta** e escolha a pasta que contém os arquivos XML das notas fiscais.
3. Clique em **Enviar pasta**. A aplicação processa os arquivos e ignora os que não possuem extensão `.xml`.
4. Ao final do processamento, é exibida uma mensagem com o resumo do envio e, havendo notas novas válidas, o resultado em JSON com as chaves de acesso. Utilize **Copiar JSON** para copiar o conteúdo.
5. Clique em **Baixar TXT** para baixar os arquivos com as chaves das notas válidas. Podem ser gerados vários arquivos, agrupados por mês e ano de emissão.

> São consideradas válidas as NFC-e emitidas sem CPF do consumidor. Notas já processadas anteriormente não são salvas novamente, portanto não aparecem no JSON nem nos arquivos para download.

Os endpoints da API estão documentados no Swagger, em `http://localhost:5189/swagger`.

## Estrutura do projeto

```text
NFirst/
├── Controllers/
│   └── NotaController.cs       # Endpoints da API
├── Data/
│   └── AppDbContext.cs         # Contexto do banco de dados
├── Migrations/                 # Histórico de alterações do banco (Entity Framework Core)
├── Models/
│   ├── NotaModel.cs            # Entidade da nota fiscal
│   └── NotaTemporariaModel.cs  # Modelo auxiliar com a chave de acesso
├── Properties/
│   └── launchSettings.json     # Perfis de execução
├── Services/
│   └── NotaService.cs          # Regras de negócio e validação dos XMLs
├── wwwroot/
│   ├── index.html              # Interface web
│   ├── script.js               # Lógica do frontend
│   └── style.css               # Estilos
├── appsettings.json            # Configurações da aplicação
├── NFirst.csproj               # Arquivo do projeto e dependências
├── NFirst.http                 # Exemplos de requisições HTTP
└── Program.cs                  # Ponto de entrada e configuração
```

Itens gerados durante a execução:

| Item | Descrição |
| :--- | :--- |
| Banco de dados | Banco SQLite local, criado automaticamente na primeira execução |
| `Temp/` | Arquivos `.txt` com as chaves das notas válidas, disponíveis para download |

## Regras de negócio

Uma nota fiscal é considerada válida e armazenada somente se atender a todos os critérios abaixo:

| Ordem | Critério | Descrição |
| :---: | :--- | :--- |
| 1 | Formato do arquivo | O arquivo deve possuir extensão `.xml`. |
| 2 | Chave de acesso | A chave deve ter estrutura válida e pertencer a uma NFC-e (modelo 65). |
| 3 | Modelo da nota | O modelo informado dentro do XML deve ser 65 (NFC-e). |
| 4 | Consumidor | A nota não pode conter CPF do consumidor. |
| 5 | Valor total | O valor total da nota deve estar presente e ser numérico. |
| 6 | Duplicidade | A chave não pode já ter sido processada anteriormente. |

Notas que não atendem aos critérios 1 a 5 são descartadas. Notas que falham apenas no critério 6 são contabilizadas como já processadas e não são salvas novamente.

Os detalhes de cada regra estão em [docs/regras-de-negocio.md](docs/regras-de-negocio.md).
