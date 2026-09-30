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
    public void ProcessarArquivos(List<IFormFile> arquivos)
    {
        Console.WriteLine("Iniciando processamento");
        List<NotaTemporaria> notas = new List<NotaTemporaria>();

        foreach (IFormFile arquivo in arquivos)
        {
            NotaTemporaria nota = LerNota(arquivo);
            notas.Add(nota);
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

        string chave = infNFe?.Attribute("Id")?.Value.Replace("NFe", "");

        Console.WriteLine("Fim leitura");
        return new NotaTemporaria{chaveXml = chave};
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
    public List<NotaFiscal> ValidarArquivos(List<IFormFile> arquivos)
    {
        List<NotaFiscal> notasValidas = new List<NotaFiscal>();

        foreach (IFormFile arquivo in arquivos)
        {
            NotaFiscal nota = ValidarNota(arquivo);

            if (nota != null)
            {
                _context.Notas.Add(nota);
                notasValidas.Add(nota);
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

            string cpf = null;
            XElement dest = infNFe.Element(ns + "dest");

            if (dest != null)
            {
                XElement tagCpf = dest.Element(ns + "CPF");

                if (tagCpf != null)
                {
                    cpf = tagCpf.Value;
                }
            }

            NotaFiscal nota = new NotaFiscal();
            nota.Chave = chave;
            nota.Cpf = cpf;
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