using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using NFirst.Data;

namespace NFirst.Services;

public class NotaService
{
    private const string NsNfe = "http://www.portalfiscal.inf.br/nfe";
    private const bool ValidarDigitoVerificador = true;

    private static readonly XName NomeMod = XName.Get("mod", NsNfe);
    private static readonly XName NomeCpf = XName.Get("CPF", NsNfe);
    private static readonly XName NomeIcmsTot = XName.Get("ICMSTot", NsNfe);
    private static readonly XName NomeVnf = XName.Get("vNF", NsNfe);
    private static readonly JsonSerializerOptions OpcoesJson = new JsonSerializerOptions { WriteIndented = true };

    private static readonly ParallelOptions OpcoesParalelo = new ParallelOptions
    {
        MaxDegreeOfParallelism = Environment.ProcessorCount
    };
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

    public List<NotaFiscal> Listar()
    {
        return _context.Notas.ToList();
    }

    public NotaFiscal Enviar(NotaFiscal nota)
    {
        _context.Notas.Add(nota);
        _context.SaveChanges();

        return nota;
    }
    private static string FormatarMesAno(string chave)
    {
        if (string.IsNullOrWhiteSpace(chave) || chave.Length != 44)
        {
            return null;
        }

        for (int i = 2; i <= 5; i++)
        {
            if (chave[i] < '0' || chave[i] > '9')
            {
                return null;
            }
        }

        int numeroMes = (chave[4] - '0') * 10 + (chave[5] - '0');

        if (numeroMes < 1 || numeroMes > 12)
        {
            return null;
        }

        string ano = "20" + chave.Substring(2, 2);
        string mes = chave.Substring(4, 2);

        return $"{mes}/{ano}";
    }
    private static bool UfValida(string chave)
    {
        int uf = (chave[0] - '0') * 10 + (chave[1] - '0');

        return uf is 11 or 12 or 13 or 14 or 15 or 16 or 17
                  or 21 or 22 or 23 or 24 or 25 or 26 or 27 or 28 or 29
                  or 31 or 32 or 33 or 35
                  or 41 or 42 or 43
                  or 50 or 51 or 52 or 53;
    }
    private static bool DigitoVerificadorValido(string chave)
    {
        int soma = 0;
        int peso = 2;

        for (int i = 42; i >= 0; i--)
        {
            soma += (chave[i] - '0') * peso;
            peso = peso == 9 ? 2 : peso + 1;
        }

        int resto = soma % 11;
        int dv = resto < 2 ? 0 : 11 - resto;

        return chave[43] - '0' == dv;
    }

    private static bool ChaveEhNfce(string chave)
    {
        if (string.IsNullOrWhiteSpace(chave) || chave.Length != 44)
            return false;

        for (int i = 0; i < 44; i++)
        {
            if (chave[i] < '0' || chave[i] > '9')
                return false;
        }

        if (chave[20] != '6' || chave[21] != '5')
            return false;

        if (!UfValida(chave))
            return false;

        if (FormatarMesAno(chave) == null)
            return false;

        return !ValidarDigitoVerificador || DigitoVerificadorValido(chave);
    }
    private static bool PodeDescartarPeloNome(string nomeArquivo)
    {
        int separador = nomeArquivo.AsSpan().LastIndexOfAny('/', '\\');
        ReadOnlySpan<char> nome = nomeArquivo.AsSpan(separador + 1);

        if (nome.StartsWith("NFe", StringComparison.OrdinalIgnoreCase))
        {
            nome = nome.Slice(3);
        }

        if (nome.Length < 44)
        {
            return false;
        }

        for (int i = 0; i < 44; i++)
        {
            if (nome[i] < '0' || nome[i] > '9')
            {
                return false;
            }
        }

        return nome[20] != '6' || nome[21] != '5';
    }

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

            if (PodeDescartarPeloNome(arquivo.FileName))
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
    private static Stream AbrirXml(IFormFile arquivo, object trava)
    {
        if (trava == null)
        {
            return arquivo.OpenReadStream();
        }

        lock (trava)
        {
            return CopiarParaMemoria(arquivo);
        }
    }

    // Gera o JSON sequencial a partir das notas JÁ VALIDADAS.
    // Gera um arquivo mmaaaaxxxxx.txt por combinação de mês/ano + 5 primeiros dígitos do CNPJ.
    // Devolve os nomes dos arquivos gerados (lista vazia se não houver notas).
    public List<string> GuardarJsonNota(List<NotaFiscal> notasValidas)
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
            var itens = grupo.Select((n, i) => new { sequencia = i + 1, chaveXml = n.Chave });
            string json = JsonSerializer.Serialize(itens, OpcoesJson);

            string caminho = Path.Combine(pasta, nomeArquivo);
            File.WriteAllText(caminho, json);
            arquivosGerados.Add(nomeArquivo);

            Console.WriteLine("Arquivo guardado em: " + caminho);
        }

        return arquivosGerados;
    }
    public List<NotaFiscal> ValidarArquivos(List<IFormFile> arquivos, out int notasJaProcessadas)
    {
        Stopwatch cronometro = Stopwatch.StartNew();
        List<NotaFiscal> notasValidas = new List<NotaFiscal>();
        notasJaProcessadas = 0;

        List<IFormFile> candidatos = SelecionarCandidatos(arquivos, out int descartadosPeloNome);

        if (candidatos.Count == 0)
        {
            return notasValidas;
        }
        Task<HashSet<string>> carregarChaves = Task.Run(
            () => new HashSet<string>(_context.Notas.Select(n => n.Chave)));

        NotaFiscal[] lidas = new NotaFiscal[candidatos.Count];
        object trava = new object();

        Parallel.For(0, candidatos.Count, OpcoesParalelo, i =>
        {
            lidas[i] = ValidarNota(candidatos[i], trava);
        });

        HashSet<string> chavesExistentes = carregarChaves.GetAwaiter().GetResult();
        int repetidas = 0;
        foreach (NotaFiscal nota in lidas)
        {
            if (nota == null)
            {
                continue;
            }

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

        notasJaProcessadas = repetidas;

        Console.WriteLine($"Validação: {notasValidas.Count} novas, {repetidas} já processadas, {descartadosPeloNome} descartadas pelo nome, {cronometro.ElapsedMilliseconds} ms");

        return notasValidas;
    }

    public NotaFiscal ValidarNota(IFormFile arquivo)
    {
        return ValidarNota(arquivo, null);
    }

    private NotaFiscal ValidarNota(IFormFile arquivo, object trava)
    {
        try
        {
            using Stream stream = AbrirXml(arquivo, trava);

            return ValidarNotaXml(stream);
        }
        catch (Exception erro)
        {
            Console.WriteLine("Erro ao validar o arquivo " + arquivo.FileName + ": " + erro.Message);
            return null;
        }
    }
    private static NotaFiscal ValidarNotaXml(Stream stream)
    {
        using XmlReader reader = XmlReader.Create(stream, ConfigLeitura);

        if (!reader.ReadToFollowing("infNFe", NsNfe))
        {
            return null;
        }

        string chave = reader.GetAttribute("Id");

        if (chave == null)
        {
            return null;
        }

        if (chave.StartsWith("NFe", StringComparison.Ordinal))
        {
            chave = chave.Substring(3);
        }

        if (!ChaveEhNfce(chave))
        {
            return null;
        }

        if (reader.IsEmptyElement)
        {
            return null;
        }

        int profundidade = reader.Depth;
        bool modeloOk = false;
        bool temValor = false;
        decimal valor = 0;

        reader.Read();

        while (!reader.EOF && reader.Depth > profundidade)
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                reader.Read();
                continue;
            }

            string nome = reader.NamespaceURI == NsNfe ? reader.LocalName : null;

            if (nome == "ide")
            {
                XElement ide = (XElement)XNode.ReadFrom(reader);
                string mod = ide.Element(NomeMod)?.Value;

                if (mod != "65")
                {
                    return null;
                }

                modeloOk = true;
            }
            else if (nome == "dest")
            {
                XElement dest = (XElement)XNode.ReadFrom(reader);

                if (dest.Element(NomeCpf) != null)
                {
                    return null;
                }
            }
            else if (nome == "total")
            {
                XElement total = (XElement)XNode.ReadFrom(reader);
                string textoValor = total.Element(NomeIcmsTot)?.Element(NomeVnf)?.Value;

                if (textoValor == null ||
                    !decimal.TryParse(textoValor, NumberStyles.Number, CultureInfo.InvariantCulture, out valor))
                {
                    return null;
                }

                temValor = true;
            }
            else
            {
                reader.Skip();
            }
        }

        if (!modeloOk || !temValor)
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