using Escalas.Application.Nucleo;
using Escalas.Infrastructure.Nucleo;

const string Uso = """
    escalas-admin — gerador de banco por empresa

    Uso:
      escalas-admin criar --nome "Padaria Exemplo" --documento 00.000.000/0001-00 --saida ./saida/padaria-exemplo

    --saida: diretório (recebe escalas.db e escalas.chave) ou caminho terminando em .db.

      escalas-admin empacotar --saida ./saida/pacote/Escalas [--banco ./saida/fabianamoveis/escalas.db]

    Copia o banco atual e a chave dele para a pasta do executável. No PC da empresa o app importa
    os dois sozinho no primeiro uso. Sem --banco, usa o mesmo banco que o app usa (ESCALAS_DB ou a pasta padrão).
    """;

if (args.Length == 0 || args[0] is "-h" or "--help" or "ajuda")
{
    Console.WriteLine(Uso);
    return args.Length == 0 ? 1 : 0;
}

if (args[0] is not ("criar" or "empacotar"))
{
    Console.Error.WriteLine($"Comando desconhecido: {args[0]}\n\n{Uso}");
    return 1;
}

var opcoes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
for (var i = 1; i < args.Length; i++)
{
    if (!args[i].StartsWith("--") || i + 1 >= args.Length)
    {
        Console.Error.WriteLine($"Argumento inválido: {args[i]}\n\n{Uso}");
        return 1;
    }
    opcoes[args[i][2..]] = args[++i];
}

string? Opcao(string nome) => opcoes.GetValueOrDefault(nome);

if (args[0] == "empacotar")
{
    if (string.IsNullOrWhiteSpace(Opcao("saida")))
    {
        Console.Error.WriteLine($"Faltando: --saida\n\n{Uso}");
        return 1;
    }
    try
    {
        var caminhos = new CaminhosApp(Opcao("banco") is { Length: > 0 } b ? Path.GetFullPath(b) : null);
        var empacotador = new EmpacotadorInstalacao(caminhos, new ChaveStore(caminhos));
        var p = await empacotador.EmpacotarAsync(Opcao("saida")!);
        Console.WriteLine($"Banco copiado: {caminhos.CaminhoBanco}");
        Console.WriteLine($"Pacote:        {Path.GetDirectoryName(p.CaminhoBanco)}");
        return 0;
    }
    catch (EmpacotamentoException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 2;
    }
}

var faltando = new[] { "nome", "documento", "saida" }.Where(o => string.IsNullOrWhiteSpace(Opcao(o))).ToList();
if (faltando.Count > 0)
{
    Console.Error.WriteLine($"Faltando: {string.Join(", ", faltando.Select(f => "--" + f))}\n\n{Uso}");
    return 1;
}

try
{
    var criador = new CriadorDeEmpresa();
    var r = await criador.CriarAsync(Opcao("nome")!, Opcao("documento")!, Opcao("saida")!);
    Console.WriteLine($"Banco criado:  {r.CaminhoBanco}");
    Console.WriteLine($"Chave gerada:  {r.CaminhoChave}");
    Console.WriteLine("Entregue os dois arquivos ao cliente, na mesma pasta de dados do app. A chave é importada e apagada no primeiro uso.");
    return 0;
}
catch (CriacaoEmpresaException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}
