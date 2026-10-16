using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Escalas.Application.Feriados;
using Escalas.Domain.Entidades;

namespace Escalas.Desktop.ViewModels;

public record FeriadoLinha(int Id, string Data, string Nome, string Escopo, bool Removivel);

public partial class FeriadosViewModel : ViewModelBase
{
    private static readonly CultureInfo Pt = new("pt-BR");

    private readonly IFeriadoService? _servico;
    private readonly IFeriadoConsulta? _consulta;

    public ObservableCollection<FilialResumo> Filiais { get; } = [];
    public ObservableCollection<FeriadoLinha> Linhas { get; } = [];
    public IReadOnlyList<AbrangenciaFeriado> Tipos { get; } = [AbrangenciaFeriado.Estadual, AbrangenciaFeriado.Municipal];

    [ObservableProperty]
    public partial FilialResumo? FilialSelecionada { get; set; }

    [ObservableProperty]
    public partial int Ano { get; set; } = DateTime.Today.Year;

    [ObservableProperty]
    public partial string NovaData { get; set; } = "";

    [ObservableProperty]
    public partial string NovoNome { get; set; } = "";

    [ObservableProperty]
    public partial AbrangenciaFeriado NovoTipo { get; set; } = AbrangenciaFeriado.Municipal;

    [ObservableProperty]
    public partial string Mensagem { get; set; } = "";

    [ObservableProperty]
    public partial bool Ocupado { get; set; }

    /// <summary>Construtor para o previewer.</summary>
    public FeriadosViewModel() { }

    public FeriadosViewModel(IFeriadoService servico, IFeriadoConsulta consulta)
    {
        _servico = servico;
        _consulta = consulta;
        _ = CarregarFiliaisAsync();
    }

    partial void OnFilialSelecionadaChanged(FilialResumo? value) => _ = RecarregarAsync();

    partial void OnAnoChanged(int value) => _ = RecarregarAsync();

    private async Task CarregarFiliaisAsync()
    {
        var filiais = await _servico!.ListarFiliaisAsync();
        Filiais.Clear();
        foreach (var f in filiais) Filiais.Add(f);
        FilialSelecionada = Filiais.FirstOrDefault();
        if (Filiais.Count == 0) Mensagem = "Cadastre uma filial para gerenciar feriados.";
    }

    private async Task RecarregarAsync()
    {
        Linhas.Clear();
        if (_consulta is null || FilialSelecionada is null) return;

        var ano = Ano;
        var filialId = FilialSelecionada.Id;
        var lista = await _consulta.ObterAsync(filialId, new DateOnly(ano, 1, 1), new DateOnly(ano, 12, 31));
        if (ano != Ano || filialId != FilialSelecionada?.Id) return; // seleção mudou durante a consulta

        Linhas.Clear();
        foreach (var f in lista)
        {
            var local = f.Abrangencia != AbrangenciaFeriado.Nacional && f.FilialId == filialId;
            Linhas.Add(new FeriadoLinha(f.Id, f.Data.ToString("dd/MM  ddd", Pt), f.Nome, f.Abrangencia.ToString(), local));
        }
    }

    [RelayCommand]
    private void AnoAnterior() => Ano--;

    [RelayCommand]
    private void AnoSeguinte() => Ano++;

    [RelayCommand]
    private async Task SincronizarAsync()
    {
        if (_servico is null) return;
        Ocupado = true;
        try
        {
            var ok = await _servico.SincronizarAnoAsync(Ano, forcar: true);
            Mensagem = ok ? $"Feriados nacionais de {Ano} atualizados." : "Sem conexão: mantidos os feriados já salvos.";
            await RecarregarAsync();
        }
        finally { Ocupado = false; }
    }

    [RelayCommand]
    private async Task AdicionarAsync()
    {
        if (_servico is null || FilialSelecionada is null) return;
        if (!DateOnly.TryParseExact($"{NovaData.Trim()}/{Ano}", "d/M/yyyy", Pt, DateTimeStyles.None, out var data))
        {
            Mensagem = "Data inválida. Use dia/mês, por exemplo 14/07.";
            return;
        }

        try
        {
            await _servico.AdicionarLocalAsync(new FeriadoLocalDados(FilialSelecionada.Id, data, NovoNome, NovoTipo));
        }
        catch (ArgumentException e)
        {
            Mensagem = e.Message;
            return;
        }

        NovaData = "";
        NovoNome = "";
        Mensagem = "";
        await RecarregarAsync();
    }

    [RelayCommand]
    private async Task RemoverAsync(FeriadoLinha linha)
    {
        if (_servico is null || !linha.Removivel) return;
        await _servico.RemoverLocalAsync(linha.Id);
        await RecarregarAsync();
    }
}
