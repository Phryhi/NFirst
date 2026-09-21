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
}