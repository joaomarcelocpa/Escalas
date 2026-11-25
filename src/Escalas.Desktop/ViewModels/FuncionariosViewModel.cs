using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Escalas.Application.Cadastros;
using Escalas.Application.Calendario;
using Escalas.Domain.Calendario;
using Avalonia.Media.Imaging;
using Escalas.Domain.Entidades;

namespace Escalas.Desktop.ViewModels;

/// <summary>Opção da filial extra; <see cref="Id"/> nulo = nenhuma.</summary>
public record FilialExtraOpcao(Filial? Id, string Nome);

public partial class FuncionariosViewModel : CadastroViewModelBase
{
    private readonly ISetorService _setores;
    private readonly IFuncionarioService _servico;
    private readonly IExpedienteService? _expedientes;
    private TabelaHorarios _expedienteGeral = TabelaHorarios.Vazia;
    private int _id;

    /// <summary>Filiais fixas.</summary>
    public IReadOnlyList<FilialOpcao> Filiais { get; } = global::Escalas.Domain.Entidades.Filiais.Todas;
    public ObservableCollection<Setor> Setores { get; } = [];
    /// <summary>"Nenhuma" + as filiais diferentes da selecionada.</summary>
    public ObservableCollection<FilialExtraOpcao> FiliaisExtra { get; } = [];
    public ObservableCollection<Funcionario> Itens { get; } = [];

    [ObservableProperty] public partial FilialOpcao? FilialSelecionada { get; set; }
    [ObservableProperty] public partial Funcionario? Selecionado { get; set; }
    [ObservableProperty] public partial Setor? SetorSelecionado { get; set; }
    /// <summary>Filial em que o funcionário pode cobrir a folga dos fixos (opcional).</summary>
    [ObservableProperty] public partial FilialExtraOpcao? FilialExtraSelecionada { get; set; }
    [ObservableProperty] public partial string Nome { get; set; } = "";
    [ObservableProperty] public partial bool Ativo { get; set; } = true;
    [ObservableProperty] public partial byte[]? Foto { get; set; }

    /// <summary>Usa os horários abaixo no lugar do expediente geral (folgas continuam na escala semanal).</summary>
    [ObservableProperty] public partial bool HorarioPersonalizado { get; set; }

    /// <summary>Segunda a sábado; dia vazio segue o expediente geral (mostrado como sugestão no campo).</summary>
    public ObservableCollection<ExpedienteDiaItem> HorariosDias { get; } = [];

    [RelayCommand]
    private void LimparHorarioDia(ExpedienteDiaItem dia) => dia.Limpar();

    /// <summary>Repete o horário do primeiro dia preenchido nos demais dias.</summary>
    [RelayCommand]
    private void RepetirPrimeiroHorario()
    {
        var modelo = HorariosDias.FirstOrDefault(d => !string.IsNullOrWhiteSpace(d.Inicio) || !string.IsNullOrWhiteSpace(d.Fim));
        if (modelo is null) { Erro = "Preencha um dia para repetir."; return; }
        foreach (var d in HorariosDias.Where(d => d != modelo))
            (d.Inicio, d.Fim) = (modelo.Inicio, modelo.Fim);
        Erro = null;
    }

    private void PreencherHorarios(Funcionario? f)
    {
        HorariosDias.Clear();
        foreach (var dia in ExpedienteSemanaViewModel.DiasEditaveis)
        {
            var h = f?.Horarios.FirstOrDefault(x => x.DiaSemana == dia);
            var geral = _expedienteGeral.Geral(dia);
            HorariosDias.Add(new ExpedienteDiaItem(dia, h is null ? null : new ExpedienteDto(dia, h.Inicio, h.Fim),
                geral is null ? "Sem expediente" : $"Padrão ({RegraHoras.FormatarIntervalo(geral.Inicio, geral.Fim)})", geral));
        }
    }

    /// <summary>Horários digitados (só os dias preenchidos); lança <see cref="CadastroException"/> se algum for inválido.</summary>
    private List<HorarioFuncionario> LerHorarios()
    {
        var lista = new List<HorarioFuncionario>();
        foreach (var d in HorariosDias)
        {
            if (!d.Ler(out var e, out var erro)) throw new CadastroException($"Horário de {d.Titulo}: {erro}.");
            if (e.Inicio is { } i && e.Fim is { } f)
                lista.Add(new HorarioFuncionario { DiaSemana = d.Dia, Inicio = i, Fim = f });
        }
        return lista;
    }

    /// <summary>Lê a imagem escolhida, reduz para um quadrado pequeno e guarda como PNG (a foto vai dentro do banco).</summary>
    public void DefinirFoto(Stream imagem)
    {
        Erro = null;
        try
        {
            using var reduzida = Bitmap.DecodeToWidth(imagem, 192, BitmapInterpolationMode.HighQuality);
            using var saida = new MemoryStream();
            reduzida.Save(saida, new PngBitmapEncoderOptions());
            Foto = saida.ToArray();
        }
        catch
        {
            Erro = "Não foi possível ler essa imagem. Use um arquivo PNG ou JPG.";
        }
    }

    [RelayCommand]
    private void RemoverFoto() => Foto = null;

    public FuncionariosViewModel(ISetorService setores, IFuncionarioService servico, IExpedienteService? expedientes = null)
    {
        _setores = setores;
        _servico = servico;
        _expedientes = expedientes;
        PreencherHorarios(null);
        FilialSelecionada = Filiais[0];
    }

    partial void OnFilialSelecionadaChanged(FilialOpcao? value)
    {
        FiliaisExtra.Clear();
        FiliaisExtra.Add(Nenhuma);
        foreach (var f in Filiais.Where(f => f.Id != value?.Id)) FiliaisExtra.Add(new FilialExtraOpcao(f.Id, f.Nome));
        FilialExtraSelecionada = Nenhuma;
        _ = RecarregarFilialAsync(value);
    }

    private static readonly FilialExtraOpcao Nenhuma = new(null, "Nenhuma");

    private Task RecarregarFilialAsync(FilialOpcao? value) => ExecutarAsync(async () =>
    {
        // Expediente geral: aparece como sugestão nos dias do horário personalizado.
        if (_expedientes is not null)
        {
            _expedienteGeral = await _expedientes.ObterTabelaAsync();
            PreencherHorarios(null);
        }
        Setores.Clear();
        if (value is not null)
            foreach (var s in await _setores.ListarAsync(value.Id)) Setores.Add(s);
        await CarregarAsync(null);
    });

    partial void OnSelecionadoChanged(Funcionario? value)
    {
        _id = value?.Id ?? 0;
        Nome = value?.Nome ?? "";
        Ativo = value?.Ativo ?? true;
        Foto = value?.Foto;
        HorarioPersonalizado = value?.HorarioPersonalizado ?? false;
        PreencherHorarios(value);
        SetorSelecionado = value is null ? null : Setores.FirstOrDefault(s => s.Id == value.SetorId);
        FilialExtraSelecionada = FiliaisExtra.FirstOrDefault(f => f.Id == value?.FilialExtra) ?? Nenhuma;
    }

    private async Task CarregarAsync(int? selecionarId)
    {
        Itens.Clear();
        if (FilialSelecionada is null) return;
        foreach (var f in await _servico.ListarAsync(FilialSelecionada.Id)) Itens.Add(f);
        Selecionado = selecionarId is int id ? Itens.FirstOrDefault(f => f.Id == id) : null;
    }

    [RelayCommand]
    private void Novo()
    {
        Erro = null;
        Selecionado = null;
        OnSelecionadoChanged(null);
    }

    [RelayCommand]
    private Task SalvarAsync() => ExecutarAsync(async () =>
    {
        if (FilialSelecionada is null) throw new CadastroException("Selecione uma filial.");
        var salvo = await _servico.SalvarAsync(new Funcionario
        {
            Id = _id,
            Nome = Nome,
            Filial = FilialSelecionada.Id,
            SetorId = SetorSelecionado?.Id ?? 0,
            FilialExtra = FilialExtraSelecionada?.Id,
            Ativo = Ativo,
            Foto = Foto,
            HorarioPersonalizado = HorarioPersonalizado,
            Horarios = HorarioPersonalizado ? LerHorarios() : [],
        });
        await CarregarAsync(null);
        Novo();
    });

    [RelayCommand]
    private Task ExcluirAsync() => ExecutarAsync(async () =>
    {
        if (_id == 0) return;
        await _servico.ExcluirAsync(_id);
        await CarregarAsync(null);
    });
}
