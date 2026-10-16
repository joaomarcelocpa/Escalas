# Sistema de Controle de Escalas de Trabalho

Documento de contexto para iniciar o desenvolvimento. Leia tudo antes de criar qualquer arquivo e, ao final, siga a seção "Primeiros passos".

## 1. Visão geral

Software **desktop nativo** (não é web) para gestão de escalas de trabalho. Cada empresa usa **uma única instalação, em um único computador**, com **dados 100% locais**. O foco é um calendário semanal em que o gestor monta a escala arrastando funcionários entre os dias.

## 2. Modelo de implantação (importante)

- **Uma instalação = uma empresa = um computador = um arquivo de banco SQLite.**
- O banco de cada instalação contém os dados de **uma única empresa**. Não existe multiempresa dentro do app, nem `EmpresaId` nas tabelas.
- O **cadastro da empresa não existe na interface**. O desenvolvedor cria cada empresa por um **script/ferramenta administrativa (gerador de banco)**, rodado na máquina dele, e entrega o resultado ao cliente.
- Não há servidor, API própria nem acesso em rede. Nada de endpoints para o app.

### Gerador de banco (`Escalas.Admin`)
Ferramenta de linha de comando que, dado o nome e o documento da empresa e as credenciais do administrador inicial:
1. Cria o arquivo `.db` (SQLite) já com todas as migrations aplicadas.
2. Insere a **única linha** da tabela `Empresa`.
3. Cria o usuário administrador inicial (senha com hash).
4. Grava o arquivo no local de saída indicado, pronto para ser copiado para `%AppData%/Escalas` na máquina do cliente (ou ao lado do executável na entrega, conforme definido pelo desenvolvedor).

Exemplo de uso desejado:
```
escalas-admin criar --nome "Padaria Exemplo" --documento 00.000.000/0001-00 --admin-login admin --saida ./saida/padaria-exemplo
```

O app, ao abrir sem banco existente, deve mostrar uma mensagem clara (sem tela de criação de empresa).

## 3. Requisitos funcionais (dentro do app)

### Acesso
- Login com usuário e senha (usuários do banco local).
- O administrador pode cadastrar outros usuários da própria empresa (opcional na primeira versão; começar só com o admin inicial).

### Cadastros (livres para a empresa, na interface)
- **Filiais**: cada uma com cidade e estado (necessário para feriados locais).
- **Setores**: cadastrados por filial.
- **Funcionários**: vinculados a uma filial e a um setor.
- **Mínimo de funcionários por setor**: definido por setor (e, se fizer sentido, por dia da semana).

### Calendário e escala
- Visão **semanal** (segunda a domingo), navegação entre semanas.
- Cada dia mostra o status: feriado (com nome), dia fechado ou normal.
- Marcar um dia como **fechado** (a filial não funciona naquele dia).
- Associar funcionários aos dias em que vão trabalhar.
- **Mover funcionários entre dias** com arrastar e soltar.
- **Alerta visual** quando um setor fica abaixo do mínimo em algum dia.
- Feriados vêm de API e são informados de forma discreta (indicador sutil, não um bloco chamativo).

### Proteção e continuidade dos dados
- **Backup**: botão "Exportar backup" (copia o `.db`) e backup automático ao fechar o app, mantendo as últimas N cópias.
- **Atualizações**: ao abrir, o app aplica migrations pendentes automaticamente e faz **backup do `.db` antes de migrar**.
- **Criptografia do banco** (LGPD, dados de funcionários): usar SQLCipher desde a escolha do pacote do SQLite; a chave de criptografia é gerenciada de forma que o cliente não precise digitá-la (definir estratégia com o desenvolvedor). Pode ser ativada em fase posterior, mas o pacote deve ser escolhido já no início.

## 4. Decisões técnicas

| Item | Decisão |
|---|---|
| Linguagem | C# com .NET (versão LTS mais recente) |
| Interface | Avalonia UI, padrão MVVM |
| MVVM | CommunityToolkit.Mvvm |
| Dados | Entity Framework Core + SQLite (um arquivo por empresa) |
| Acesso a dados | Atrás de interfaces (repositórios/serviços), para manter a porta aberta a um backend central no futuro |
| Senhas | Hash com Argon2 ou BCrypt, nunca texto puro |
| Feriados nacionais | BrasilAPI: `GET https://brasilapi.com.br/api/feriados/v1/{ano}` |
| Feriados estaduais/municipais | Tabela própria, vinculada à filial (a API não cobre) |
| Cache de feriados | Baixar por ano e guardar no banco local; funcionar offline depois |
| IDE do desenvolvedor | JetBrains Rider (com plugin AvaloniaRider) |

## 5. Estrutura sugerida da solução

```
Escalas.sln
├── src/
│   ├── Escalas.Domain/          # Entidades e regras (sem dependência de UI nem banco)
│   ├── Escalas.Application/     # Casos de uso, serviços, interfaces de repositório
│   ├── Escalas.Infrastructure/  # EF Core, repositórios, cliente de feriados, hash de senha, backup
│   ├── Escalas.Desktop/         # Avalonia (Views, ViewModels, estilos, recursos)
│   └── Escalas.Admin/           # Gerador de banco (CLI) para criar cada empresa
└── tests/
    └── Escalas.Tests/           # Testes das regras (mínimo por setor, mover entre dias etc.)
```

### Modelo de dados (rascunho, sem multiempresa)
- Empresa (Id, Nome, Documento) — **sempre uma única linha**
- Usuario (Id, Login, SenhaHash, Papel)
- Filial (Id, Nome, Cidade, UF)
- Setor (Id, FilialId, Nome, MinimoFuncionarios)
- Funcionario (Id, FilialId, SetorId, Nome, Ativo)
- DiaFilial (Id, FilialId, Data, Fechado)
- Escala (Id, FuncionarioId, Data) — um registro por funcionário escalado em um dia
- Feriado (Id, Data, Nome, Abrangencia: Nacional/Estadual/Municipal, UF, Cidade, FilialId opcional)

Garantir por regra de aplicação (e, se possível, por constraint) que a tabela `Empresa` tenha no máximo uma linha.

## 6. Diretrizes de interface

Objetivo: profissional, minimalista, **sem aparência genérica ou de template**.

- Definir uma identidade própria antes de montar telas: uma fonte com personalidade para títulos combinada com uma fonte neutra e legível para o corpo, **embutidas como recurso do app** (não depender das fontes do sistema).
- Paleta curta: neutros levemente quentes ou frios, **uma única cor de destaque**. Evitar o azul padrão.
- Cor com função, nunca decorativa: vermelho somente para "abaixo do mínimo", cinza para dia fechado, indicador sutil para feriado.
- Alta densidade de informação e visual escaneável. Evitar cartões enormes, sombras pesadas e cantos excessivamente arredondados.
- Bordas finas, espaçamento consistente em escala fixa (4, 8, 12, 16, 24), poucos tipos de componente.
- Centralizar cores, tipografia e espaçamentos em dicionários de recursos/estilos do Avalonia (tokens), nunca valores soltos nas telas.
- Tema claro primeiro; estruturar os tokens para permitir tema escuro depois.
- Usar *compiled bindings* (`x:DataType`) em todas as views.

## 7. Distribuição

- Alvo inicial: **Windows x64**, arquivo único autocontido:
  ```
  dotnet publish src/Escalas.Desktop -c Release -r win-x64 --self-contained true \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile=true
  ```
- Entrega ao cliente: o executável + o `.db` gerado pelo `Escalas.Admin` para aquela empresa.
- Dados e configurações ficam em `%AppData%/Escalas` (ou equivalente em cada sistema), nunca dentro do executável.
- Manter o código livre de dependências específicas de Windows, para publicar depois em Linux (`linux-x64`) e macOS (`osx-arm64`, `osx-x64`).
- Futuro: instalador e atualização automática com Velopack; assinatura de código para evitar alerta do SmartScreen.

## 8. Primeiros passos (ordem de execução)

1. Verificar se o .NET SDK está instalado (`dotnet --version`). Se não estiver, avisar o desenvolvedor e parar.
2. Instalar os templates do Avalonia: `dotnet new install Avalonia.Templates`.
3. Criar a solução e os projetos da seção 5 na pasta atual, com as referências corretas entre eles (Domain não referencia nada; Application referencia Domain; Infrastructure referencia Application; Desktop referencia Application e Infrastructure; Admin referencia Application e Infrastructure).
4. Adicionar os pacotes: CommunityToolkit.Mvvm, EF Core para SQLite (escolhendo desde já a variante compatível com SQLCipher), Microsoft.EntityFrameworkCore.Design, Konscious.Security.Cryptography (Argon2) ou BCrypt.Net-Next.
5. Criar `.gitignore` para .NET, iniciar o repositório Git e fazer o primeiro commit.
6. Verificar que `dotnet build` passa e que a janela do Avalonia abre.

## 9. Roadmap por fases

1. **Fundação**: solução, entidades, EF Core, migrations, tokens de estilo e janela principal.
2. **Gerador e acesso**: `Escalas.Admin` (cria `.db`, empresa e admin) e tela de login com hash de senha.
3. **Cadastros**: filiais, setores (com mínimo) e funcionários, com listas e formulários.
4. **Feriados**: cliente BrasilAPI, cache local e cadastro de feriados locais por filial.
5. **Calendário semanal**: visão da semana, status dos dias, fechar dia, escalar funcionários.
6. **Arrastar e soltar** entre dias e alerta de mínimo por setor.
7. **Continuidade**: backup manual e automático, migrations automáticas com backup prévio.
8. **Acabamento**: criptografia do banco, testes das regras, empacotamento Windows, ajustes visuais.

Trabalhar uma fase por vez, com `dotnet build` e testes passando antes de avançar. Pedir confirmação ao desenvolvedor ao final de cada fase.