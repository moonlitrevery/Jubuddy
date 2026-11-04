# Desempenho medido

A regra: **o Jubuddy roda enquanto você joga, então não pode pesar.**
Metas: CPU e GPU perto de zero com o buddy parado, e menos de 150 MB de memória.

## Resultado (3 nov 2026)

Executável exportado em modo release (`build/Jubuddy.exe`), papel de parede embutido no
Progman (Windows 11 24H2+), monitor principal de 1920×1080. **O Roblox estava aberto e
maximizado durante todas as medições**: é o cenário real, com o jogo disputando a GPU.

| Cenário | CPU | GPU (3D) | Memória privada | Working set | Quadros/s |
|---|---:|---:|---:|---:|---:|
| Andando (passeando) | 0,24% | 1,9% | 100 MB | 191 MB | 30 |
| Varrendo | 0,39% | 2,8% | 100 MB | 191 MB | 30 |
| Parado: no PC | 0,04% | 0,8% | 99 MB | 190 MB | 8 |
| Parado: sentado ¹ | 0,93% | 1,0% | 98 MB | 190 MB | 8 (30 andando) |
| Dormindo | 0,09% | 0,3% | 100 MB | 191 MB | 3 |
| **Pausado: jogo aberto (detecção real)** | **0,07%** | **0%** | **100 MB** | 191 MB | 0 |
| Pausado: forçado | 0,16% | 0% | 98 MB | 190 MB | 0 |
| Plano B (janela transparente), andando | 0,31% | 1,7% | 100 MB | 191 MB | 30 |
| Modo quarto (janela 1280×800, 60 qps) | 0,89% | 1,4% | 122 MB | 214 MB | 60 |

**Meta de memória: cumprida** na medida que o Gerenciador de Tarefas mostra (memória privada,
98–122 MB). O *working set* total (~190 MB) é maior porque inclui páginas compartilhadas e
somente leitura: o próprio executável de 97 MB mapeado na memória e as DLLs do driver de vídeo,
que não são do Jubuddy e o Windows descarta quando falta memória.

¹ "Sentado" forçado escolhe um assento novo a cada 30–90 s e anda até ele, então a janela de
30 s pegou trechos andando a 30 qps. Sentado e quieto, o custo é igual ao de "no PC".

Dados brutos: [`medicoes/2026-11-03-gtx1650s-1080p.csv`](medicoes/2026-11-03-gtx1650s-1080p.csv).

### Máquina

- Windows 11 Pro, build 26200 (25H2)
- Intel Core i5-12400F (12 threads), 32 GB de RAM
- NVIDIA GeForce GTX 1650 SUPER, driver 617.14, OpenGL 3.3 (renderizador Compatibility do Godot)
- Dois monitores 1920×1080; o quarto estava no principal

## Como foi medido

Script: [`tools/medir-desempenho.ps1`](../tools/medir-desempenho.ps1). Para cada cenário ele abre o
executável com uma pasta de dados separada, espera 8 s de aquecimento e mede por 30 s:

- **CPU**: tempo de processador do processo ÷ tempo de relógio ÷ número de threads da CPU
  (a mesma conta do Gerenciador de Tarefas).
- **GPU**: soma da utilização dos motores 3D do processo, pelos contadores `GPU Engine` do
  Windows, lidos crus via WMI. Assim funciona com o Windows em qualquer idioma.
- **Memória privada**: `WorkingSetPrivate`, a coluna "Memória" do Gerenciador de Tarefas,
  amostrada a cada segundo (média e máximo).

Os cenários são forçados por linha de comando, para serem repetíveis:
`--forcar-atividade=andar|varrer|pc|sentar|dormir`, `--forcar-pausa`, `--forcar-alternativo`, `--quarto`.
Para medir o custo de desenhar com o jogo aberto, as linhas ativas usam `--nunca-pausar`. Sem isso,
o Jubuddy pausa sozinho, que é o caso "pausado: jogo aberto (real)".

Para repetir:

```powershell
.\tools\medir-desempenho.ps1 -Cenario "andando" -AppArgs "--nunca-pausar","--forcar-atividade=andar"
.\tools\medir-desempenho.ps1 -Cenario "pausado" -AppArgs "--forcar-pausa"
```

## O que faz ele ser leve

- **Para de desenhar de verdade** (`RenderingServer.RenderLoopEnabled = false`) quando: um app está em
  tela cheia (`SHQueryUserNotificationState` ou janela em primeiro plano cobrindo o monitor, o caso dos
  jogos em "tela cheia sem borda"); uma janela maximizada cobre o monitor do quarto; o PC está
  bloqueado; ou a proteção de tela está ativa. Pausado, o laço do Godot cai para 2 voltas por segundo.
- **Checagens no máximo 1×/s**, todas chamadas baratas da API do Windows, sem polling pesado.
- **Quadros por atividade**: 30/s andando ou varrendo, 8/s parado, 3/s dormindo. Configurável em `ajustes.json`.
- `low_processor_mode`, vsync desligado, áudio desligado (driver `Dummy`), física a 10 Hz, 2 threads de trabalho.
- Renderizador **Compatibility (OpenGL 3.3)**: uma luz direcional **sem sombra**, sombra de "bolha"
  falsa sob o buddy, iluminação por vértice, **nada de pós-processamento**.
- Malhas e materiais compartilhados (cada forma e cada cor existe uma vez só).
- Pôsteres reduzidos a no máximo 1024 px ao importar.
- Processo em prioridade **abaixo do normal**: se o jogo precisar, ele cede.
- Ao voltar de uma pausa, o buddy **avança pelo relógio** (`BuddyBrain.FastForward`): encadeia as
  atividades que teriam acontecido e já aparece fazendo outra coisa, sem ter desenhado nada.

## Coisas testadas e descartadas

| Tentativa | Memória privada (modo quarto) | Decisão |
|---|---:|---|
| OpenGL nativo (padrão) | 119 MB | mantido |
| ANGLE (Direct3D 11) | 151 MB | pior |
| Coletor de lixo .NET não concorrente | 125 MB | sem ganho (ruído) |
| Driver de texto `Fallback` em vez do ICU | 126 MB | sem ganho |

Uma vez, numa medição "dormindo", apareceram 156–159 MB. Não se repetiu em cinco medições
seguintes (todas com 99–101 MB, a 3, 4 e 8 qps). O mais provável é uma alocação passageira do
driver de vídeo enquanto o jogo rodava. Fica registrado; o modo de diagnóstico grava a memória
a cada 5 s em `diagnostico.csv`, então dá para conferir se acontecer de novo.

## Conferir no dia a dia

Ligue o **modo de diagnóstico** (menu da bandeja ou Ajustes). No canto aparecem CPU, quadros por
segundo, memória e se está pausado (e por quê). A dica da bandeja também mostra o estado, o que
ajuda com o jogo em tela cheia. A cada 5 s ele grava uma linha em `diagnostico.csv` na pasta de
dados, para olhar depois de jogar.
