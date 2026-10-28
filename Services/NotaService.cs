using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using NFirst.Data;

namespace NFirst.Services;

public class NotaService
{
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

        string ano = "20" + chave.Substring(2, 2);
        string mes = chave.Substring(4, 2);

        if (!int.TryParse(mes, out int numeroMes) || numeroMes < 1 || numeroMes > 12)
        {
            return null;
        }

        return $"{mes}/{ano}";
    }

    private static bool ChaveEhNfce(string chave)
    {
        if (string.IsNullOrWhiteSpace(chave) || chave.Length != 44 || !chave.All(char.IsDigit))
            return false;

        if (chave.Substring(20, 2) != "65")
            return false;

        return FormatarMesAno(chave) != null;
    }

    private static bool PodeDescartarPeloNome(string nomeArquivo)
    {
        string nome = Path.GetFileName(nomeArquivo);

        if (nome.Length < 44)
        {
            return false;
        }

        string inicio = nome.Substring(0, 44);

        if (!inicio.All(char.IsDigit))
        {
            return false;
        }
        return inicio.Substring(20, 2) != "65";
    }

    public void ProcessarArquivos(List<IFormFile> arquivos)
    {
        Console.WriteLine("Iniciando processamento");
        List<NotaTemporaria> notas = new List<NotaTemporaria>();
        string primeiroCnpj = null;
        string primeiroMesAnoChave = null;

        foreach (IFormFile arquivo in arquivos)
        {
            if (!arquivo.FileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (PodeDescartarPeloNome(arquivo.FileName))
            {
                Console.WriteLine("Descartado pelo nome: " + arquivo.FileName);
                continue;
            }

            try
            {
                NotaTemporaria nota = LerNota(arquivo);
                if (nota != null)
                {
                    notas.Add(nota);
                    if (string.IsNullOrEmpty(primeiroCnpj) && nota.chaveXml.Length >= 11)
                    {
                        primeiroCnpj = nota.chaveXml.Substring(6, 5); // 5 primeiros dígitos do CNPJ
                        string ano = "20" + nota.chaveXml.Substring(2, 2);
                        string mes = nota.chaveXml.Substring(4, 2);
                        primeiroMesAnoChave = mes + ano; // MMAAAA
                    }
                }
            }
            catch (Exception erro)
            {
                Console.WriteLine("Erro ao ler " + arquivo.FileName + ": " + erro.Message);
            }
        }

        GuardarJsonNota(notas, primeiroMesAnoChave, primeiroCnpj);
        Console.WriteLine("Fim processamento");
    }

    public NotaTemporaria LerNota(IFormFile arquivo)
    {
        using TextReader reader = new StreamReader(arquivo.OpenReadStream());
        string xml = reader.ReadToEnd();

        XDocument documento = XDocument.Parse(xml);
        XElement infNFe = documento.Descendants().FirstOrDefault(x => x.Name.LocalName == "infNFe");
        string chave = infNFe?.Attribute("Id")?.Value;

        if (chave != null && chave.StartsWith("NFe"))
        {
            chave = chave.Substring(3);
        }

        if (!ChaveEhNfce(chave))
        {
            return null;
        }

        return new NotaTemporaria { chaveXml = chave };
    }

    public void GuardarJsonNota(List<NotaTemporaria> notas, string mesAno = null, string cnpj5 = null)
    {
        Console.WriteLine("Guardando arquivo");
        string pasta = "Temp";
        Directory.CreateDirectory(pasta);

        string nomeArquivo = "notasTemp.json";
        if (!string.IsNullOrEmpty(mesAno) && !string.IsNullOrEmpty(cnpj5))
        {
            nomeArquivo = $"{mesAno}{cnpj5}.txt";
        }

        string caminho = Path.Combine(pasta, nomeArquivo);
        string json = JsonSerializer.Serialize(notas, new JsonSerializerOptions { WriteIndented = true });

        File.WriteAllText(caminho, json);
        Console.WriteLine("Arquivo guardado em: " + caminho);
    }

    public List<NotaFiscal> ValidarArquivos(List<IFormFile> arquivos, out int notasJaProcessadas)
    {
        List<NotaFiscal> notasValidas = new List<NotaFiscal>();
        notasJaProcessadas = 0;

        var chavesExistentesNoBanco = new HashSet<string>(_context.Notas.Select(n => n.Chave));

        foreach (IFormFile arquivo in arquivos)
        {
            if (!arquivo.FileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (PodeDescartarPeloNome(arquivo.FileName))
            {
                Console.WriteLine("Descartado pelo nome: " + arquivo.FileName);
                continue;
            }

            NotaFiscal nota = ValidarNota(arquivo);

            if (nota != null)
            {
                bool repetidaNaLista = notasValidas.Any(n => n.Chave == nota.Chave);
                bool existeNoBanco = chavesExistentesNoBanco.Contains(nota.Chave);

                if (!existeNoBanco && !repetidaNaLista)
                {
                    _context.Notas.Add(nota);
                    notasValidas.Add(nota);
                    chavesExistentesNoBanco.Add(nota.Chave);
                }
                else
                {
                    notasJaProcessadas++;
                    Console.WriteLine("Nota já processada: " + nota.Chave);
                }
            }
        }

        _context.SaveChanges();

        return notasValidas;
    }

    public NotaFiscal ValidarNota(IFormFile arquivo)
    {
        try
        {
            using TextReader reader = new StreamReader(arquivo.OpenReadStream());
            string xml = reader.ReadToEnd();

            XDocument documento = XDocument.Parse(xml);
            XNamespace ns = "http://www.portalfiscal.inf.br/nfe";

            XElement infNFe = documento.Descendants(ns + "infNFe").FirstOrDefault();

            if (infNFe == null)
            {
                return null;
            }

            XAttribute atributoId = infNFe.Attribute("Id");

            if (atributoId == null)
            {
                return null;
            }

            string chave = atributoId.Value;

            if (chave.StartsWith("NFe"))
            {
                chave = chave.Substring(3);
            }

            if (!ChaveEhNfce(chave))
            {
                return null;
            }

            XElement ide = infNFe.Element(ns + "ide");
            XElement mod = ide?.Element(ns + "mod");

            if (mod == null || mod.Value != "65")
            {
                return null;
            }

            XElement total = infNFe.Element(ns + "total");
            XElement icmsTot = total?.Element(ns + "ICMSTot");
            XElement vNF = icmsTot?.Element(ns + "vNF");

            if (vNF == null)
            {
                return null;
            }

            decimal valor = decimal.Parse(vNF.Value, CultureInfo.InvariantCulture);
            XElement dest = infNFe.Element(ns + "dest");

            if (dest != null)
            {
                XElement tagCpf = dest.Element(ns + "CPF");

                if (tagCpf != null)
                {
                    return null; 
                }
            }

            NotaFiscal nota = new NotaFiscal();
            nota.Chave = chave;
            nota.Valor = valor;
            nota.TipoNota = true;

            return nota;
        }
        catch (Exception erro)
        {
            Console.WriteLine("Erro ao validar o arquivo " + arquivo.FileName + ": " + erro.Message);
            return null;
        }
    }
}