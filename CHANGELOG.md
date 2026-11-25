# Changelog

Todas as mudanças relevantes do YtCortador ficam registradas aqui.

O formato segue o [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/)
e o projeto usa [Versionamento Semântico](https://semver.org/lang/pt-BR/).
Cada PR acrescenta sua linha em **[Não lançado]**; na release essa seção vira a versão.

## [Não lançado]

## [1.0.0] - 2026-11-24

Primeira versão publicada.

### Adicionado
- Corte de vídeos do YouTube baixando só os trechos marcados (corte exato em H.264/AAC).
- Preview do vídeo com marcação de início/fim direto pelo player e entrada de tempo só com números (`11606` → `01:16:06`).
- Miniatura 1280x720 no estilo do canal Tom de Louvor, salva junto do corte numa pasta com o nome dele.
- Progresso por corte, cancelamento e aviso ao fechar com downloads em andamento.
- Interface com o visual da marca (verde e ouro, Montserrat).
- Instalador (Inno Setup) que instala yt-dlp e ffmpeg via winget.
- Fluxo de versionamento com tags, CI e release automática com changelog.

[Não lançado]: https://github.com/frankllin15/yt-dowloader-cut/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/frankllin15/yt-dowloader-cut/releases/tag/v1.0.0
