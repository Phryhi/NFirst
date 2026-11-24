using System.ComponentModel.DataAnnotations;
/// <summary>
/// Nota fiscal de consumidor (NFC-e) guardada no banco de dados.
/// </summary>
public class NotaFiscal
{
    [Key]
    public int Id {get; set;}
    /// <summary>Chave de acesso de 44 dígitos que identifica a nota.</summary>
    [Required]
    public string Chave { get; set; }
    /// <summary>CPF do destinatário, quando informado.</summary>
    public string Cpf { get; set; }
    /// <summary>Valor total da nota (vNF).</summary>
    public decimal Valor { get; set; }
    [Required]
    public bool TipoNota { get; set; }
}