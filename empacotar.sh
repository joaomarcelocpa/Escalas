#!/usr/bin/env bash
# Gera o executável para o PC da empresa: saida/Escalas.exe (Windows x64, arquivo único).
# O banco atual (escalas.db) e a chave dele vão DENTRO do .exe. No PC da empresa fica só o .exe:
# no primeiro uso o app extrai o banco para %AppData%\Escalas e guarda a chave protegida pelo Windows (DPAPI).
# Se já existir banco em %AppData%\Escalas, ele é mantido (nada é sobrescrito).
#
# Uso: ./empacotar.sh [caminho/do/escalas.db]
#   Sem argumento: usa $ESCALAS_DB; se não houver, saida/fabianamoveis/escalas.db.
set -euo pipefail
cd "$(dirname "$0")"

BANCO="${1:-${ESCALAS_DB:-}}"
if [ -z "$BANCO" ] && [ -f saida/fabianamoveis/escalas.db ]; then BANCO=saida/fabianamoveis/escalas.db; fi

DADOS="$PWD/saida/_dados-pacote"   # banco + chave em texto: só existe durante o build
PUBLICACAO=saida/_publicacao
EXE=saida/Escalas.exe

limpar() {
  rm -rf "$DADOS" "$PUBLICACAO"
  # O build intermediário também contém o banco e a chave embutidos: não deixa cópia para trás.
  rm -rf src/Escalas.Desktop/obj/Release src/Escalas.Desktop/bin/Release
}
trap limpar EXIT

# Remove pacotes antigos (o zip e a pasta continham a chave em texto).
rm -rf saida/pacote saida/Escalas-pacote.zip "$EXE"
limpar

echo "==> Copiando o banco e a chave..."
dotnet run --project src/Escalas.Admin -- empacotar --saida "$DADOS" ${BANCO:+--banco "$BANCO"}

echo "==> Gerando o executável para Windows (com banco e chave embutidos)..."
dotnet publish src/Escalas.Desktop -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -p:PacoteDados="$DADOS" -o "$PUBLICACAO"

ARQUIVOS=$(ls -A "$PUBLICACAO" | grep -v '\.pdb$' || true)
if [ "$ARQUIVOS" != "Escalas.Desktop.exe" ]; then
  echo "Erro: a publicação gerou arquivos além do .exe:" >&2
  echo "$ARQUIVOS" >&2
  exit 1
fi
mv "$PUBLICACAO/Escalas.Desktop.exe" "$EXE"

echo
echo "Pronto: $EXE"
echo "Leve só esse arquivo para o PC da empresa (por exemplo, na área de trabalho) e abra."
echo "Atenção: o .exe contém o banco e a chave. Não compartilhe nem deixe em pastas públicas."
