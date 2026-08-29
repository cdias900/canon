# POC — Porta das Ovelhas

| | |
|---|---|
| **Engine** | Unity 6 LTS · 2D URP |
| **Alvo** | Android · iOS · WebGL |
| **Modo** | Single player |
| **Login** | Nenhum |
| **Duração** | ~20 min · 3 dias de jogo |

Especificação de implementação ponta a ponta. Escrita para ser executada por agentes: cada seção define artefatos concretos, esquemas e critérios de aceite.

**Convenção de idioma:** prosa em português; identificadores, chaves de JSON e nomes de arquivo em inglês; strings exibidas ao jogador em pt-BR.

---

## 00 · Objetivo e critério de sucesso

O POC existe para responder uma pergunta: **o jogador abre o capítulo por vontade própria?** Todo o resto é meio. Se ao fim do dia 3 o evento `deep_read` não for disparado sem que o jogo mande, o POC cumpriu seu papel — respondeu não, e barato.

> **Definição de pronto**
> Um APK instala num Android, abre sem cadastro, e uma pessoa que nunca viu o projeto joga os três dias até o fim sem ajuda externa, com a muralha visivelmente construída, uma prova de ânimo vencida ou perdida, e o leitor de capítulo aberto ao menos uma vez.

---

## 01 · Stack e configuração

| Item | Escolha | Motivo |
|---|---|---|
| Engine | Unity 6 LTS (6000.x) | Maior massa de código C# em treino de modelo do que GDScript — decisivo, porque a implementação é feita por agentes. |
| Render | 2D URP | Necessário para `Light2D`: o ciclo dia/noite é requisito de design, e URP entrega de graça. |
| Input | Input System 1.7+ | Unifica mouse e toque num só `InputAction`. Sem código duplicado por plataforma. |
| Tilemap | Unity Tilemap (2D Tilemap Extras) | Nativo, com Rule Tiles para a muralha. |
| Serialização | `Newtonsoft.Json` (`com.unity.nuget.newtonsoft-json`) | `JsonUtility` não lê dicionário nem polimorfismo. Não use. |
| Conteúdo | JSON em `Resources/Data/` | ScriptableObject exige editor para autoria; JSON é editável por agente e por humano fora do Unity. |
| Backend | Nenhum em runtime | Versículos empacotados em build time (§09). Zero rede durante a demo. |
| Resolução | Portrait 1080×1920, PPU 32 | Câmera ortográfica, `orthographicSize = 7.5`. Retrato é o alvo (§04). |

> **REGRA INEGOCIÁVEL, HERDADA DO PROJETO**
> O texto bíblico **nunca** aparece escrito nesta spec, no código ou em prompt de modelo. O que circula é sempre a **referência** (`NEH.4.6`), e o texto literal é resolvido a partir de `verses.json`. Qualquer agente que escrever um versículo à mão introduziu um bug de integridade, não uma conveniência.

---

## 02 · Estrutura do projeto

```
Assets/
  Scenes/
    Boot.unity              // carrega save, roteia para Creation ou Game
    CharacterCreation.unity
    Game.unity              // aldeia + muralha, cena única
  Scripts/
    Core/        GameState.cs  SaveSystem.cs  Telemetry.cs  ServiceLocator.cs
    Player/      PlayerController.cs  GridPathfinder.cs  CharacterAppearance.cs
    World/       DayCycle.cs  WallSystem.cs  ResourceSystem.cs  InteractableBase.cs
    Dialogue/    DialogueSystem.cs  DialogueUI.cs  DialogueData.cs
    Scripture/   ScriptureService.cs  ChapterReaderUI.cs
    Contest/     MoraleContest.cs  ContestUI.cs
    Vocation/    VocationTracker.cs
    Quiz/        DailyQuiz.cs
    UI/          HUD.cs  EndDayPanel.cs  MorningReportUI.cs
  Resources/
    Data/
      verses.json           // gerado por tools/ — NÃO editar à mão
      npcs.json
      dialogue.json
      wall_segments.json
      contest.json
      vocations.json
      quiz.json
  Art/  Sprites/  Tiles/  UI/
tools/
  fetch-verses.mjs          // Node 20+, roda fora do Unity
  verses.manifest.json      // lista de referências
```

---

## 03 · Esquemas de dados

### verses.json — gerado, somente leitura

```json
{
  "version": { "id": "...", "abbrev": "...", "copyright": "..." },
  "verses": {
    "NEH.4.6": { "ref_display": "Neemias 4:6", "text": "<literal>" }
  },
  "chapters": {
    "NEH.4": {
      "ref_display": "Neemias 4",
      "verses": [ { "n": 1, "text": "..." } ]
    }
  }
}
```

### npcs.json

```json
[
  {
    "id": "hananias",
    "display": "Hananias",
    "source_ref": "NEH.3.8",
    "spawn": { "x": 12, "y": 8 },
    "palette": "npc_a"
  }
]
```

### dialogue.json — nó por NPC por dia

```json
{
  "hananias_d1": {
    "npc": "hananias",
    "day": 1,
    "lines": [
      { "text": "Meu pai fazia unguento. Hoje eu carrego pedra." },
      { "verse": "NEH.4.6", "frame": "O governador disse assim:" }
    ],
    "grants": { "vocation": { "pastor": 1 } },
    "reliable": true
  }
}
```

> **Como o versículo entra na fala**
> Uma linha tem `text` **ou** `verse`, nunca os dois. Quando tem `verse`, o `DialogueSystem` resolve via `ScriptureService.GetVerse(ref)` e renderiza em estilo visualmente distinto (itálico, com `ref_display` no rodapé do balão, mesmo corpo dos outros metadados). O campo `frame` é a fala do NPC que introduz a citação — essa sim escrita por nós.

### wall_segments.json

```json
[
  { "id": "seg_01", "grid_x": 20, "stage_cost": [3, 3, 4, 4], "exposed": false }
]
```

4 estágios; custo em unidades de trabalho.

---

## 04 · Cenas e fluxo

**Boot.unity** — Carrega `verses.json` e demais dados, instancia serviços, lê o save. Sem save → `CharacterCreation`. Com save → `Game`. Sem UI além de uma splash.

**CharacterCreation.unity** — Corpo (2 opções) + 3 slots cosméticos × 4 opções cada. Preview ao vivo em 4 direções. Botão único: **Começar**. Sem cadastro, sem e-mail, sem pergunta de religião.

**Game.unity** — Cena única contendo aldeia e muralha lado a lado, sem carregamento entre elas. Câmera segue o jogador com `SmoothDamp` e limites de mapa.

> **Duas câmeras, um mapa**
> A vista padrão é **perto, em retrato**, seguindo o personagem. Um botão de HUD alterna para a **Ronda**: câmera afasta (`orthographicSize` 7.5 → 20), desliza para enquadrar a muralha inteira, e o jogador arrasta na horizontal. É a única vista onde se vê o progresso total, e é diegética — Neemias inspeciona o muro de noite antes de qualquer coisa (`NEH.2.13`).

---

## 05 · Sistemas

| Sistema | Responsabilidade | API principal |
|---|---|---|
| `GameState` | Estado central serializável: dia, recursos, segmentos, ânimo, flags, contadores de vocação. Fonte única de verdade. | `Current`, `Save()`, `Load()` |
| `PlayerController` | Toque/clique no chão → caminho → move. Toque em interagível → aproxima e dispara `Interact()`. | `MoveTo(Vector2)` |
| `GridPathfinder` | A* na grade do tilemap. Mapa pequeno; não use pacote externo. | `FindPath(a,b)` |
| `WallSystem` | Estágios por segmento, consome trabalho, troca sprite, dispara evento de conclusão. | `ApplyWork(id,n)` |
| `ResourceSystem` | Entulho e capacidade de trabalho diária. Capacidade reseta a cada manhã. | `Spend(n) : bool` |
| `DayCycle` | Dia → painel de fim de dia → resolução noturna → relatório matinal. Controla `Light2D` global. | `EndDay(split)` |
| `DialogueSystem` | Fila de linhas, revelação por digitação (40 car/s), resolve `verse` via ScriptureService, aplica `grants`. | `Play(nodeId)` |
| `ScriptureService` | Índice em memória de `verses.json`. Nunca vai à rede. | `GetVerse` / `GetChapter` |
| `ChapterReaderUI` | Painel rolável com o capítulo inteiro. Dispara `deep_read` ao passar 20s **e** 60% de rolagem. | `Open(chapterRef)` |
| `MoraleContest` | Máquina de turnos do dia 3. Ver §07. | `Begin(configId)` |
| `VocationTracker` | Acumula pontos em silêncio. Nunca exibe progresso. Revela no fim do dia 3. | `Add(id,n)` / `Resolve()` |
| `DailyQuiz` | Uma questão por dia. Check-in vale acertando ou errando. | `Show(day)` |
| `Telemetry` | Append-only em JSON local, atrás de `ITelemetrySink`. Ver §10. | `Track(name, props)` |

> **NUNCA MOSTRE A BARRA DE VOCAÇÃO**
> Se o jogador vê que faltam três ações para virar Zelote, a descoberta vira lista de tarefas e o valor evapora. `VocationTracker` não expõe getter público de pontuação para nenhuma UI. Acumula escondido, revela o nome.

---

## 06 · Conteúdo — os três dias

Seis moradores, todos nomeados em Neemias 3 e **sem fala registrada no texto**. Essa é a categoria em que escrever diálogo é legítimo: a Bíblia os nomeia e não os cita. Figura canônica com fala atestada (o governador, o adversário) só reproduz referência.

| id | Nome | Origem | Papel no POC |
|---|---|---|---|
| `hananias` | Hananias | `NEH.3.8` | Filho de perfumista. Dá o tom da dor do povo. Confiável. |
| `salum` | Salum | `NEH.3.12` | Trabalha com as filhas. Ensina a divisão obra/guarda. |
| `baruque` | Baruque | `NEH.3.20` | "Reparou com fervor." Empurra para o trecho exposto. |
| `meremote` | Meremote | `NEH.3.4` | Dia 2: viu cavaleiros na estrada. **Informação correta.** |
| `zacur` | Zacur | `NEH.3.2` | Dia 2: diz que não vem ninguém. **Informação errada.** O jogo não avisa. |
| `malquias` | Malquias | `NEH.3.14` | Governante de distrito. Entrega o convite de fora no dia 2. |

### Dia 1 — A convocação

- Spawn na aldeia. HUD mínima: capacidade de trabalho, entulho.
- Falar com `hananias`, `salum`, `baruque` destrava o trecho.
- Citações do dia: `NEH.2.17`, `NEH.2.18`, `NEH.4.6`.
- Catar entulho (5 pontos no chão) → trabalhar `seg_01`.
- Fim de dia: dividir gente entre **obra** e **guarda**.

### Dia 2 — Os que chamam de fora

- Relatório matinal: o que a noite fez, com e sem guarda.
- `meremote` e `zacur` se contradizem. Nenhum indicador de qual crer.
- `malquias` traz o convite. Aceitar consome o dia inteiro e danifica `seg_01`; recusar cita `NEH.6.3`.
- Peixe no poço: 2 tentativas falham, na 3ª aparece a dica e cita `JHN.21.6`.
- Fim de dia: dividir de novo.

### Dia 3 — A brecha e a leitura

- Manhã curta, depois a investida dispara a prova (§07).
- No turno 2 entra **A Página** e destrava o movimento forte.
- Vitória ou derrota, `seg_01` conclui e grava o nome do jogador.
- Botão **Saber mais** abre `NEH.4` no leitor interno.
- Revelação da vocação, e fim do POC.

---

## 07 · Prova de ânimo

Turnos alternados. **Sem barra de vida e sem contador de mortes:** vence quem faz o outro desistir. O resultado é decidido pelo que o jogador fez nos dias 1 e 2 — é isso que faz a prova parecer merecida em vez de sorteada.

```
player.morale   = 100
enemy.resolve   = 60 + (10 if !watchPostedD2) + (10 if acceptedInvite)
turn limit      = 8   // estouro = recuo do inimigo, empate técnico
```

| Movimento | Efeito | Depende de |
|---|---|---|
| Segurar a linha | −8 resolve, base | +1 por estágio construído em `seg_01` |
| Chamar os outros | +12 morale | ×(nº de NPCs com quem falou / 6) |
| Mostrar a guarda | −20 resolve se guarda foi posta; −4 se não | flag `watchPostedD2` |
| **Metade e metade** *(destrava no t2)* | −15 resolve **e** +8 morale, mesmo turno | Só existe depois de A Página |

> **A Página — o momento que o POC existe para testar**
> No início do turno 2 a prova pausa e um painel desliza mostrando `NEH.4.17`, referência à vista no rodapé. Ao fechar, **Metade e metade** passa a existir no menu, com um brilho de destaque. A revelação de que isto é a Bíblia acontece no mesmo instante em que a Bíblia vira a arma mais forte disponível. Dispara `reveal_shown`.

> **DERROTA NÃO É GAME OVER**
> Se `morale <= 0`, o inimigo recua mesmo assim ao fim do turno, `seg_01` perde **um estágio não concluído** e o dia 3 segue normalmente até a leitura e a vocação. **Dá para perder o amanhã, nunca o ontem:** estágio já concluído jamais regride. Não existe tela de derrota no POC.

---

## 08 · Pontuação de vocação

Seis vocações, acumuladas em silêncio. No fim do dia 3, a de maior pontuação é revelada; empate resolve pela ordem da tabela.

| Vocação | Ações que pontuam | Pts |
|---|---|---|
| `zelote` | Trabalhar o segmento exposto · recusar o convite de forma direta · abrir a prova com Segurar a linha | +2 cada |
| `escriba` | Abrir o leitor de capítulo · falar com os 6 NPCs · reler um diálogo | +2 cada |
| `pastor` | Usar Chamar os outros · falar com Hananias e Salum nos dois dias · doar entulho a um NPC | +2 cada |
| `exilado` | Usar a Ronda 3+ vezes · pegar o peixe · caminhar até a borda do mapa | +2 cada |
| `profeta` | Acreditar em Meremote e não em Zacur (postar guarda no dia 2) · fechar A Página sem pular | +3 cada |
| `mordomo` | Terminar dia 1 e 2 com capacidade de trabalho toda gasta · zero entulho desperdiçado | +3 cada |

---

## 09 · Pipeline de versículos

`tools/fetch-verses.mjs` roda fora do Unity, lê `verses.manifest.json`, busca na API do YouVersion e grava `Assets/Resources/Data/verses.json`. Rodar uma vez, versionar a saída.

```json
{
  "version_id": "<definir na decisão de tradução>",
  "verses": [
    "NEH.2.17", "NEH.2.18", "NEH.3.1", "NEH.4.2", "NEH.4.6",
    "NEH.4.9", "NEH.4.17", "NEH.6.3", "JHN.21.6", "1CO.13.13"
  ],
  "chapters": ["NEH.4"]
}
```

> **Validador determinístico — camada 1**
> O script falha a build se: uma referência do manifesto não existir na resposta; um `text` voltar vazio; ou qualquer `frame` de `dialogue.json` contiver uma sequência de 8+ palavras que também apareça em `verses.json` (paráfrase acidental). Isso porta a camada determinística do Cânon para o POC, e é ~40 linhas.

> **BLOQUEIO CONHECIDO**
> `version_id` depende da decisão de tradução e das três perguntas de licença ainda em aberto (armazenar, buscar em lote, versões habilitadas). Enquanto não sair, use uma tradução de domínio público para destravar a implementação e troque o id depois — o resto do pipeline não muda.

---

## 10 · Telemetria

Append-only em `Application.persistentDataPath/telemetry.jsonl`, uma linha JSON por evento, atrás de `ITelemetrySink` para que um backend real entre depois sem tocar nas chamadas.

| Evento | Props | Por quê |
|---|---|---|
| `session_start` | `day` | Base de retenção. |
| `verse_shown` | `ref`, `context` | Exposição ao texto. |
| `chapter_opened` | `ref`, `trigger` | `trigger` distingue "saber mais" de prompt do jogo. |
| `deep_read` | `ref`, `seconds`, `scroll_pct` | **A métrica-norte.** 20s e 60% de rolagem. |
| `reveal_shown` | `turn` | O momento da ficha caindo. |
| `node_completed` | `segment`, `day` | Progresso de obra. |
| `vocation_revealed` | `vocation`, `scores` | Distribuição real de comportamento. |

---

## 11 · Assets necessários

Nada de arte própria. Tileset CC0 (Kenney ou equivalente) com paleta reduzida a três cores por ajuste de material. Pixel art 32×32; personagem 32×48.

**Personagem — em camadas**

- 2 corpos base × 4 direções × 3 animações (idle, walk, work)
- 4 tops, 4 calças, 4 acessórios — mesmas dimensões, sobrepostos
- 4 `SpriteRenderer` ordenados no mesmo `GameObject`, um `Animator` só dirigindo todos
- NPCs reusam o corpo base com troca de paleta via `MaterialPropertyBlock`

**Cenário e UI**

- Tiles: chão, escombro, água, casa, e 4 estágios de muralha
- Props: 5 pilhas de entulho, 1 poço com peixe
- UI: balão de diálogo, painel de fim de dia, HUD, painel de prova, A Página, leitor de capítulo
- Áudio: 1 ambiente diurno, 1 noturno, 4 efeitos (passo, pedra, confirma, trombeta)

---

## 12 · Fora do escopo

- Multiplayer, expedições, assentos — **o schema já prevê, o POC não implementa**
- Aldeia como base separada com construções próprias
- Loja, talentos gastáveis, dracmas, qualquer compra
- Figueira diária, streak, notificação push
- Cadastro, conta, nuvem, sincronização
- Chamada de LLM em runtime — todo diálogo é autorado em `dialogue.json`
- As outras 9 portas, as outras 3 ameaças, qualquer outra temporada
- Jesus ou Espírito Santo como guia — **adiado por decisão, não descartado**

---

## 13 · Critérios de aceite

| # | Verificação |
|---|---|
| 01 | Build Android instala e abre em menos de 5s, sem tela de login. |
| 02 | Criação de personagem produz aparência distinta e persistida entre sessões. |
| 03 | Toque no chão move; toque em NPC aproxima e abre diálogo. |
| 04 | Todo versículo exibido veio de `verses.json`; nenhum literal em C#. Verificável por grep. |
| 05 | `seg_01` muda de sprite nos 4 estágios e o progresso sobrevive a fechar o app. |
| 06 | Fim de dia 1 sem guarda produz relatório matinal diferente do dia com guarda. |
| 07 | Recusar o convite exibe `NEH.6.3`; aceitar consome o dia e danifica o segmento. |
| 08 | A Página aparece no turno 2 e destrava **Metade e metade**. |
| 09 | Perder a prova **não** mostra game over e não regride estágio concluído. |
| 10 | `deep_read` aparece em `telemetry.jsonl` após leitura real de `NEH.4`. |
| 11 | Vocação revelada corresponde à maior pontuação; nenhuma UI expôs progresso antes. |
| 12 | Roda offline do início ao fim, em modo avião. |
