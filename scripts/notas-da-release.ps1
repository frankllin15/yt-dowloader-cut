# Extrai do CHANGELOG.md a seção de uma versão, para virar as notas da release no GitHub.
# Uso: ./scripts/notas-da-release.ps1 -Versao 1.2.0 [-Saida notas.md]
# Falha (exit 1) se a versão não tiver seção ou estiver vazia: release sem changelog não sai.
param(
    [Parameter(Mandatory)] [string] $Versao,
    [string] $Changelog,
    [string] $Saida
)

if (-not $Changelog) { $Changelog = Join-Path (Split-Path $PSScriptRoot) 'CHANGELOG.md' } # $PSScriptRoot fica vazio no param() do PS 5.1
$linhas = Get-Content $Changelog -Encoding utf8
$titulo = "## [$Versao]"
$ini = [Array]::FindIndex($linhas, [Predicate[string]] { param($l) $l.StartsWith($titulo) })
if ($ini -lt 0) { Write-Error "CHANGELOG.md não tem a seção '$titulo'. Mova as entradas de [Não lançado] para ela antes da tag."; exit 1 }

# Corpo: até a próxima seção de versão ou o bloco de links do rodapé.
$corpo = $linhas[($ini + 1)..($linhas.Count - 1)] | ForEach-Object -Begin { $fim = $false } -Process {
    if ($_ -match '^## \[' -or $_ -match '^\[[^\]]+\]: ') { $fim = $true }
    if (-not $fim) { $_ }
}
$texto = ($corpo -join "`n").Trim()
if (-not $texto) { Write-Error "A seção '$titulo' do CHANGELOG.md está vazia."; exit 1 }

if ($Saida) { Set-Content $Saida $texto -Encoding utf8 } else { $texto }
