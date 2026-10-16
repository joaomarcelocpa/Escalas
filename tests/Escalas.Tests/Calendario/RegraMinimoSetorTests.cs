using Escalas.Domain.Calendario;

namespace Escalas.Tests.Calendario;

public class RegraMinimoSetorTests
{
    private static readonly SetorMinimo[] Setores =
    [
        new(1, "Caixa", 2),
        new(2, "Estoque", 1),
        new(3, "Limpeza", 0),
    ];

    [Fact]
    public void SetorAbaixoDoMinimo_GeraAlerta()
    {
        var alertas = RegraMinimoSetor.Avaliar(Setores, new Dictionary<int, int> { [1] = 1, [2] = 1 }, diaFechado: false);

        var alerta = Assert.Single(alertas);
        Assert.Equal(1, alerta.SetorId);
        Assert.Equal(2, alerta.Minimo);
        Assert.Equal(1, alerta.Escalados);
    }

    [Fact]
    public void SetorSemNinguemEscalado_ContaComoZero()
    {
        var alertas = RegraMinimoSetor.Avaliar(Setores, new Dictionary<int, int>(), diaFechado: false);

        Assert.Equal([1, 2], alertas.Select(a => a.SetorId).ToArray());
        Assert.All(alertas, a => Assert.Equal(0, a.Escalados));
    }

    [Fact]
    public void SetorNoMinimoOuAcima_NaoGeraAlerta()
    {
        var alertas = RegraMinimoSetor.Avaliar(Setores, new Dictionary<int, int> { [1] = 2, [2] = 3 }, diaFechado: false);

        Assert.Empty(alertas);
    }

    [Fact]
    public void MinimoZero_NuncaGeraAlerta()
    {
        var alertas = RegraMinimoSetor.Avaliar([new SetorMinimo(3, "Limpeza", 0)], new Dictionary<int, int>(), diaFechado: false);

        Assert.Empty(alertas);
    }

    [Fact]
    public void DiaFechado_NaoGeraAlerta()
    {
        var alertas = RegraMinimoSetor.Avaliar(Setores, new Dictionary<int, int>(), diaFechado: true);

        Assert.Empty(alertas);
    }
}
