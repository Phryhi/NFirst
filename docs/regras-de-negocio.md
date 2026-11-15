# Regras de negócios

## Visão geral do fluxo

O fluxo da API é composto por cinco etapas, executadas em ordem:

1. **Seleção dos arquivos:** a API recebe os arquivos enviados e mantém apenas os candidatos à validação (extensão `.xml` e nome compatível com NFC-e).
2. **Validação de cada nota:** para cada arquivo, a API verifica a chave de acesso, a autorização da nota e o conteúdo do XML (modelo, ausência de CPF do consumidor e valor total). Os arquivos são validados em paralelo.
3. **Controle de duplicidade:** notas com chave já processada não são armazenadas novamente.
4. **Armazenamento:** as notas válidas e novas são salvas no banco de dados.
5. **Exportação das chaves:** as chaves das notas válidas são gravadas em arquivos para download.
   
## 1. Seleção dos arquivos

Antes de abrir qualquer XML, a API seleciona os arquivos que seguem para a validação, aplicando dois filtros em ordem.

### 1.1 Extensão do arquivo

Somente arquivos com extensão `.xml` são considerados, sem diferenciar maiúsculas de minúsculas. Os demais arquivos da pasta são ignorados.

### 1.2 Descarte pelo nome do arquivo

Arquivos XML de notas fiscais costumam ser nomeados com a chave de acesso (44 dígitos), com ou sem o prefixo `NFe`. Quando o nome segue esse padrão, a API consulta o modelo informado nas posições 21 e 22 e descarta o arquivo, sem abri-lo, se o modelo não for `65` (NFC-e).

| Situação do nome | Resultado |
| :--- | :--- |
| 44 dígitos e modelo `65` | Segue para a validação |
| 44 dígitos e modelo diferente de `65` | Descartado sem leitura do conteúdo |
| Fora do padrão (menos de 44 dígitos ou com outros caracteres) | Segue para a validação |

Esse filtro é uma otimização: evita ler arquivos que já se sabe que não são NFC-e, como NF-e de modelo 55. Ele não substitui a validação da chave de acesso, feita na etapa seguinte a partir do conteúdo do XML.

## 2. Validação da chave de acesso

A chave de acesso é lida do atributo `Id` da nota, com o prefixo `NFe` removido. Ela precisa ter 44 dígitos numéricos, organizados assim:

| Posição | Tamanho | Campo |
| :---: | :---: | :--- |
| 1-2 | 2 | Código da UF |
| 3-6 | 4 | Ano e mês de emissão (AAMM) |
| 7-20 | 14 | CNPJ do emitente |
| 21-22 | 2 | Modelo do documento |
| 23-25 | 3 | Série |
| 26-34 | 9 | Número da nota |
| 35 | 1 | Forma de emissão |
| 36-43 | 8 | Código numérico |
| 44 | 1 | Dígito verificador |

A chave é aceita somente se passar por todas as verificações listadas abaixo:

1. Possui exatamente 44 caracteres, todos numéricos.
2. O modelo (posições 21-22) é `65`, que identifica a NFC-e.
3. O código da UF corresponde a um estado brasileiro ou ao Distrito Federal.
4. O mês de emissão está entre `01` e `12`.
5. O dígito verificador está correto (módulo 11).

### Cálculo do dígito verificador

1. Percorra os 43 primeiros dígitos da direita para a esquerda, multiplicando cada um por um peso que começa em 2 e vai até 9, voltando a 2 depois do 9.
2. Some os resultados.
3. Calcule o resto da divisão da soma por 11.
4. Se o resto for 0 ou 1, o dígito verificador é `0`. Nos demais casos, é `11` menos o resto.
5. O resultado deve ser igual ao 44º dígito da chave.

## 3. Autorização da nota

Depois da chave, a API confere se a nota foi autorizada pela SEFAZ (Secretária da Fazenda). Essa informação fica no protocolo de autorização (`infProt`), que acompanha o XML da nota.

A nota segue para a próxima etapa somente se o campo `cStat` do protocolo for `100` (autorizado o uso da NF-e). Se o protocolo não existir ou tiver outro status, a nota é descartada.

## 4. Validação do conteúdo do XML

Com a chave e a autorização aprovadas, a API confere três informações dentro do bloco principal da nota (`infNFe`):

| Ordem | Verificação | Condição para aprovar |
| :---: | :--- | :--- |
| 1 | Modelo (`ide/mod`) | Deve ser `65` (NFC-e). |
| 2 | Destinatário (`dest`) | Não pode conter o campo `CPF`. |
| 3 | Valor total (`total/ICMSTot/vNF`) | Deve existir e ser um número válido, com ponto como separador decimal. |

Se qualquer verificação falhar, a nota é descartada. Se todas passarem, a API registra a chave e o valor total da nota.

## 5. Controle de duplicidade

Depois que todos os arquivos são validados, a API compara a chave de cada nota aprovada com as chaves já gravadas no banco de dados e com as demais notas do mesmo envio.

| Situação | Resultado |
| :--- | :--- |
| Chave ainda não existe | A nota é aceita e segue para o armazenamento. |
| Chave já existe no banco | A nota é contabilizada como já processada e não é salva novamente. |
| Mesma chave em mais de um arquivo do envio | A primeira ocorrência é aceita, e as demais são contabilizadas como já processadas. |

As notas já processadas não aparecem no JSON de resultado nem nos arquivos de exportação. O número delas é informado na mensagem de resumo do envio.

## 6. Armazenamento

As notas aprovadas e que ainda não existem no banco são gravadas de uma só vez, ao final da validação. Para cada nota, são salvos a chave de acesso, o valor total e o indicador de tipo da nota.

Se nenhuma nota nova for aprovada, nada é gravado.

## 7. Exportação das chaves

Depois do armazenamento, a API gera arquivos com as chaves das notas aprovadas no envio. Notas já processadas não entram.

### Agrupamento e nome dos arquivos

Os arquivos são agrupados por mês e ano de emissão e pelos cinco primeiros dígitos do CNPJ do emitente. Cada grupo gera um arquivo `.txt` na pasta `Temp/`, nomeado como `MMAAAA` seguido dos cinco dígitos.

| Posições da chave | Uso no nome do arquivo |
| :---: | :--- |
| 5-6 | Mês (`MM`) |
| 3-4 | Ano (`AAAA`, com o prefixo `20`) |
| 7-11 | Cinco primeiros dígitos do CNPJ |

### Conteúdo dos arquivos

Cada arquivo contém uma lista em formato JSON, com um item por nota:

| Campo | Descrição |
| :--- | :--- |
| `chave` | Chave de acesso da nota |
| `serie` | Valor fixo `65` |
| `mesAno` | Mês e ano de emissão, no formato `MM/AAAA` |

### Download

Os arquivos ficam disponíveis pelo endpoint `GET /Notas/Download/{nome}`, que devolve o arquivo da pasta `Temp/` ou o erro 404 se ele não existir. Na interface web, o botão **Baixar TXT** baixa todos os arquivos gerados no envio.

## Resultado para cada nota

| Situação | Resultado |
| :--- | :--- |
| Arquivo sem extensão `.xml` | Ignorado. |
| Nome com 44 dígitos e modelo diferente de `65` | Descartado, sem leitura do conteúdo. |
| XML ilegível ou sem os dados principais da nota (`infNFe` e `Id`) | Descartada. |
| Chave de acesso inválida | Descartada. |
| Protocolo de autorização ausente ou com status diferente de `100` | Descartada. |
| Modelo do XML diferente de `65` | Descartada. |
| Destinatário com CPF | Descartada. |
| Valor total ausente ou inválido | Descartada. |
| Chave já existente no banco ou repetida no mesmo envio | Contabilizada como já processada e não é salva novamente. |
| Todas as verificações aprovadas e chave inédita | Salva no banco e incluída nos arquivos de exportação. |

## Observações

| Tema | Descrição |
| :--- | :--- |
| Destinatário | A nota sem o bloco de destinatário, ou identificada apenas por CNPJ, é aceita. Somente a presença de CPF a rejeita. |
| Autorização | Somente o status `100` é aceito. O cancelamento posterior de uma nota não é verificado. |
| Arquivos descartados | Os arquivos descartados e os erros de leitura não são informados ao usuário e ficam registrados apenas no console da API. A mensagem de resumo conta todos os arquivos recebidos, inclusive os ignorados. |
| Notas já processadas | A mensagem de resumo usa a mesma expressão para chaves já existentes no banco e para chaves repetidas dentro do próprio envio. |
| Exportação | O arquivo é gravado com o mesmo nome para o mesmo mês, ano e CNPJ. Um novo envio com notas desse grupo substitui o arquivo anterior, que passa a conter apenas as notas do último envio. As notas antigas permanecem no banco. |
| Pasta `Temp/` | Os arquivos exportados não são apagados automaticamente. |
| Dígito verificador | A verificação é controlada pela constante `ValidarDigitoVerificador`, atualmente ativa. |
| Campo `serie` | Possui valor fixo `65`, que corresponde ao modelo da nota e não à série informada na chave. |
| Crédito | A API não calcula o crédito. Armazena apenas o valor total da nota. |
