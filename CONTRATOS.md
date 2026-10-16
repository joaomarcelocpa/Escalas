# Contratos de trabalho entre módulos (uso interno de desenvolvimento)

- Serviços em Infrastructure usam `IDbContextFactory<EscalasDbContext>` (nunca um DbContext singleton). Testes usam SQLite em memória com uma factory própria.
- Interfaces e regras puras ficam em Application/Domain, por feature (pasta própria por módulo).
- Cada módulo registra seus serviços em `Infrastructure/Modulos/<Modulo>Modulo.cs` e sua UI em `Desktop/Modulos/<Modulo>UiModulo.cs` (que também adiciona itens ao menu).
- ViewModels: `Desktop/ViewModels/<Nome>ViewModel.cs`; Views: `Desktop/Views/<Nome>View.axaml` (o ViewLocator troca ViewModel→View). Compiled bindings (`x:DataType`) obrigatórios. Só tokens de `Styles/Tokens.axaml`, nada de cores/tamanhos soltos.
- `IFeriadoConsulta` (Application/Feriados) é o contrato que o calendário usa para feriados.
