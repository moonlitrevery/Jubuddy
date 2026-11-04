# Jubuddy

Um quarto 3D visto de cima, em ângulo, com um bonequinho seu morando nele. Ele anda, varre, lê,
fica no PC, senta e dorme. O quarto fica no fundo da área de trabalho, atrás dos ícones, e para de
desenhar sozinho quando você está jogando.

O quarto começa **vazio**. Você vai adicionando objetos que dizem quem você é, cada um com uma frase
sua sobre por que ele está ali.

Projeto pessoal e privado. Tudo é local: sem nuvem, sem conta, sem telemetria, sem rede.

---

## Gerar o executável (Windows)

### O que instalar (uma vez)

1. **Godot 4.5.1 .NET**: <https://godotengine.org/download/archive/4.5.1-stable/>, versão
   *.NET* para Windows x86_64. É um zip; não precisa instalar, só extrair.
2. **.NET SDK 8**: <https://dotnet.microsoft.com/download/dotnet/8.0> (o instalador "SDK x64").
3. **Modelos de exportação**: abra o Godot, abra este projeto (`project.godot`), vá em
   *Editor → Gerenciar Modelos de Exportação → Baixar e Instalar*.

### Pelo editor

1. Abra `project.godot` no Godot .NET.
2. *Projeto → Exportar… → Windows Desktop → Exportar Projeto…*, desmarque "Exportar com Depuração"
   e salve como `build/Jubuddy.exe`.

### Pela linha de comando

```powershell
cd L:\Projetos\Jubuddy
& "C:\caminho\Godot_v4.5.1-stable_mono_win64_console.exe" --headless --path . --export-release "Windows Desktop" build/Jubuddy.exe
```

O resultado é `build/Jubuddy.exe` mais a pasta `build/data_Jubuddy_windows_x86_64/`, onde fica o
.NET embutido. **As duas precisam ficar juntas.** Pode mover a pasta `build` inteira para onde
quiser (por exemplo `C:\Programas\Jubuddy`). Não precisa instalar nada no PC que vai rodar.

---

## Testar no seu Windows

Roteiro de uns 10 minutos, na ordem:

1. **Papel de parede.** Rode `build\Jubuddy.exe` e aperte **Win+D** para mostrar a área de trabalho.
   O quarto deve aparecer atrás dos ícones. Os ícones continuam clicáveis e o Jubuddy não aparece
   na barra de tarefas. *Se não aparecer nada*: Ajustes → marque "Papel de parede alternativo"
   (ou rode com `-- --forcar-alternativo`) e me conte qual versão do Windows.
2. **Bandeja.** Ache o ícone perto do relógio. Clique com o botão direito e veja o menu: abrir o
   quarto, frases, pasta de dados, diagnóstico, iniciar com o Windows, sair.
3. **Modo quarto.** "Abrir o quarto": vira uma janela normal e o buddy acena.
   - *＋ Adicionar* → escolha um móvel → escreva a frase → mova o mouse e clique para soltar
     (**R** gira, botão direito cancela).
   - Clique num objeto: aparece a frase dele, com Mover, Girar, Editar frase, Cor e Guardar no baú.
   - *Pôster*: escolha uma imagem sua; ela é copiada para a pasta do app.
   - *Baú*: o que você guardou continua lá, com a frase. "Devolver ao quarto" ou "Apagar de vez…"
     (pede confirmação).
   - *Aparência*: pele, cabelo e penteado, olhos, roupa, cores, óculos, tamanho da cabeça e altura.
   - Fechar a janela (X) volta para o papel de parede.
4. **Comportamento.** Com mesa com PC, ele usa o PC (senta se houver cadeira na frente). Com
   estante, ele pega um livro e vai ler sentado. Varrer acontece sempre. De madrugada (0h–6h)
   ele dorme, na cama se houver.
5. **Frases.** Ajustes → *Editar* abre `frases.txt`. Escreva uma por linha, salve, e use
   *Mostrar uma* para testar. Por padrão aparece uma a cada ~45 min (0 desliga).
6. **Jogo.** Ligue o *Modo de diagnóstico* pela bandeja e abra um jogo. A dica do ícone da bandeja
   deve mostrar "pausado (tela cheia)". Ao voltar, o buddy está fazendo outra coisa, como se o
   tempo tivesse passado. Depois veja `diagnostico.csv` na pasta de dados.
7. **Iniciar com o Windows.** Pela bandeja. Só funciona no executável exportado, não rodando pelo editor.

---

## Seus dados

Tudo fica em `%APPDATA%\Jubuddy\` (no Linux, `~/.local/share/Jubuddy/`):

| Arquivo | O que é |
|---|---|
| `quarto.json` | objetos no quarto e no baú, com as frases (`.bak` é a versão anterior) |
| `buddy.json` | aparência do buddy (dá para editar à mão) |
| `ajustes.json` | frequência das frases, quadros por segundo, posição e monitor do papel de parede, cores do quarto |
| `frases.txt` | suas frases, uma por linha (`#` comenta) |
| `posters/` | cópias das imagens dos pôsteres (reduzidas a no máximo 1024 px) |
| `diagnostico.csv` | só existe se o modo de diagnóstico estiver ligado |

Salvar é atômico: escreve num arquivo temporário e troca, mantendo o anterior em `.bak`. Se um
arquivo estiver corrompido, ele **não é apagado**: vira `*.corrompido-DATA` e o app usa o `.bak`.

O app não vem com nenhuma frase pronta. Não tem sequência de dias, cobrança nem nota.

---

## Linha de comando

Depois de `--`, por exemplo `Jubuddy.exe -- --quarto`:

| Opção | Para quê |
|---|---|
| `--quarto` | abre direto no modo quarto |
| `--diagnostico` | liga o diagnóstico só nesta execução |
| `--forcar-alternativo` | usa a janela transparente em vez do WorkerW |
| `--dados=PASTA` | usa outra pasta de dados (para testar sem mexer no seu quarto) |
| `--forcar-atividade=X` | `andar`, `varrer`, `ler`, `pc`, `sentar`, `dormir`, `atoa` (para medir) |
| `--forcar-pausa` / `--nunca-pausar` | para medir o custo pausado / desenhando |
| `--sair-apos=SEG`, `--captura=ARQ.png`, `--demo-painel=X` | testes automáticos |

---

## Desenvolvimento

```
src/Core/       C# puro, sem Godot: quarto, baú, máquina de estados, navegação, frases, salvar/carregar
src/Platform/   tudo que depende do sistema; Windows/ isolado (user32, shell32, registro)
src/App/        Godot: cena, buddy (corpo + animações procedurais), móveis, interface, bandeja
tests/          testes do núcleo (xUnit)
tools/          medir-desempenho.ps1
docs/           desempenho.md e medições
```

- **Testes**: `dotnet test tests/Jubuddy.Tests`. São 48: máquina de estados, baú, salvar/carregar,
  navegação e frases. Rodam no Windows e no Linux.
- **Rodar pelo editor**: abra o projeto no Godot .NET e aperte F5. Pelo editor, "iniciar com o
  Windows" fica desligado.
- **No Linux**: tudo compila e roda. `DesktopPlatform.Create()` escolhe `NullDesktopPlatform`, então
  o "papel de parede" vira uma janela sem borda comum, sem detecção de tela cheia e sem iniciar com
  o sistema. O código do Windows só é chamado atrás de `OperatingSystem.IsWindows()`.

Detalhes de desempenho e como medir: [docs/desempenho.md](docs/desempenho.md).

---

## Limitações conhecidas

- **Papel de parede no Windows 11 24H2+**: o modo embutido entra como filho do Progman, abaixo dos
  ícones, com a janela em estilo *layered* (que é o que o Lively faz). A estrutura foi conferida
  nesta máquina (build 26200), mas a confirmação visual ficou pendente: as duas telas estavam
  cobertas por janelas durante o teste. Ver o passo 1 do roteiro.
- **Se o Explorer reiniciar**, a janela-mãe do papel de parede some junto e o Jubuddy pode fechar.
  É só abrir de novo.
- Um monitor por vez (escolhido em Ajustes, quando houver mais de um).
- Pausa com qualquer app em tela cheia, inclusive vídeo em tela cheia. É de propósito.
