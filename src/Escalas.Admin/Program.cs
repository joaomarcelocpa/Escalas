using System.Text;
using Escalas.Application.Nucleo;
using Escalas.Infrastructure.Nucleo;

const string Uso = """
    escalas-admin — gerador de banco por empresa

    Uso:
      escalas-admin criar --nome "Padaria Exemplo" --documento 00.000.000/0001-00 \
                          --admin-login admin [--admin-senha <senha>] --saida ./saida/padaria-exemplo

    --saida: diretório (recebe escalas.db e escalas.chave) ou caminho terminando em .db.
    Sem --admin-senha, a senha é pedida no console (sem eco).
    """;

if (args.Length == 0 || args[0] is "-h" or "--help" or "ajuda")
{
    Console.WriteLine(Uso);
    return args.Length == 0 ? 1 : 0;
}

if (args[0] != "criar")
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

var faltando = new[] { "nome", "documento", "admin-login", "saida" }.Where(o => string.IsNullOrWhiteSpace(Opcao(o))).ToList();
if (faltando.Count > 0)
{
    Console.Error.WriteLine($"Faltando: {string.Join(", ", faltando.Select(f => "--" + f))}\n\n{Uso}");
    return 1;
}

var senha = Opcao("admin-senha");
if (string.IsNullOrEmpty(senha))
{
    senha = LerSenha("Senha do administrador: ");
    if (senha != LerSenha("Confirme a senha: "))
    {
        Console.Error.WriteLine("As senhas não conferem.");
        return 1;
    }
}

try
{
    var criador = new CriadorDeEmpresa(new BcryptPasswordHasher());
    var r = await criador.CriarAsync(Opcao("nome")!, Opcao("documento")!, Opcao("admin-login")!, senha, Opcao("saida")!);
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

static string LerSenha(string prompt)
{
    Console.Write(prompt);
    if (Console.IsInputRedirected) return Console.ReadLine() ?? "";
    var sb = new StringBuilder();
    while (true)
    {
        var k = Console.ReadKey(intercept: true);
        if (k.Key == ConsoleKey.Enter) break;
        if (k.Key == ConsoleKey.Backspace) { if (sb.Length > 0) sb.Length--; continue; }
        if (!char.IsControl(k.KeyChar)) sb.Append(k.KeyChar);
    }
    Console.WriteLine();
    return sb.ToString();
}
