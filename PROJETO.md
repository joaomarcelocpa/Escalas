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
Ferramenta de linha de comando que, dado o nome e o documento da empresa:
1. Cria o arquivo `.db` (SQLite) já com todas as migrations aplicadas.
2. Insere a **única linha** da tabela `Empresa`.
3. Grava o arquivo no local de saída indicado, pronto para ser copiado para `%AppData%/Escalas` na máquina do cliente (ou ao lado do executável na entrega, conforme definido pelo desenvolvedor).

Exemplo de uso desejado:
```
escalas-admin criar --nome "Padaria Exemplo" --documento 00.000.000/0001-00 --saida ./saida/padaria-exemplo
```

O app, ao abrir sem banco existente, deve mostrar uma mensagem clara (sem tela de criação de empresa).

## 3. Requisitos funcionais (dentro do app)

### Acesso
- Sem login: o app é nativo e roda só no PC da empresa, então abre direto na tela principal.

### Filiais (fixas, sem cadastro)
- A empresa tem sempre as mesmas cinco filiais: **Matriz, Matozinhos, FC, Interiores e Depósito**. Não existe tela nem cadastro de filial.
- No código são o enum `Filial` (Matriz=1, Matozinhos=2, FC=3, Interiores=4, Deposito=5) com nomes de exibição em `Filiais.Todas` (`Escalas.Domain`). O número é o que fica gravado no banco: nunca renumerar nem reaproveitar.
- Bancos antigos (com tabela `Filiais`) são convertidos pela migration `FiliaisFixas`, com backup antes, ligando cada filial antiga à fixa de mesmo nome (ignorando maiúsculas, acentos e espaços nas pontas). A tabela antiga fica guardada como `FiliaisAntigas` (nome, cidade, UF e a filial fixa correspondente). Se uma filial antiga com dados não corresponder a nenhuma fixa, ou duas corresponderem à mesma, o app avisa quais são e não altera o banco; o `empacotar` também recusa gerar o pacote nesse caso.

### Cadastros (livres para a empresa, na interface)
- **Setores**: cadastrados por filial.
- **Funcionários**: vinculados a uma filial e a um setor. Opcionalmente têm uma **filial extra**, onde podem cobrir a folga dos funcionários fixos de lá.
- **Mínimo de funcionários por setor**: definido por setor (e, se fizer sentido, por dia da semana).

### Calendário e escala
- Visão **semanal** (segunda a domingo), navegação entre semanas.
- Cada dia mostra o status: feriado (com nome), dia fechado ou normal.
- Marcar um dia como **fechado** (a filial não funciona naquele dia).
- A escala é montada no **modelo semanal** (arrastar e soltar entre dias da semana).
- O calendário é **somente leitura** para pessoas: mostra quem está escalado e quem está de folga em cada dia, sem incluir, remover ou mover alguém pontualmente.
- **Alerta visual** quando um setor fica abaixo do mínimo em algum dia.
- Feriados vêm de API e são informados de forma discreta (indicador sutil, não um bloco chamativo).

### Cobertura de folga (filial extra)
- No cadastro do funcionário, "Filial extra (opcional)": qualquer filial diferente da dele. Quem só tem a filial como extra não aparece como folga nela.
- A cobertura é definida na **Escala semanal** da filial coberta e repete em todas as semanas: nos dias em que algum fixo da filial está de folga, a caixa "Cobrir folga com…" lista os ativos que têm essa filial como extra e trabalham na filial deles nesse dia.
- Cobrir troca o lugar do dia: o funcionário sai da filial dele naquele dia e vai para a filial coberta (fica gravado na própria linha do modelo semanal, então nunca fica em duas filiais no mesmo dia). Não dá para cobrir no dia de folga do próprio cobridor. O "×" desfaz a cobertura e ele volta para a filial dele.
- Na filial dele, o dia de cobertura aparece em "Cobertura de folga" / "Em outra filial" (não é folga) e não pode ser alocado, movido ou removido por lá.
- Calendário: o cobridor aparece entre os escalados da filial coberta, marcado "Cobertura · filial de origem", e conta no mínimo do setor de mesmo nome. Dia fechado na filial coberta não tem cobertura.
- Horas: o dia de cobertura conta na semana do funcionário (popup mostra "Cobre X"). Resumo e planilha mostram o funcionário só na filial dele, com o dia de cobertura marcado com a filial coberta; a filial coberta não lista quem veio cobrir.
- Trocar a filial ou a filial extra do funcionário desfaz as coberturas dele.

### Gestão de horários
- **Expediente por dia da semana**: definido na tela **Escala semanal** (botão "Expediente" ou clique no horário de um dia), de segunda a sábado. Repete em todas as semanas e vale para todas as filiais e para todos os escalados no dia; não atravessa a meia-noite. O calendário só exibe o horário.
- **Almoço fixo**: 1h30 descontada de todos os funcionários nos dias com 8h ou mais de expediente (dias abaixo de 8h não descontam). O popup indica quando houve desconto; resumo e exportações mostram só as horas já descontadas.
- **Horas** = fim − início do expediente (menos o almoço, quando houver), somadas nos dias em que o funcionário está escalado (mesma regra do calendário: modelo semanal, exceções, dia fechado e feriado). Dia escalado sem expediente aparece como "Sem expediente" e não soma.
- **Popup do funcionário**: clicar em um funcionário no calendário mostra os dias escalados da semana visível, o horário e as horas de cada dia, o total até hoje e o total da semana.
- **Exportar planilha** (Escala semanal): .xlsx simples com uma aba por filial: uma linha por funcionário (nome e setor) e uma coluna por dia, com o horário do expediente nos dias em que trabalha ("Escalado" se o dia não tem expediente) e "Descanso" na folga. A última coluna traz as horas da semana, já com o almoço descontado.
- **Resumo semanal**: botão no canto do calendário; segunda a sábado (sem domingo); todos os funcionários ativos por filial, com o horário e as horas de cada dia e o total de cada funcionário (sem total por filial). Só visualização (sem exportação).

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
| Feriados nacionais | BrasilAPI: `GET https://brasilapi.com.br/api/feriados/v1/{ano}` |
| Feriados estaduais/municipais | Tabela própria, vinculada à filial fixa (a API não cobre); valem só para a filial a que foram vinculados |
| Cache de feriados | Baixar por ano e guardar no banco local; funcionar offline depois |
| IDE do desenvolvedor | JetBrains Rider (com plugin AvaloniaRider) |

## 5. Estrutura sugerida da solução

```
Escalas.sln
├── src/
│   ├── Escalas.Domain/          # Entidades e regras (sem dependência de UI nem banco)
│   ├── Escalas.Application/     # Casos de uso, serviços, interfaces de repositório
│   ├── Escalas.Infrastructure/  # EF Core, repositórios, cliente de feriados, backup
│   ├── Escalas.Desktop/         # Avalonia (Views, ViewModels, estilos, recursos)
│   └── Escalas.Admin/           # Gerador de banco (CLI) para criar cada empresa
└── tests/
    └── Escalas.Tests/           # Testes das regras (mínimo por setor, mover entre dias etc.)
```

### Modelo de dados (rascunho, sem multiempresa)
- Empresa (Id, Nome, Documento) — **sempre uma única linha**
- Filial: **não é tabela**; é o enum fixo gravado como número na coluna `Filial` das tabelas abaixo
- Setor (Id, Filial, Nome, MinimoFuncionarios)
- Funcionario (Id, Filial, FilialExtra opcional, SetorId, Nome, Ativo, Foto)
- DiaFilial (Id, Filial, Data, Fechado)
- Escala (Id, FuncionarioId, Data, Removido) — exceção pontual ao modelo semanal
- EscalaPadrao (Id, FuncionarioId, DiaSemana, FilialCobertura opcional) — modelo semanal; FilialCobertura preenchida = nesse dia cobre a filial extra
- Expediente (Id, DiaSemana único, Inicio, Fim) — tabela ExpedientesSemana; horário geral do dia da semana, igual para todas as filiais
- Feriado (Id, Data, Nome, Abrangencia: Nacional/Estadual/Municipal, UF, Cidade, Filial opcional)
- FiliaisAntigas (fora do modelo do EF) — cópia da antiga tabela de filiais, guardada pela migration `FiliaisFixas`

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
- Entrega ao cliente: `./empacotar.sh` gera um único arquivo, `saida/Escalas.exe`, com o banco atual (`escalas.db`) e a chave embutidos como recursos. No PC da empresa fica só o `.exe` (sem pasta nem arquivos soltos): no primeiro uso o app extrai o banco para `%AppData%\Escalas`, guarda a chave protegida pelo Windows (DPAPI) e passa a usar esses dados. Se já houver banco na pasta de dados, o conteúdo embutido é ignorado (nunca sobrescreve). Limitação: a chave embutida pode ser extraída por quem tiver o `.exe`, então o arquivo deve ser tratado como confidencial. O formato antigo (`escalas.db`/`escalas.chave` ao lado do executável) continua sendo aceito.
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
2. **Gerador e acesso**: `Escalas.Admin` (cria `.db` e empresa).
3. **Cadastros**: setores (com mínimo) e funcionários, com listas e formulários (filiais são fixas).
4. **Feriados**: cliente BrasilAPI, cache local e cadastro de feriados locais por filial.
5. **Calendário semanal**: visão da semana, status dos dias, fechar dia, escalar funcionários.
6. **Arrastar e soltar** entre dias e alerta de mínimo por setor.
7. **Continuidade**: backup manual e automático, migrations automáticas com backup prévio.
8. **Acabamento**: criptografia do banco, testes das regras, empacotamento Windows, ajustes visuais.

Trabalhar uma fase por vez, com `dotnet build` e testes passando antes de avançar. Pedir confirmação ao desenvolvedor ao final de cada fase.