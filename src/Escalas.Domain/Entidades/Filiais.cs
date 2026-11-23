using System.Globalization;
using System.Text;

namespace Escalas.Domain.Entidades;

/// <summary>
/// Filiais fixas da empresa. Não há cadastro de filial: setores, funcionários, dias fechados e feriados locais
/// guardam um destes valores. Os números são gravados no banco; nunca reaproveite nem renumere.
/// </summary>
public enum Filial
{
    Matriz = 1,
    Matozinhos = 2,
    FC = 3,
    Interiores = 4,
    Deposito = 5,
}

/// <summary>Filial com o nome de exibição, para listas e caixas de seleção.</summary>
public record FilialOpcao(Filial Id, string Nome);

public static class Filiais
{
    /// <summary>Todas as filiais, na ordem de exibição.</summary>
    public static IReadOnlyList<FilialOpcao> Todas { get; } =
    [
        new(Filial.Matriz, "Matriz"),
        new(Filial.Matozinhos, "Matozinhos"),
        new(Filial.FC, "FC"),
        new(Filial.Interiores, "Interiores"),
        new(Filial.Deposito, "Depósito"),
    ];

    public static bool Existe(Filial filial) => Todas.Any(f => f.Id == filial);

    public static string Nome(Filial filial) =>
        Todas.FirstOrDefault(f => f.Id == filial)?.Nome ?? throw new ArgumentOutOfRangeException(nameof(filial), filial, "Filial inexistente.");

    /// <summary>Encontra a filial fixa pelo nome, ignorando maiúsculas, acentos e espaços nas pontas. Null se não corresponder.</summary>
    public static Filial? Identificar(string? nome)
    {
        var alvo = Normalizar(nome);
        foreach (var f in Todas)
            if (Normalizar(f.Nome) == alvo) return f.Id;
        return null;
    }

    public static string Normalizar(string? texto)
    {
        var decomposto = (texto ?? "").Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposto.Length);
        foreach (var c in decomposto)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }
}
