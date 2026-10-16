namespace Escalas.Domain.Calendario;

public record FuncionarioEscalavel(int Id, int FilialId, bool Ativo);

public record ResultadoRegra(bool Ok, string? Erro = null)
{
    public static readonly ResultadoRegra Sucesso = new(true);
    public static ResultadoRegra Falha(string erro) => new(false, erro);
}

/// <summary>Regras para escalar e mover funcionários entre dias.</summary>
public static class RegraEscala
{
    public static ResultadoRegra ValidarEscalar(
        FuncionarioEscalavel funcionario,
        int filialId,
        bool diaDestinoFechado,
        bool jaEscaladoNoDestino)
    {
        if (!funcionario.Ativo) return ResultadoRegra.Falha("Funcionário inativo.");
        if (funcionario.FilialId != filialId) return ResultadoRegra.Falha("Funcionário pertence a outra filial.");
        if (diaDestinoFechado) return ResultadoRegra.Falha("O dia está fechado.");
        if (jaEscaladoNoDestino) return ResultadoRegra.Falha("Funcionário já está escalado neste dia.");
        return ResultadoRegra.Sucesso;
    }

    public static ResultadoRegra ValidarMover(
        FuncionarioEscalavel funcionario,
        int filialId,
        DateOnly origem,
        DateOnly destino,
        bool diaDestinoFechado,
        bool jaEscaladoNoDestino)
    {
        if (origem == destino) return ResultadoRegra.Falha("Origem e destino são o mesmo dia.");
        return ValidarEscalar(funcionario, filialId, diaDestinoFechado, jaEscaladoNoDestino);
    }

    public static DateOnly InicioDaSemana(DateOnly data)
    {
        var delta = ((int)data.DayOfWeek + 6) % 7; // segunda = 0
        return data.AddDays(-delta);
    }
}
