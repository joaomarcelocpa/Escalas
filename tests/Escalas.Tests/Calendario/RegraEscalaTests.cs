using Escalas.Domain.Calendario;

namespace Escalas.Tests.Calendario;

public class RegraEscalaTests
{
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
