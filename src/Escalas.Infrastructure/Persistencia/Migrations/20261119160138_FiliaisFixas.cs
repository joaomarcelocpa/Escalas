using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Escalas.Infrastructure.Persistencia.Migrations
{
    /// <summary>
    /// Troca a tabela de filiais pelas filiais fixas (Matriz=1, Matozinhos=2, FC=3, Interiores=4, Depósito=5).
    /// Setores, funcionários, dias fechados e feriados locais passam a guardar o número da filial fixa na coluna "Filial".
    /// A conversão é pelo nome da filial antiga, ignorando maiúsculas, acentos e espaços nas pontas.
    /// Nada é apagado: a tabela antiga fica preservada em "FiliaisAntigas" (com cidade, UF e a filial fixa correspondente).
    /// Se uma filial antiga com dados não corresponder a nenhuma fixa (ou duas corresponderem à mesma), a migration
    /// falha e o banco fica como estava; o app avisa antes (veja <c>VerificacaoFiliaisFixas</c>).
    /// Tudo roda em uma única transação, com chaves estrangeiras desligadas só para recriar as tabelas
    /// (sem isso, recriar Funcionarios apagaria em cascata as escalas).
    /// </summary>
    public partial class FiliaisFixas : Migration
    {
        public const string IdMigracao = "20261119160138_FiliaisFixas";

        private static readonly (string De, string Para)[] Acentos =
        [
            ("á", "a"), ("à", "a"), ("â", "a"), ("ã", "a"), ("ä", "a"), ("Á", "a"), ("À", "a"), ("Â", "a"), ("Ã", "a"), ("Ä", "a"),
            ("é", "e"), ("è", "e"), ("ê", "e"), ("ë", "e"), ("É", "e"), ("È", "e"), ("Ê", "e"), ("Ë", "e"),
            ("í", "i"), ("ì", "i"), ("î", "i"), ("ï", "i"), ("Í", "i"), ("Ì", "i"), ("Î", "i"), ("Ï", "i"),
            ("ó", "o"), ("ò", "o"), ("ô", "o"), ("õ", "o"), ("ö", "o"), ("Ó", "o"), ("Ò", "o"), ("Ô", "o"), ("Õ", "o"), ("Ö", "o"),
            ("ú", "u"), ("ù", "u"), ("û", "u"), ("ü", "u"), ("Ú", "u"), ("Ù", "u"), ("Û", "u"), ("Ü", "u"),
            ("ç", "c"), ("Ç", "c"),
        ];

        private static readonly (string Chave, int Numero)[] Fixas =
            [("matriz", 1), ("matozinhos", 2), ("fc", 3), ("interiores", 4), ("deposito", 5)];

        /// <summary>
        /// Número da filial fixa para um nome antigo (null se não corresponder). Mesmo critério do SQL da migration:
        /// tira espaços das pontas, passa para minúsculas e remove acentos.
        /// </summary>
        public static int? FilialFixaDe(string nome)
        {
            var n = (nome ?? "").Trim(' ').ToLowerInvariant();
            foreach (var (de, para) in Acentos) n = n.Replace(de, para);
            foreach (var (chave, numero) in Fixas) if (n == chave) return numero;
            return null;
        }

        /// <summary>Expressão SQL: 1 se a filial antiga (coluna de id informada) tem setores, funcionários, dias ou feriados.</summary>
        public static string SqlTemDados(string colunaId) =>
            $"(EXISTS (SELECT 1 FROM \"Setores\" WHERE \"FilialId\" = {colunaId})" +
            $" OR EXISTS (SELECT 1 FROM \"Funcionarios\" WHERE \"FilialId\" = {colunaId})" +
            $" OR EXISTS (SELECT 1 FROM \"DiasFilial\" WHERE \"FilialId\" = {colunaId})" +
            $" OR EXISTS (SELECT 1 FROM \"Feriados\" WHERE \"FilialId\" = {colunaId}))";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("PRAGMA foreign_keys = 0;", suppressTransaction: true);

            migrationBuilder.Sql($"""
                CREATE TABLE "FiliaisAntigas" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_FiliaisAntigas" PRIMARY KEY,
                    "Nome" TEXT NOT NULL,
                    "Cidade" TEXT NOT NULL,
                    "UF" TEXT NOT NULL,
                    "FilialFixa" INTEGER NULL
                );
                CREATE TEMP TABLE "NomesFiliais" AS SELECT "Id", lower(trim("Nome")) AS "N" FROM "Filiais";
                {string.Concat(Acentos.Select(x => $"UPDATE \"NomesFiliais\" SET \"N\" = replace(\"N\", '{x.De}', '{x.Para}');\n"))}
                INSERT INTO "FiliaisAntigas" ("Id", "Nome", "Cidade", "UF", "FilialFixa")
                SELECT f."Id", f."Nome", f."Cidade", f."UF",
                       CASE n."N" {string.Concat(Fixas.Select(x => $"WHEN '{x.Chave}' THEN {x.Numero} "))}END
                FROM "Filiais" f JOIN "NomesFiliais" n ON n."Id" = f."Id";
                DROP TABLE "NomesFiliais";
                """);

            // Trava de segurança: qualquer linha aqui derruba a migration inteira (CHECK sempre falso) e nada muda.
            migrationBuilder.Sql($"""
                CREATE TEMP TABLE "VerificacaoFiliais" (
                    "Nome" TEXT,
                    CONSTRAINT "Filial_antiga_sem_correspondencia_ou_duplicada" CHECK (0)
                );
                INSERT INTO "VerificacaoFiliais" ("Nome")
                SELECT a."Nome" FROM "FiliaisAntigas" a
                WHERE {SqlTemDados("a.\"Id\"")}
                  AND (a."FilialFixa" IS NULL OR EXISTS (
                        SELECT 1 FROM "FiliaisAntigas" b
                        WHERE b."FilialFixa" = a."FilialFixa" AND b."Id" <> a."Id" AND {SqlTemDados("b.\"Id\"")}));
                DROP TABLE "VerificacaoFiliais";
                """);

            // LEFT JOIN: um vínculo sem correspondência vira NULL e a restrição NOT NULL aborta, em vez de sumir com a linha.
            migrationBuilder.Sql("""
                CREATE TABLE "ef_temp_Setores" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_Setores" PRIMARY KEY AUTOINCREMENT,
                    "Filial" INTEGER NOT NULL,
                    "MinimoFuncionarios" INTEGER NOT NULL,
                    "Nome" TEXT NOT NULL
                );
                INSERT INTO "ef_temp_Setores" ("Id", "Filial", "MinimoFuncionarios", "Nome")
                SELECT s."Id", a."FilialFixa", s."MinimoFuncionarios", s."Nome"
                FROM "Setores" s LEFT JOIN "FiliaisAntigas" a ON a."Id" = s."FilialId";

                CREATE TABLE "ef_temp_Funcionarios" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_Funcionarios" PRIMARY KEY AUTOINCREMENT,
                    "Ativo" INTEGER NOT NULL,
                    "Filial" INTEGER NOT NULL,
                    "Foto" BLOB NULL,
                    "Nome" TEXT NOT NULL,
                    "SetorId" INTEGER NOT NULL,
                    CONSTRAINT "FK_Funcionarios_Setores_SetorId" FOREIGN KEY ("SetorId") REFERENCES "Setores" ("Id") ON DELETE RESTRICT
                );
                INSERT INTO "ef_temp_Funcionarios" ("Id", "Ativo", "Filial", "Foto", "Nome", "SetorId")
                SELECT f."Id", f."Ativo", a."FilialFixa", f."Foto", f."Nome", f."SetorId"
                FROM "Funcionarios" f LEFT JOIN "FiliaisAntigas" a ON a."Id" = f."FilialId";

                CREATE TABLE "ef_temp_DiasFilial" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_DiasFilial" PRIMARY KEY AUTOINCREMENT,
                    "Data" TEXT NOT NULL,
                    "Fechado" INTEGER NOT NULL,
                    "Filial" INTEGER NOT NULL
                );
                INSERT INTO "ef_temp_DiasFilial" ("Id", "Data", "Fechado", "Filial")
                SELECT d."Id", d."Data", d."Fechado", a."FilialFixa"
                FROM "DiasFilial" d LEFT JOIN "FiliaisAntigas" a ON a."Id" = d."FilialId";

                CREATE TABLE "ef_temp_Feriados" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_Feriados" PRIMARY KEY AUTOINCREMENT,
                    "Abrangencia" INTEGER NOT NULL,
                    "Cidade" TEXT NULL,
                    "Data" TEXT NOT NULL,
                    "Filial" INTEGER NULL,
                    "Nome" TEXT NOT NULL,
                    "UF" TEXT NULL
                );
                INSERT INTO "ef_temp_Feriados" ("Id", "Abrangencia", "Cidade", "Data", "Filial", "Nome", "UF")
                SELECT r."Id", r."Abrangencia", r."Cidade", r."Data", a."FilialFixa", r."Nome", r."UF"
                FROM "Feriados" r LEFT JOIN "FiliaisAntigas" a ON a."Id" = r."FilialId";

                DROP TABLE "Setores";
                ALTER TABLE "ef_temp_Setores" RENAME TO "Setores";
                DROP TABLE "Funcionarios";
                ALTER TABLE "ef_temp_Funcionarios" RENAME TO "Funcionarios";
                DROP TABLE "DiasFilial";
                ALTER TABLE "ef_temp_DiasFilial" RENAME TO "DiasFilial";
                DROP TABLE "Feriados";
                ALTER TABLE "ef_temp_Feriados" RENAME TO "Feriados";
                DROP TABLE "Filiais";

                CREATE INDEX "IX_Setores_Filial" ON "Setores" ("Filial");
                CREATE INDEX "IX_Funcionarios_Filial" ON "Funcionarios" ("Filial");
                CREATE INDEX "IX_Funcionarios_SetorId" ON "Funcionarios" ("SetorId");
                CREATE UNIQUE INDEX "IX_DiasFilial_Filial_Data" ON "DiasFilial" ("Filial", "Data");
                CREATE INDEX "IX_Feriados_Data" ON "Feriados" ("Data");
                CREATE INDEX "IX_Feriados_Filial" ON "Feriados" ("Filial");
                """);

            // Confere as chaves estrangeiras que sobraram (escalas → funcionários, funcionários → setores) antes de confirmar.
            migrationBuilder.Sql("""
                CREATE TEMP TABLE "VerificacaoChaves" ("Tabela" TEXT, CONSTRAINT "Chave_estrangeira_quebrada" CHECK (0));
                INSERT INTO "VerificacaoChaves" ("Tabela") SELECT "table" FROM pragma_foreign_key_check;
                DROP TABLE "VerificacaoChaves";
                """);

            migrationBuilder.Sql("PRAGMA foreign_keys = 1;", suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("PRAGMA foreign_keys = 0;", suppressTransaction: true);

            // Recria as filiais a partir das preservadas; filial fixa usada sem filial antiga ganha uma linha nova.
            migrationBuilder.Sql("""
                CREATE TABLE "Filiais" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_Filiais" PRIMARY KEY AUTOINCREMENT,
                    "Cidade" TEXT NOT NULL,
                    "Nome" TEXT NOT NULL,
                    "UF" TEXT NOT NULL
                );
                INSERT INTO "Filiais" ("Id", "Cidade", "Nome", "UF")
                SELECT "Id", "Cidade", "Nome", "UF" FROM "FiliaisAntigas";
                INSERT INTO "Filiais" ("Cidade", "Nome", "UF")
                SELECT '', CASE v.n WHEN 1 THEN 'Matriz' WHEN 2 THEN 'Matozinhos' WHEN 3 THEN 'FC' WHEN 4 THEN 'Interiores' ELSE 'Depósito' END, ''
                FROM (SELECT "Filial" AS n FROM "Setores" UNION SELECT "Filial" FROM "Funcionarios"
                      UNION SELECT "Filial" FROM "DiasFilial" UNION SELECT "Filial" FROM "Feriados" WHERE "Filial" IS NOT NULL) v
                WHERE NOT EXISTS (SELECT 1 FROM "FiliaisAntigas" a WHERE a."FilialFixa" = v.n)
                ORDER BY v.n;
                CREATE TEMP TABLE "MapaFiliais" AS
                SELECT a."FilialFixa" AS "Fixa", MIN(a."Id") AS "Id" FROM "FiliaisAntigas" a WHERE a."FilialFixa" IS NOT NULL GROUP BY a."FilialFixa"
                UNION ALL
                SELECT n, f."Id" FROM (SELECT 1 AS n UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5) x
                JOIN "Filiais" f ON f."Id" NOT IN (SELECT "Id" FROM "FiliaisAntigas")
                    AND f."Nome" = CASE x.n WHEN 1 THEN 'Matriz' WHEN 2 THEN 'Matozinhos' WHEN 3 THEN 'FC' WHEN 4 THEN 'Interiores' ELSE 'Depósito' END
                WHERE NOT EXISTS (SELECT 1 FROM "FiliaisAntigas" a WHERE a."FilialFixa" = x.n);

                CREATE TABLE "ef_temp_Setores" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_Setores" PRIMARY KEY AUTOINCREMENT,
                    "FilialId" INTEGER NOT NULL,
                    "Nome" TEXT NOT NULL,
                    "MinimoFuncionarios" INTEGER NOT NULL,
                    CONSTRAINT "FK_Setores_Filiais_FilialId" FOREIGN KEY ("FilialId") REFERENCES "Filiais" ("Id") ON DELETE RESTRICT
                );
                INSERT INTO "ef_temp_Setores" ("Id", "FilialId", "Nome", "MinimoFuncionarios")
                SELECT s."Id", m."Id", s."Nome", s."MinimoFuncionarios" FROM "Setores" s LEFT JOIN "MapaFiliais" m ON m."Fixa" = s."Filial";

                CREATE TABLE "ef_temp_Funcionarios" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_Funcionarios" PRIMARY KEY AUTOINCREMENT,
                    "FilialId" INTEGER NOT NULL,
                    "SetorId" INTEGER NOT NULL,
                    "Nome" TEXT NOT NULL,
                    "Ativo" INTEGER NOT NULL,
                    "Foto" BLOB NULL,
                    CONSTRAINT "FK_Funcionarios_Filiais_FilialId" FOREIGN KEY ("FilialId") REFERENCES "Filiais" ("Id") ON DELETE RESTRICT,
                    CONSTRAINT "FK_Funcionarios_Setores_SetorId" FOREIGN KEY ("SetorId") REFERENCES "Setores" ("Id") ON DELETE RESTRICT
                );
                INSERT INTO "ef_temp_Funcionarios" ("Id", "FilialId", "SetorId", "Nome", "Ativo", "Foto")
                SELECT f."Id", m."Id", f."SetorId", f."Nome", f."Ativo", f."Foto" FROM "Funcionarios" f LEFT JOIN "MapaFiliais" m ON m."Fixa" = f."Filial";

                CREATE TABLE "ef_temp_DiasFilial" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_DiasFilial" PRIMARY KEY AUTOINCREMENT,
                    "FilialId" INTEGER NOT NULL,
                    "Data" TEXT NOT NULL,
                    "Fechado" INTEGER NOT NULL,
                    CONSTRAINT "FK_DiasFilial_Filiais_FilialId" FOREIGN KEY ("FilialId") REFERENCES "Filiais" ("Id") ON DELETE CASCADE
                );
                INSERT INTO "ef_temp_DiasFilial" ("Id", "FilialId", "Data", "Fechado")
                SELECT d."Id", m."Id", d."Data", d."Fechado" FROM "DiasFilial" d LEFT JOIN "MapaFiliais" m ON m."Fixa" = d."Filial";

                CREATE TABLE "ef_temp_Feriados" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_Feriados" PRIMARY KEY AUTOINCREMENT,
                    "Data" TEXT NOT NULL,
                    "Nome" TEXT NOT NULL,
                    "Abrangencia" INTEGER NOT NULL,
                    "UF" TEXT NULL,
                    "Cidade" TEXT NULL,
                    "FilialId" INTEGER NULL,
                    CONSTRAINT "FK_Feriados_Filiais_FilialId" FOREIGN KEY ("FilialId") REFERENCES "Filiais" ("Id") ON DELETE CASCADE
                );
                INSERT INTO "ef_temp_Feriados" ("Id", "Data", "Nome", "Abrangencia", "UF", "Cidade", "FilialId")
                SELECT r."Id", r."Data", r."Nome", r."Abrangencia", r."UF", r."Cidade", m."Id" FROM "Feriados" r LEFT JOIN "MapaFiliais" m ON m."Fixa" = r."Filial";

                DROP TABLE "Setores";
                ALTER TABLE "ef_temp_Setores" RENAME TO "Setores";
                DROP TABLE "Funcionarios";
                ALTER TABLE "ef_temp_Funcionarios" RENAME TO "Funcionarios";
                DROP TABLE "DiasFilial";
                ALTER TABLE "ef_temp_DiasFilial" RENAME TO "DiasFilial";
                DROP TABLE "Feriados";
                ALTER TABLE "ef_temp_Feriados" RENAME TO "Feriados";
                DROP TABLE "MapaFiliais";
                DROP TABLE "FiliaisAntigas";

                CREATE INDEX "IX_Setores_FilialId" ON "Setores" ("FilialId");
                CREATE INDEX "IX_Funcionarios_FilialId" ON "Funcionarios" ("FilialId");
                CREATE INDEX "IX_Funcionarios_SetorId" ON "Funcionarios" ("SetorId");
                CREATE UNIQUE INDEX "IX_DiasFilial_FilialId_Data" ON "DiasFilial" ("FilialId", "Data");
                CREATE INDEX "IX_Feriados_Data" ON "Feriados" ("Data");
                CREATE INDEX "IX_Feriados_FilialId" ON "Feriados" ("FilialId");
                """);

            migrationBuilder.Sql("PRAGMA foreign_keys = 1;", suppressTransaction: true);
        }
    }
}
