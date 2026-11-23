using System.Data;
using Escalas.Infrastructure.Persistencia.Migrations;
using Microsoft.EntityFrameworkCore;

namespace Escalas.Infrastructure.Persistencia;

/// <summary>
/// Antes de aplicar a migration <see cref="FiliaisFixas"/>, confere se cada filial antiga que tem dados
/// corresponde, pelo nome, a exatamente uma filial fixa. Se não, devolve uma mensagem clara e o banco não é migrado.
/// (A própria migration tem a mesma trava; isto só troca o erro técnico por uma explicação.)
/// </summary>
public static class VerificacaoFiliaisFixas
{
    public static async Task<string?> VerificarAsync(EscalasDbContext db, IEnumerable<string> pendentes, CancellationToken ct = default)
    {
        if (!pendentes.Contains(FiliaisFixas.IdMigracao)) return null;

        var conexao = db.Database.GetDbConnection();
        var abriu = conexao.State != ConnectionState.Open;
        if (abriu) await conexao.OpenAsync(ct);
        try
        {
            await using var cmd = conexao.CreateCommand();
            cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = 'Filiais'";
            if (Convert.ToInt64(await cmd.ExecuteScalarAsync(ct)) == 0) return null;

            cmd.CommandText = $"""
                SELECT "Nome", {FiliaisFixas.SqlTemDados("\"Id\"")} FROM "Filiais" ORDER BY "Nome"
                """;
            var antigas = new List<(string Nome, int? Fixa, bool TemDados)>();
            await using (var r = await cmd.ExecuteReaderAsync(ct))
                while (await r.ReadAsync(ct))
                    antigas.Add((r.GetString(0), FiliaisFixas.FilialFixaDe(r.GetString(0)), r.GetInt64(1) != 0));

            return MontarAviso(antigas);
        }
        finally
        {
            if (abriu) await conexao.CloseAsync();
        }
    }

    /// <summary>Mensagem com os problemas encontrados, ou null se a conversão é segura.</summary>
    public static string? MontarAviso(IReadOnlyList<(string Nome, int? Fixa, bool TemDados)> antigas)
    {
        var comDados = antigas.Where(a => a.TemDados).ToList();
        var problemas = new List<string>();
        foreach (var a in comDados.Where(a => a.Fixa is null))
            problemas.Add($"• \"{a.Nome}\" não corresponde a nenhuma das filiais fixas.");
        foreach (var g in comDados.Where(a => a.Fixa is not null).GroupBy(a => a.Fixa!.Value).Where(g => g.Count() > 1))
            problemas.Add($"• {string.Join(" e ", g.Select(a => $"\"{a.Nome}\""))} correspondem à mesma filial ({Domain.Entidades.Filiais.Nome((Domain.Entidades.Filial)g.Key)}).");
        if (problemas.Count == 0) return null;

        return "Esta versão usa filiais fixas (Matriz, Matozinhos, FC, Interiores e Depósito), mas algumas filiais cadastradas " +
               "não puderam ser convertidas:\n\n" + string.Join("\n", problemas) +
               "\n\nO banco não foi alterado. Renomeie essas filiais (ou mova seus setores e funcionários) na versão anterior do " +
               "aplicativo e abra esta versão novamente.";
    }
}
