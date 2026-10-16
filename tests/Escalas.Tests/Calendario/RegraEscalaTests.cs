using Escalas.Domain.Calendario;

namespace Escalas.Tests.Calendario;

public class RegraEscalaTests
{
    private static readonly DateOnly Segunda = new(2026, 10, 12);
    private static readonly DateOnly Terca = new(2026, 10, 13);
    private static readonly FuncionarioEscalavel Ana = new(1, FilialId: 10, Ativo: true);

    [Fact]
    public void Mover_Valido_Passa()
    {
        var r = RegraEscala.ValidarMover(Ana, 10, Segunda, Terca, diaDestinoFechado: false, jaEscaladoNoDestino: false);

        Assert.True(r.Ok);
    }

    [Fact]
    public void Mover_ParaDiaFechado_Bloqueia()
    {
        var r = RegraEscala.ValidarMover(Ana, 10, Segunda, Terca, diaDestinoFechado: true, jaEscaladoNoDestino: false);

        Assert.False(r.Ok);
    }

    [Fact]
    public void Mover_ParaDiaEmQueJaEstaEscalado_Bloqueia()
    {
        var r = RegraEscala.ValidarMover(Ana, 10, Segunda, Terca, diaDestinoFechado: false, jaEscaladoNoDestino: true);

        Assert.False(r.Ok);
    }

    [Fact]
    public void Mover_ParaOMesmoDia_Bloqueia()
    {
        var r = RegraEscala.ValidarMover(Ana, 10, Segunda, Segunda, false, false);

        Assert.False(r.Ok);
    }

    [Fact]
    public void FuncionarioInativo_Bloqueia()
    {
        var r = RegraEscala.ValidarEscalar(Ana with { Ativo = false }, 10, false, false);

        Assert.False(r.Ok);
    }

    [Fact]
    public void FuncionarioDeOutraFilial_Bloqueia()
    {
        var r = RegraEscala.ValidarEscalar(Ana, filialId: 99, false, false);

        Assert.False(r.Ok);
    }

    [Theory]
    [InlineData(2026, 10, 12, 12)] // segunda
    [InlineData(2026, 10, 14, 12)] // quarta
    [InlineData(2026, 10, 18, 12)] // domingo pertence à semana que começou na segunda 12
    [InlineData(2026, 10, 19, 19)] // próxima segunda
    public void InicioDaSemana_ComecaNaSegunda(int ano, int mes, int dia, int esperadoDia)
    {
        Assert.Equal(new DateOnly(2026, 10, esperadoDia), RegraEscala.InicioDaSemana(new DateOnly(ano, mes, dia)));
    }
}
