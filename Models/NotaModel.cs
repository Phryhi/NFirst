using System.ComponentModel.DataAnnotations;

public class NotaFiscal
{
    [Key]
    public int Id {get; set;}
    [Required]
    public string Chave { get; set; }
    public string Cpf { get; set; }
    public decimal Valor { get; set; }
    [Required]
    public bool TipoNota { get; set; }
}