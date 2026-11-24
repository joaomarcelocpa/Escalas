using Escalas.Domain.Calendario;

namespace Escalas.Tests.Calendario;

public class RegraHorasTests
{
    private static readonly DateOnly Segunda = new(2026, 11, 23);

    private static DiaEscalado Dia(int deslocamento, string? inicio, string? fim) =>
        new(Segunda.AddDays(deslocamento),
            inicio is null ? null : TimeOnly.Parse(inicio),
            fim is null ? null : TimeOnly.Parse(fim));

    [Fact]
    public void Duracao_EhFimMenosInicio()
    {
        Assert.Equal(TimeSpan.FromHours(8.5), RegraHoras.Duracao(new TimeOnly(8, 0), new TimeOnly(16, 30)));
    }

    [Theory]
    [InlineData("08:00", "17:30", 480, true)]   // 9h30 → 8h
    [InlineData("08:00", "16:00", 390, true)]   // exatamente 8h → 6h30
    [InlineData("08:00", "15:59", 479, false)]  // abaixo de 8h: sem almoço
    [InlineData("08:00", "12:00", 240, false)]
    public void HorasTrabalhadas_DescontaAlmocoSoComOitoHorasOuMais(string inicio, string fim, int minutos, bool desconta)
    {
        var (i, f) = (TimeOnly.Parse(inicio), TimeOnly.Parse(fim));
        Assert.Equal(TimeSpan.FromMinutes(minutos), RegraHoras.HorasTrabalhadas(i, f));
        Assert.Equal(desconta, RegraHoras.DescontaAlmoco(i, f));
        Assert.Equal(desconta, new DiaEscalado(new DateOnly(2026, 11, 23), i, f).DescontaAlmoco);
    }

    [Fact]
    public void FormatarHorasDoDia_IndicaAlmoco()
    {
        Assert.Equal("8h (−1h30 almoço)", RegraHoras.FormatarHorasDoDia(new TimeOnly(8, 0), new TimeOnly(17, 30)));
        Assert.Equal("4h", RegraHoras.FormatarHorasDoDia(new TimeOnly(8, 0), new TimeOnly(12, 0)));
    }

    [Theory]
    [InlineData("08:00", "08:00")]
    [InlineData("18:00", "08:00")]
    public void FimNaoPosteriorAoInicio_EhInvalido(string inicio, string fim)
    {
        Assert.NotNull(RegraHoras.Validar(TimeOnly.Parse(inicio), TimeOnly.Parse(fim)));
        Assert.Throws<ArgumentException>(() => RegraHoras.Duracao(TimeOnly.Parse(inicio), TimeOnly.Parse(fim)));
    }

    [Fact]
    public void Totalizar_SomaSoDiasComExpediente_eContaOsSemExpediente()
    {
        var dias = new[] { Dia(0, "08:00", "17:00"), Dia(1, "08:00", "12:30"), Dia(2, null, null) };

        var t = RegraHoras.Totalizar(dias, hoje: Segunda.AddDays(6));

        Assert.Equal(3, t.Dias);
        Assert.Equal(TimeSpan.FromHours(7.5 + 4.5), t.Total); // 9h − 1h30 de almoço + 4h30
        Assert.Equal(1, t.DiasSemExpediente);
    }

    [Fact]
    public void Totalizar_AteHoje_IncluiHojeENaoOsDiasSeguintes()
    {
        var dias = new[] { Dia(0, "08:00", "17:00"), Dia(2, "08:00", "17:00"), Dia(4, "08:00", "17:00") };

        var t = RegraHoras.Totalizar(dias, hoje: Segunda.AddDays(2));

        Assert.Equal(TimeSpan.FromHours(22.5), t.Total); // 3 × 7h30
        Assert.Equal(TimeSpan.FromHours(15), t.AteHoje);
    }

    [Fact]
    public void Totalizar_SemDias_EhZero()
    {
        var t = RegraHoras.Totalizar([], Segunda);
        Assert.Equal(new TotalHoras(0, TimeSpan.Zero, TimeSpan.Zero, 0), t);
    }

    [Theory]
    [InlineData(0, "0h")]
    [InlineData(480, "8h")]
    [InlineData(510, "8h30")]
    [InlineData(2645, "44h05")]
    public void Formatar(int minutos, string esperado)
    {
        Assert.Equal(esperado, RegraHoras.Formatar(TimeSpan.FromMinutes(minutos)));
    }

    [Theory]
    [InlineData("8", 8, 0)]
    [InlineData("08:00", 8, 0)]
    [InlineData("8:30", 8, 30)]
    [InlineData(" 17:45 ", 17, 45)]
    [InlineData("0830", 8, 30)]
    [InlineData("8h30", 8, 30)]
    [InlineData("18h", 18, 0)]
    public void TentarLerHora_AceitaFormatosComuns(string texto, int h, int m)
    {
        Assert.True(RegraHoras.TentarLerHora(texto, out var hora));
        Assert.Equal(new TimeOnly(h, m), hora);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("25:00")]
    [InlineData("8:75")]
    public void TentarLerHora_RecusaTextoInvalido(string texto)
    {
        Assert.False(RegraHoras.TentarLerHora(texto, out _));
    }
}
