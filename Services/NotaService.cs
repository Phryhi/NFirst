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

        if (!int.TryParse(mes, out int numeroMes) ||
            numeroMes < 1 || numeroMes > 12)
        {
            return null;
        }

        return $"{mes}/{ano}";
    }
    private static bool ChaveEhNfce(string chave)
    {
        if (string.IsNullOrWhiteSpace(chave))
            return false;

        if (chave.Length != 44 || !chave.All(char.IsDigit))
            return false;

        if (chave.Substring(20, 2) != "65")
            return false;

        if (FormatarMesAno(chave) == null)
            return false;

        return true;
    }
    public void ProcessarArquivos(List<IFormFile> arquivos)
    {
        Console.WriteLine("Iniciando processamento");
        List<NotaTemporaria> notas = new List<NotaTemporaria>();

        foreach (IFormFile arquivo in arquivos)
        {
            if (!arquivo.FileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                NotaTemporaria nota = LerNota(arquivo);
                if (nota != null)
                {
                    notas.Add(nota);
                }
            }
            catch (Exception erro)
            {
                Console.WriteLine("Erro ao ler " + arquivo.FileName + ": " + erro.Message);
            }
        }

        GuardarJsonNota(notas);
        Console.WriteLine("Fim processamento");
    }
    public NotaTemporaria LerNota(IFormFile arquivo)
    {
        Console.WriteLine("Iniciando leitura");
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
            Console.WriteLine("chave inválida ou modelo diferente de 65");
            return null;
        }

        Console.WriteLine("Fim leitura");
        return new NotaTemporaria { chaveXml = chave };
    }
    public void GuardarJsonNota(List<NotaTemporaria> notas)
    {
        Console.WriteLine("Guardando arquivo");
        string pasta = "Temp";
        Directory.CreateDirectory(pasta);
        string caminho = Path.Combine(pasta, "notasTemp.json");
        string json = JsonSerializer.Serialize(notas);

        File.WriteAllText(caminho, json);
        Console.WriteLine("Arquivo guardado");
    }
    public List<NotaFiscal> ValidarArquivos(List<IFormFile> arquivos, out int notasJaProcessadas)
    {
        List<NotaFiscal> notasValidas = new List<NotaFiscal>();
        notasJaProcessadas = 0;

        foreach (IFormFile arquivo in arquivos)
        {
            NotaFiscal nota = ValidarNota(arquivo);

            if (nota != null)
            {
                Console.WriteLine("Chave encontrada: " + nota.Chave);
                Console.WriteLine("Existe no banco: " +
                    _context.Notas.Any(n => n.Chave == nota.Chave));
                bool existe = _context.Notas.Any(n => n.Chave == nota.Chave);
                bool repetidaNaLista = notasValidas.Any(n => n.Chave == nota.Chave);

                if (!existe && !repetidaNaLista)
                {
                    _context.Notas.Add(nota);
                    notasValidas.Add(nota);
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

            XElement infNFe = null;

            foreach (XElement elemento in documento.Descendants(ns + "infNFe"))
            {
                infNFe = elemento;
                break;
            }

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

            string anoMes = chave.Substring(2, 4);
            string mesAno = anoMes.Substring(2, 2) + "/20" + anoMes.Substring(0, 2);

            XElement ide = infNFe.Element(ns + "ide");

            if (ide == null)
            {
                return null;
            }

            XElement mod = ide.Element(ns + "mod");

            if (mod == null)
            {
                return null;
            }

            if (mod.Value != "65")
            {
                return null;
            }

            XElement total = infNFe.Element(ns + "total");

            if (total == null)
            {
                return null;
            }

            XElement icmsTot = total.Element(ns + "ICMSTot");

            if (icmsTot == null)
            {
                return null;
            }

            XElement vNF = icmsTot.Element(ns + "vNF");

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