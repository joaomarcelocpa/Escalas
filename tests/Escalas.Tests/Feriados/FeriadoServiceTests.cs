using System.Net;
using System.Text;
using Escalas.Application.Feriados;
using Escalas.Domain.Entidades;
using Escalas.Infrastructure.Feriados;

namespace Escalas.Tests.Feriados;

public class FeriadoServiceTests : IDisposable
{
    private readonly BancoMemoria _banco = new();
    private readonly ApiFalsa _api = new();
    private readonly RelogioFalso _relogio = new();
    private readonly FeriadoService _svc;

    public FeriadoServiceTests()
    {
        _api.PorAno[2026] = [new(new DateOnly(2026, 1, 1), "Confraternização mundial"), new(new DateOnly(2026, 12, 25), "Natal")];
        _svc = new FeriadoService(_banco, _api, _relogio);
    }

    public void Dispose() => _banco.Dispose();

    private static readonly DateOnly Ini = new(2026, 1, 1);
    private static readonly DateOnly Fim = new(2026, 12, 31);

    // ---- cache ----

    [Fact]
    public async Task Sincronizar_baixa_e_grava_nacionais()
    {
        Assert.True(await _svc.SincronizarAnoAsync(2026));

        using var db = _banco.CreateDbContext();
        Assert.Equal(2, db.Feriados.Count(f => f.Abrangencia == AbrangenciaFeriado.Nacional));
    }

    [Fact]
    public async Task Ano_em_cache_nao_consulta_a_api_novamente()
    {
        await _svc.SincronizarAnoAsync(2026);
        await _svc.SincronizarAnoAsync(2026);

        Assert.Equal([2026], _api.Chamadas);
    }

    [Fact]
    public async Task Forcar_substitui_nacionais_sem_duplicar_nem_tocar_locais()
    {
        var filial = Filial.Matriz;
        await _svc.AdicionarLocalAsync(new(filial, new DateOnly(2026, 7, 14), "Aniversário", AbrangenciaFeriado.Municipal));
        await _svc.SincronizarAnoAsync(2026);
        _api.PorAno[2026] = [new(new DateOnly(2026, 1, 1), "Ano Novo")];

        await _svc.SincronizarAnoAsync(2026, forcar: true);

        using var db = _banco.CreateDbContext();
        var nac = db.Feriados.Where(f => f.Abrangencia == AbrangenciaFeriado.Nacional).ToList();
        Assert.Single(nac);
        Assert.Equal("Ano Novo", nac[0].Nome);
        Assert.Equal(1, db.Feriados.Count(f => f.Abrangencia == AbrangenciaFeriado.Municipal));
    }

    // ---- offline ----

    [Fact]
    public async Task Falha_de_rede_sem_cache_retorna_false_sem_lancar()
    {
        _api.Offline = true;

        Assert.False(await _svc.SincronizarAnoAsync(2026));
    }

    [Fact]
    public async Task Offline_depois_de_baixado_continua_servindo_o_cache()
    {
        var filial = Filial.Matriz;
        await _svc.SincronizarAnoAsync(2026);
        _api.Offline = true;
        _api.Chamadas.Clear();

        var lista = await _svc.ObterAsync(filial, Ini, Fim);

        Assert.Equal(2, lista.Count);
        Assert.Empty(_api.Chamadas);
    }

    [Fact]
    public async Task Forcar_offline_preserva_o_cache_existente()
    {
        await _svc.SincronizarAnoAsync(2026);
        _api.Offline = true;

        Assert.False(await _svc.SincronizarAnoAsync(2026, forcar: true));

        using var db = _banco.CreateDbContext();
        Assert.Equal(2, db.Feriados.Count());
    }

    [Fact]
    public async Task Consulta_falha_de_rede_retorna_o_que_ha_em_cache_e_faz_backoff()
    {
        var filial = Filial.Matriz;
        _api.Offline = true;

        Assert.Empty(await _svc.ObterAsync(filial, Ini, Fim));
        Assert.Empty(await _svc.ObterAsync(filial, Ini, Fim));
        Assert.Single(_api.Chamadas); // segunda consulta respeitou a espera

        _relogio.Agora += TimeSpan.FromMinutes(6);
        _api.Offline = false;

        Assert.Equal(2, (await _svc.ObterAsync(filial, Ini, Fim)).Count);
    }

    [Fact]
    public async Task Consulta_baixa_automaticamente_o_ano_ausente()
    {
        var filial = Filial.Matriz;

        var lista = await _svc.ObterAsync(filial, new DateOnly(2026, 12, 20), new DateOnly(2026, 12, 31));

        Assert.Equal(["Natal"], lista.Select(f => f.Nome));
    }

    // ---- escopos ----

    [Fact]
    public async Task Consulta_respeita_escopos_nacional_estadual_municipal()
    {
        var campinas = Filial.Matriz;
        var santos = Filial.Matozinhos;
        var rio = Filial.Deposito;
        await _svc.SincronizarAnoAsync(2026);
        await _svc.AdicionarLocalAsync(new(campinas, new DateOnly(2026, 7, 9), "Revolução Constitucionalista", AbrangenciaFeriado.Estadual));
        await _svc.AdicionarLocalAsync(new(campinas, new DateOnly(2026, 7, 14), "Aniversário de Campinas", AbrangenciaFeriado.Municipal));
        await _svc.AdicionarLocalAsync(new(rio, new DateOnly(2026, 4, 23), "São Jorge", AbrangenciaFeriado.Estadual));

        var nomesCampinas = (await _svc.ObterAsync(campinas, Ini, Fim)).Select(f => f.Nome).ToList();
        var nomesSantos = (await _svc.ObterAsync(santos, Ini, Fim)).Select(f => f.Nome).ToList();
        var nomesRio = (await _svc.ObterAsync(rio, Ini, Fim)).Select(f => f.Nome).ToList();

        Assert.Contains("Natal", nomesCampinas);
        Assert.Contains("Aniversário de Campinas", nomesCampinas);
        Assert.Contains("Revolução Constitucionalista", nomesCampinas);
        Assert.DoesNotContain("São Jorge", nomesCampinas);

        // feriado local vale só para a filial a que foi vinculado
        Assert.DoesNotContain("Aniversário de Campinas", nomesSantos);
        Assert.DoesNotContain("Revolução Constitucionalista", nomesSantos);
        Assert.Contains("Natal", nomesSantos);

        Assert.Contains("São Jorge", nomesRio);
        Assert.DoesNotContain("Aniversário de Campinas", nomesRio);
    }

    [Fact]
    public async Task Consulta_filtra_pelo_intervalo_e_ordena_por_data()
    {
        var f = Filial.Matriz;
        await _svc.SincronizarAnoAsync(2026);

        var lista = await _svc.ObterAsync(f, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));

        Assert.Equal(["Confraternização mundial"], lista.Select(x => x.Nome));
    }

    // ---- CRUD local ----

    [Fact]
    public async Task Adicionar_local_vincula_a_filial_fixa()
    {
        var f = Filial.FC;

        var mun = await _svc.AdicionarLocalAsync(new(f, new DateOnly(2026, 7, 14), "  Aniversário  ", AbrangenciaFeriado.Municipal));
        var est = await _svc.AdicionarLocalAsync(new(f, new DateOnly(2026, 7, 9), "9 de Julho", AbrangenciaFeriado.Estadual));

        Assert.Equal("Aniversário", mun.Nome);
        Assert.Equal(Filial.FC, mun.Filial);
        Assert.Equal(Filial.FC, est.Filial);
    }

    [Fact]
    public async Task Adicionar_local_valida_entrada()
    {
        var f = Filial.Matriz;

        await Assert.ThrowsAsync<ArgumentException>(() => _svc.AdicionarLocalAsync(new(f, Ini, "X", AbrangenciaFeriado.Nacional)));
        await Assert.ThrowsAsync<ArgumentException>(() => _svc.AdicionarLocalAsync(new(f, Ini, "  ", AbrangenciaFeriado.Estadual)));
        await Assert.ThrowsAsync<ArgumentException>(() => _svc.AdicionarLocalAsync(new((Filial)999, Ini, "X", AbrangenciaFeriado.Estadual)));
    }

    [Fact]
    public async Task Listar_locais_filtra_por_filial_e_ano_e_exclui_nacionais()
    {
        var a = Filial.Matriz;
        var b = Filial.Interiores;
        await _svc.SincronizarAnoAsync(2026);
        await _svc.AdicionarLocalAsync(new(a, new DateOnly(2026, 7, 14), "A-2026", AbrangenciaFeriado.Municipal));
        await _svc.AdicionarLocalAsync(new(a, new DateOnly(2027, 7, 14), "A-2027", AbrangenciaFeriado.Municipal));
        await _svc.AdicionarLocalAsync(new(b, new DateOnly(2026, 8, 1), "B-2026", AbrangenciaFeriado.Municipal));

        var lista = await _svc.ListarLocaisAsync(a, 2026);

        Assert.Equal(["A-2026"], lista.Select(x => x.Nome));
    }

    [Fact]
    public async Task Remover_local_apaga_e_recusa_nacional()
    {
        var f = Filial.Matriz;
        await _svc.SincronizarAnoAsync(2026);
        var local = await _svc.AdicionarLocalAsync(new(f, new DateOnly(2026, 7, 14), "Aniversário", AbrangenciaFeriado.Municipal));

        await _svc.RemoverLocalAsync(local.Id);
        Assert.Empty(await _svc.ListarLocaisAsync(f, 2026));

        int idNacional;
        using (var db = _banco.CreateDbContext()) idNacional = db.Feriados.First(x => x.Abrangencia == AbrangenciaFeriado.Nacional).Id;
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.RemoverLocalAsync(idNacional));
    }

    // ---- cliente HTTP ----

    [Fact]
    public async Task Cliente_brasilapi_le_o_json_e_usa_a_rota_do_ano()
    {
        const string json = """[{"date":"2026-01-01","name":"Confraternização mundial","type":"national"},{"date":"2026-04-03","name":"Sexta-feira Santa","type":"national"}]""";
        var handler = new HandlerFalso(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
        var client = new BrasilApiFeriadosClient(new HttpFactoryFalsa(handler));

        var itens = await client.ObterNacionaisAsync(2026);

        Assert.Equal("/api/feriados/v1/2026", handler.Requisicoes.Single().AbsolutePath);
        Assert.Equal([new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 3)], itens.Select(i => i.Data));
        Assert.Equal("Sexta-feira Santa", itens[1].Nome);
    }

    [Fact]
    public async Task Cliente_brasilapi_propaga_erro_http_e_servico_tolera()
    {
        var handler = new HandlerFalso(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var client = new BrasilApiFeriadosClient(new HttpFactoryFalsa(handler));
        await Assert.ThrowsAsync<HttpRequestException>(() => client.ObterNacionaisAsync(2026));

        var svc = new FeriadoService(_banco, client, _relogio);
        Assert.False(await svc.SincronizarAnoAsync(2026));
    }
}
