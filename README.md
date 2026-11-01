# BetterLoading (experimental)

Carrega mais rápido os mapas de mod (`.3ds`) do Proton Bus Simulator.

**Esta versão ainda não foi testada no jogo.** Se algo der errado, desligue o mod com a opção `Ativo` e mande o log.

O jogo original perde tempo de três jeitos ao carregar um mapa:
- espera um quadro inteiro por arquivo;
- lê cada número do `.3ds` separadamente;
- decodifica e comprime as mesmas texturas toda vez que carrega.

O mod resolve cada um:
- **Quadros:** tira o limite de FPS e o vsync durante a carga, e só desenha a tela de carregamento.
- **Leitura:** lê cada `.3ds` inteiro na memória, com vértices, faces e UVs em bloco.
- **Texturas:** guarda as já processadas em `UserData/BetterLoading/texturas`. Pode apagar a pasta quando quiser.
- **Pré-leitura:** lê os arquivos da pasta em segundo plano, para o Windows já tê-los em cache.

Cada recurso pode ser ligado e desligado na categoria `[BetterLoading]`.

## Relatório

Depois de cada carga, o mod grava `UserData/BetterLoading_relatorio.txt`, com o tempo gasto em texturas, leitura,
resto e espera de quadros. Mande esse arquivo junto com o `Latest.log` ao reportar.
