using Microsoft.AspNetCore.Mvc;
using NFirst.Services;

namespace NFirst.Controllers;

[ApiController]
[Route("[controller]")]
public class NotasController : ControllerBase
{
    private readonly NotaService _service;

    public NotasController(NotaService service)
    {
        _service = service;
    }

    [HttpGet]
    public IActionResult Listar()
    {
        var notas = _service.Listar();

        return Ok(notas);
    }

    [HttpPost]
    public IActionResult Enviar(NotaFiscal nota)
    {
        var resultado = _service.Enviar(nota);

        return Ok(resultado);
    }
    [HttpPost("EnviarArquivos")]
    public IActionResult EnviarArquivos(List<IFormFile> arquivos)
    {
        _service.ProcessarArquivos(arquivos);
        List<NotaFiscal> notasValidas = _service.ValidarArquivos(arquivos);
        var chaves = notasValidas.Select(n => new { chaveXml = n.Chave }).ToList();
        return Ok(new {mensagem = $"Recebi {arquivos.Count} arquivos. {notasValidas.Count} notas validas.", notas = chaves});

    }
    [HttpPost("ValidarArquivos")]
    public IActionResult ValidarArquivos(List<IFormFile> arquivos)
    {
        List<NotaFiscal> notasValidas = _service.ValidarArquivos(arquivos);
        return Ok(notasValidas);
    }
}