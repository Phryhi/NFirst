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
}