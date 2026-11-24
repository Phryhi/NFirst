using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using NFirst.Data;

namespace NFirst.Services;
/// <summary>
/// Importa notas fiscais (NFC-e) a partir de arquivos XML, grava no banco de dados
/// e gera os arquivos JSON de saída.
/// </summary>
public class NotaService
{
    private const string NsNfe = "http://www.portalfiscal.inf.br/nfe";
    private const string StatusAutorizada = "100";
    private const int ModeloNfce = 65;
    private const string ExtensaoXml = ".xml";
    /// <summary>Pasta onde os arquivos JSON gerados são guardados.</summary>
    public const string PastaTemp = "Temp";

    private static readonly XName NomeMod = XName.Get("mod", NsNfe);
    private static readonly XName NomeCpf = XName.Get("CPF", NsNfe);
    private static readonly XName NomeIcmsTot = XName.Get("ICMSTot", NsNfe);
    private static readonly XName NomeVnf = XName.Get("vNF", NsNfe);
    private static readonly JsonSerializerOptions OpcoesJson = new JsonSerializerOptions { WriteIndented = true };

    private static readonly ParallelOptions OpcoesParalelo = new ParallelOptions
    {
        MaxDegreeOfParallelism = Environment.ProcessorCount
    };
    // DtdProcessing.Prohibit protege contra XML malicioso com DTD externo (ataque XXE).
    private static readonly XmlReaderSettings ConfigLeitura = new XmlReaderSettings
    {
        DtdProcessing = DtdProcessing.Prohibit,
        IgnoreWhitespace = true,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        CloseInput = false
    };

    private readonly AppDbContext _context;

    public NotaService(AppDbContext context)
    {
        _context = context;
    }

    public List<NotaFiscal> ListarNotas()
    {
        return _context.Notas.ToList();
    }

    public NotaFiscal SalvarNota(NotaFiscal nota)
    {
        _context.Notas.Add(nota);
        _context.SaveChanges();

        return nota;
    }
    // Mantém só arquivos .xml e já descarta, pelo nome, os que não são NFC-e,
    // para não gastar tempo abrindo-os.
    private static List<IFormFile> SelecionarCandidatos(List<IFormFile> arquivos, out int descartadosPeloNome)
    {
        List<IFormFile> candidatos = new List<IFormFile>(arquivos.Count);
        descartadosPeloNome = 0;

        foreach (IFormFile arquivo in arquivos)
        {
            if (!arquivo.FileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (ChaveNfce.PodeDescartarPeloNome(arquivo.FileName))
            {
                descartadosPeloNome++;
                continue;
            }

            candidatos.Add(arquivo);
        }

        return candidatos;
    }
    private static MemoryStream CopiarParaMemoria(IFormFile arquivo)
    {
        using Stream origem = arquivo.OpenReadStream();
        MemoryStream copia = new MemoryStream((int)Math.Min(arquivo.Length, int.MaxValue));
        origem.CopyTo(copia);
        copia.Position = 0;

        return copia;
    }
    // Sem trava: lê direto do upload (uso sequencial).
    // Com trava: copia para memória dentro do lock, porque o stream do IFormFile
    // não é seguro para leituras concorrentes. Depois da cópia, o parse roda em paralelo.
    private static Stream AbrirXml(IFormFile arquivo, object travaLeituraArquivo)
    {
        if (travaLeituraArquivo == null)
        {
            return arquivo.OpenReadStream();
        }

        lock (travaLeituraArquivo)
        {
            return CopiarParaMemoria(arquivo);
        }
    }

    /// <summary>
    /// Gera os arquivos JSON de saída a partir de notas já validadas.
    /// </summary>
    /// <param name="notasValidas">Notas já aprovadas por <see cref="ProcessarArquivos"/>.</param>
    /// <returns>
    /// Os nomes dos arquivos gerados, dentro de <see cref="PastaTemp"/>.
    /// A lista vem vazia se não houver notas.
    /// </returns>
    /// <remarks>
    /// Gera um arquivo <c>mmaaaaxxxxx.txt</c> por combinação de mês/ano de emissão
    /// com os 5 primeiros dígitos do CNPJ. Cada arquivo contém chave, série e mês/ano.
    /// Arquivos com o mesmo nome são sobrescritos.
    /// </remarks>
    public List<string> GerarArquivosJson(List<NotaFiscal> notasValidas)
    {
        List<string> arquivosGerados = new List<string>();

        if (notasValidas.Count == 0)
        {
            return arquivosGerados;
        }

        string pasta = "Temp";
        Directory.CreateDirectory(pasta);

        foreach (IGrouping<string, NotaFiscal> grupo in notasValidas.GroupBy(
                     n => n.Chave.Substring(4, 2) + "20" + n.Chave.Substring(2, 2) + n.Chave.Substring(6, 5)))
        {
            string nomeArquivo = grupo.Key + ".txt";
            var itens = grupo.Select(n => new { chave = n.Chave, serie = 65, mesAno = ChaveNfce.FormatarMesAno(n.Chave)});
            string json = JsonSerializer.Serialize(itens, OpcoesJson);

            string caminho = Path.Combine(pasta, nomeArquivo);
            File.WriteAllText(caminho, json);
            arquivosGerados.Add(nomeArquivo);

            Console.WriteLine("Arquivo guardado em: " + caminho);
        }

        return arquivosGerados;
    }
    /// <summary>
    /// Lê os XMLs enviados e grava no banco as NFC-e novas e válidas.
    /// </summary>
    /// <param name="arquivos">Arquivos enviados. Os que não forem .xml são ignorados.</param>
    /// <param name="quantidadeJaProcessadas">
    /// Saída: quantas notas válidas foram ignoradas por já existirem no banco
    /// ou aparecerem repetidas no próprio envio.
    /// </param>
    /// <returns>As notas novas, já gravadas no banco.</returns>
    /// <remarks>
    /// Atenção: além de validar, este método <b>grava no banco</b>. Notas inválidas
    /// (não autorizadas, com CPF do destinatário, sem valor etc.) são descartadas
    /// sem erro.
    /// </remarks>
    public List<NotaFiscal> ProcessarArquivos(List<IFormFile> arquivos, out int quantidadeJaProcessadas)
    {
        Stopwatch cronometro = Stopwatch.StartNew();
        List<NotaFiscal> notasValidas = new List<NotaFiscal>();
        quantidadeJaProcessadas = 0;

        List<IFormFile> candidatos = SelecionarCandidatos(arquivos, out int descartadosPeloNome);

        if (candidatos.Count == 0)
        {
            return notasValidas;
        }
        // Carrega as chaves do banco enquanto os XMLs são lidos.
        Task<HashSet<string>> carregarChaves = Task.Run(
            () => new HashSet<string>(_context.Notas.Select(n => n.Chave)));

        NotaFiscal[]notasLidas= new NotaFiscal[candidatos.Count];
        object travaLeituraArquivo = new object();

        Parallel.For(0, candidatos.Count, OpcoesParalelo, i =>
        {
            notasLidas[i] = LerNota(candidatos[i], travaLeituraArquivo);
        });

        HashSet<string> chavesExistentes = carregarChaves.GetAwaiter().GetResult();
        int repetidas = 0;
        foreach (NotaFiscal nota in notasLidas)
        {
            if (nota == null)
            {
                continue;
            }
            // Add devolve false se a chave já existe, o que cobre tanto o banco
            // quanto repetições dentro do mesmo envio.
            if (chavesExistentes.Add(nota.Chave))
            {
                notasValidas.Add(nota);
            }
            else
            {
                repetidas++;
            }
        }

        if (notasValidas.Count > 0)
        {
            _context.Notas.AddRange(notasValidas);
            _context.SaveChanges();
        }

        quantidadeJaProcessadas = repetidas;

        Console.WriteLine($"Validação: {notasValidas.Count} novas, {repetidas} já processadas, {descartadosPeloNome} descartadas pelo nome, {cronometro.ElapsedMilliseconds} ms");

        return notasValidas;
    }
    /// <summary>
    /// Lê um único arquivo XML e devolve a nota correspondente.
    /// </summary>
    /// <param name="arquivo">Arquivo XML enviado.</param>
    /// <returns>A nota, ou <c>null</c> se o arquivo for inválido ou não puder ser lido.</returns>
    /// <remarks>Não grava no banco.</remarks>
    public NotaFiscal LerNota(IFormFile arquivo)
    {
        return LerNota(arquivo, null);
    }

    private NotaFiscal LerNota(IFormFile arquivo, object travaLeituraArquivo)
    {
        try
        {
            using Stream stream = AbrirXml(arquivo, travaLeituraArquivo);

            return LerNotaXml(stream);
        }
        // Um arquivo ruim não pode derrubar o lote inteiro: registramos o erro e seguimos.
        catch (Exception erro)
        {
            Console.WriteLine("Erro ao validar o arquivo " + arquivo.FileName + ": " + erro.Message);
            return null;
        }
    }
    // Devolve a nota se for uma NFC-e autorizada (cStat 100), modelo 65, sem CPF do
    // destinatário e com valor numérico válido. Devolve null se alguma regra falhar.
    private static NotaFiscal LerNotaXml(Stream stream)
    {
        using XmlReader reader = XmlReader.Create(stream, ConfigLeitura);
        XDocument documento = XDocument.Load(reader);

        XElement infNfe = documento.Descendants(XName.Get("infNFe", NsNfe)).FirstOrDefault();

        if (infNfe == null)
        {
            return null;
        }

        string chave = infNfe.Attribute("Id")?.Value;

        if (chave == null)
        {
            return null;
        }
        // No XML, o atributo Id vem com o prefixo "NFe" antes dos 44 dígitos.
        if (chave.StartsWith("NFe", StringComparison.Ordinal))
        {
            chave = chave.Substring(3);
        }

        if (!ChaveNfce.EhNfce(chave))
        {
            return null;
        }

        XElement infProt = documento.Descendants(XName.Get("infProt", NsNfe)).FirstOrDefault();

        string cStat = infProt?.Element(XName.Get("cStat", NsNfe))?.Value;
        // 100 = "Autorizado o uso da NF-e".
        if (cStat != "100")
        {
            return null;
        }

        XElement ide = infNfe.Element(XName.Get("ide", NsNfe));
        string mod = ide?.Element(NomeMod)?.Value;

        if (mod != "65")
        {
            return null;
        }

        XElement dest = infNfe.Element(XName.Get("dest", NsNfe));
        // Só importamos notas sem CPF do destinatário (consumidor não identificado).
        if (dest?.Element(NomeCpf) != null)
        {
            return null;
        }

        XElement total = infNfe.Element(XName.Get("total", NsNfe));
        string textoValor = total?.Element(NomeIcmsTot)?.Element(NomeVnf)?.Value;
        // InvariantCulture: o XML usa ponto como separador decimal, independente da
        // cultura do servidor.
        if (textoValor == null ||
            !decimal.TryParse(textoValor, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal valor))
        {
            return null;
        }

        NotaFiscal nota = new NotaFiscal();
        nota.Chave = chave;
        nota.Valor = valor;
        nota.TipoNota = true;

        return nota;
    }
}