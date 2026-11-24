using Microsoft.AspNetCore.Mvc;
using NFirst.Services;

namespace NFirst.Controllers;
/// <summary>
/// Endpoints para consultar, cadastrar e importar notas fiscais (NFC-e).
/// </summary>
[ApiController]
[Route("[controller]")]
public class NotasController : ControllerBase
{
    private readonly NotaService _service;

    public NotasController(NotaService service)
    {
        _service = service;
    }
    /// <summary>Lista todas as notas cadastradas.</summary>
    [HttpGet]
    public IActionResult ListarNotas()
    {
        var notas = _service.ListarNotas();

        return Ok(notas);
    }
    /// <summary>Lista todas as notas cadastradas.</summary>
    [HttpPost]
    public IActionResult SalvarNota(NotaFiscal nota)
    {
        var resultado = _service.SalvarNota(nota);

        return Ok(resultado);
    }
    /// <summary>
    /// Importa XMLs de NFC-e, grava as notas novas e gera os arquivos JSON.
    /// </summary>
    /// <param name="arquivos">Arquivos XML enviados no formulário.</param>
    /// <returns>
    /// Uma mensagem-resumo, os nomes dos arquivos gerados (para usar em <c>Download</c>)
    /// e o resumo das notas salvas.
    /// </returns>
    [HttpPost("EnviarArquivos")]
    public IActionResult SalvarNotaArquivos(List<IFormFile> arquivos)
    {
        var notasValidas = _service.ProcessarArquivos(arquivos, out int quantidadeJaProcessadas);
        List<string> nomesArquivos = _service.GerarArquivosJson(notasValidas);

        var chaves = notasValidas.Select(n => new {chave = n.Chave, serie = 65, mesAno = ChaveNfce.FormatarMesAno(n.Chave)}).ToList();
        string mensagem;

        if (quantidadeJaProcessadas > 0)
        {
            mensagem = $"Recebi {arquivos.Count} arquivos, {quantidadeJaProcessadas} já haviam sido processados, {notasValidas.Count} notas salvas.";
        }
        else if (notasValidas.Count == 0)
        {
            mensagem = $"Recebi {arquivos.Count} arquivos. 0 notas válidas.";
        }
        else
        {
            mensagem = $"Recebi {arquivos.Count} arquivos. {notasValidas.Count} notas salvas.";
        }

        return Ok(new { mensagem = mensagem, arquivos = nomesArquivos, notas = chaves });
    }
    /// <summary>
    /// Processa XMLs de NFC-e e devolve as notas novas, sem gerar arquivos JSON.
    /// </summary>
    /// <remarks>
    /// Apesar de a rota sugerir só validação, este endpoint <b>grava as notas novas no banco</b>.
    /// O nome da rota foi mantido para não quebrar quem já a utiliza.
    /// </remarks>
    [HttpPost("ValidarArquivos")]
    public IActionResult ProcessarArquivos(List<IFormFile> arquivos)
    {
        int quantidadeJaProcessadas;
        List<NotaFiscal> notasValidas = _service.ProcessarArquivos(arquivos,out quantidadeJaProcessadas);
        return Ok(notasValidas);
    }
    /// <summary>
    /// Baixa um arquivo JSON gerado anteriormente.</c>.
    /// </summary>
    /// <param name="nome">Nome do arquivo, como devolvido por <c>EnviarArquivos</c>.</param>
    /// <returns>O arquivo, ou 404 se ele não existir.</returns>
    [HttpGet("Download/{nome}")]
    public IActionResult Download(string nome)
    {
        // Path.GetFileName impede que o cliente peça arquivos fora da pasta.
        string nomeSeguro = Path.GetFileName(nome);
        string caminho = Path.Combine("Temp", nomeSeguro);

        if (!System.IO.File.Exists(caminho))
            return NotFound();

        return PhysicalFile(Path.GetFullPath(caminho), "text/plain", nomeSeguro);
    }
}