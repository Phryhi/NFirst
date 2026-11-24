namespace NFirst.Services;
/// <summary>
/// Regras da chave de acesso de 44 dígitos da NFC-e. Contém apenas funções puras,
/// sem acesso a banco de dados ou arquivos.
/// </summary>
/// <remarks>
/// Posições usadas da chave (base 0): 0-1 UF, 2-3 ano, 4-5 mês, 6-19 CNPJ,
/// 20-21 modelo, 43 dígito verificador.
/// </remarks>
public static class ChaveNfce
{
    private const bool ValidarDigitoVerificador = true;
    /// <summary>
    /// Extrai o mês e o ano de emissão da chave, no formato "MM/AAAA".
    /// </summary>
    /// <param name="chave">Chave de acesso com 44 dígitos.</param>
    /// <returns>
    /// O mês/ano formatado, ou <c>null</c> se a chave tiver tamanho errado,
    /// caracteres não numéricos nas posições de ano/mês ou mês fora de 1 a 12.
    /// </returns>
    public static string FormatarMesAno(string chave)
    {
        if (string.IsNullOrWhiteSpace(chave) || chave.Length != 44)
        {
            return null;
        }
        // Ano e mês são vizinhos na chave (posições 2 a 5).
        for (int i = 2; i <= 5; i++)
        {
            if (chave[i] < '0' || chave[i] > '9')
            {
                return null;
            }
        }

        int numeroMes = (chave[4] - '0') * 10 + (chave[5] - '0');

        if (numeroMes < 1 || numeroMes > 12)
        {
            return null;
        }

        string ano = "20" + chave.Substring(2, 2);
        string mes = chave.Substring(4, 2);

        return $"{mes}/{ano}";
    }
    // Códigos de UF do IBGE.
    private static bool UfValida(string chave)
    {
        int uf = (chave[0] - '0') * 10 + (chave[1] - '0');

        return uf is 11 or 12 or 13 or 14 or 15 or 16 or 17
                  or 21 or 22 or 23 or 24 or 25 or 26 or 27 or 28 or 29
                  or 31 or 32 or 33 or 35
                  or 41 or 42 or 43
                  or 50 or 51 or 52 or 53;
    }
    // Módulo 11 com pesos de 2 a 9, aplicados da direita para a esquerda
    // sobre os 43 primeiros dígitos. Resto 0 ou 1 resulta em dígito 0.
    private static bool DigitoVerificadorValido(string chave)
    {
        int soma = 0;
        int peso = 2;

        for (int i = 42; i >= 0; i--)
        {
            soma += (chave[i] - '0') * peso;
            peso = peso == 9 ? 2 : peso + 1;
        }

        int resto = soma % 11;
        int dv = resto < 2 ? 0 : 11 - resto;

        return chave[43] - '0' == dv;
    }
    /// <summary>
    /// Indica se a chave é de uma NFC-e (modelo 65) estruturalmente válida.
    /// </summary>
    /// <param name="chave">Chave de acesso com 44 dígitos.</param>
    /// <returns>
    /// <c>true</c> se a chave tem 44 dígitos, modelo 65, UF existente, mês válido
    /// e dígito verificador correto; caso contrário, <c>false</c>.
    /// </returns>
    public static bool EhNfce(string chave)
    {
        if (string.IsNullOrWhiteSpace(chave) || chave.Length != 44)
            return false;

        for (int i = 0; i < 44; i++)
        {
            if (chave[i] < '0' || chave[i] > '9')
                return false;
        }

        if (chave[20] != '6' || chave[21] != '5')
            return false;

        if (!UfValida(chave))
            return false;
        // FormatarMesAno já valida a faixa do mês, então reaproveitamos a regra.
        if (FormatarMesAno(chave) == null)
            return false;

        return !ValidarDigitoVerificador || DigitoVerificadorValido(chave);
    }
    /// <summary>
    /// Indica se o arquivo pode ser descartado apenas pelo nome, sem abrir o XML.
    /// </summary>
    /// <param name="nomeArquivo">Nome (ou caminho) do arquivo, com ou sem o prefixo "NFe".</param>
    /// <returns>
    /// <c>true</c> somente quando o nome contém uma chave de 44 dígitos cujo modelo
    /// não é 65. Se o nome não segue o padrão da chave, devolve <c>false</c>, e o
    /// conteúdo do XML decide.
    /// </returns>
    /// <remarks>
    /// É uma otimização: evita abrir e interpretar XMLs de outros modelos (ex.: NF-e 55).
    /// </remarks>
    public static bool PodeDescartarPeloNome(string nomeArquivo)
    {
        int separador = nomeArquivo.AsSpan().LastIndexOfAny('/', '\\');
        ReadOnlySpan<char> nome = nomeArquivo.AsSpan(separador + 1);

        if (nome.StartsWith("NFe", StringComparison.OrdinalIgnoreCase))
        {
            nome = nome.Slice(3);
        }

        if (nome.Length < 44)
        {
            return false;
        }

        for (int i = 0; i < 44; i++)
        {
            if (nome[i] < '0' || nome[i] > '9')
            {
                return false;
            }
        }

        return nome[20] != '6' || nome[21] != '5';
    }
}