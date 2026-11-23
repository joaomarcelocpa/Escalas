#!/usr/bin/env bash
# Gera o pacote para o PC da empresa: saida/Escalas-pacote.zip
# Dentro: Escalas.Desktop.exe (Windows x64) + escalas.db (dados atuais) + escalas.chave.
# No PC da empresa, basta descompactar e abrir o .exe: o app importa banco e chave sozinho no primeiro uso.
#
# Uso: ./empacotar.sh [caminho/do/escalas.db]
#   Sem argumento: usa $ESCALAS_DB; se não houver, saida/fabianamoveis/escalas.db.
set -euo pipefail
cd "$(dirname "$0")"

BANCO="${1:-${ESCALAS_DB:-}}"
if [ -z "$BANCO" ] && [ -f saida/fabianamoveis/escalas.db ]; then BANCO=saida/fabianamoveis/escalas.db; fi

PASTA=saida/pacote/Escalas
ZIP=saida/Escalas-pacote.zip
rm -rf saida/pacote "$ZIP"

echo "==> Gerando o executável para Windows..."
dotnet publish src/Escalas.Desktop -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -o "$PASTA"
rm -f "$PASTA"/*.pdb

echo "==> Copiando o banco e a chave..."
dotnet run --project src/Escalas.Admin -- empacotar --saida "$PASTA" ${BANCO:+--banco "$BANCO"}

(cd saida/pacote && zip -rq ../Escalas-pacote.zip Escalas)
echo
echo "Pronto: $ZIP"
echo "Leve para o PC da empresa, descompacte e abra Escalas.Desktop.exe."
echo "Atenção: o zip contém a chave do banco. Não compartilhe; apague depois de instalar."
