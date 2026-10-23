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
        int notasJaProcessadas;
        List<NotaFiscal> notasValidas = _service.ValidarArquivos(arquivos,out notasJaProcessadas);

        var chaves = notasValidas.Select(n => new { chaveXml = n.Chave }).ToList();
        string mensagem;

        if (notasJaProcessadas > 0)
        {
            mensagem = $"Recebi {arquivos.Count} arquivos, {notasJaProcessadas} já haviam sido processados, {notasValidas.Count} notas salvas.";
        }
        else if (notasValidas.Count == 0)
        {
            mensagem = $"Recebi {arquivos.Count} arquivos. 0 notas válidas.";
        }
        else
        {
            mensagem = $"Recebi {arquivos.Count} arquivos. {notasValidas.Count} notas salvas.";
        }

        return Ok(new { mensagem = mensagem, notas = chaves });
    }
    [HttpPost("ValidarArquivos")]
    public IActionResult ValidarArquivos(List<IFormFile> arquivos)
    {
        int notasJaProcessadas;
        List<NotaFiscal> notasValidas = _service.ValidarArquivos(arquivos,out notasJaProcessadas);
        return Ok(notasValidas);
    }
}