# Daily check-in — design

## O que é

Uma recompensa dada uma vez por dia real (calendário, não dia da história) quando o jogador
abre o app: talentos, a moeda cosmética do jogo. A recompensa cresce com sequência de dias
consecutivos e a sequência reinicia se o jogador pular um dia.

## Por que existe

Motor de retorno diário simples, independente do enredo de três dias do POC. Não mede
`deep_read` diretamente e não é o produto — é engajamento de baixo custo que não compete com
as regras do jogo.

## Decisão sobre a regra 7

A regra 7 (`CLAUDE.md`) diz: "dá para perder o amanhã, nunca o ontem — ausência atrasa, nunca
retrocede; progresso concluído jamais regride."

O check-in reinicia o **contador de sequência** quando o jogador falta um dia, o que muda o
patamar de recompensa do próximo login de volta para o tier 1. Os talentos já pagos nunca são
removidos — nada que o jogador já possui regride.

Isso é uma leitura deliberada da regra, feita por João (autor do documento original, git user
deste repositório) em conversa: perder acesso a um patamar de recompensa ainda não conquistado
não é "regredir progresso concluído", é não conceder algo que o jogador não fez por merecer
ainda. Registrado aqui porque a leitura literal da regra 7 é mais estrita do que isso, e um
leitor futuro precisa saber que a divergência foi intencional, não um descuido.

## Gatilho

Dispara uma vez por dia de calendário real, no boot do app — logo depois que o save carrega em
`BootSequence`, no mesmo ponto em que já loga `[Boot] Save ->`. Nada de polling, nada de timer
em background.

Comparação de data usa o dia local do dispositivo, formato ISO `yyyy-MM-dd`.

## Modelo de dados

Três campos novos em `GameState` (`Assets/Scripts/Core/GameState.cs`), ao lado do padrão já
existente em `counters`:

```csharp
public string lastCheckInDate;   // ISO "yyyy-MM-dd", data local do dispositivo
public int checkInStreak;        // dias de calendário consecutivos com check-in
public int talents;              // moeda cosmética
```

No boot, comparando a data de hoje com `lastCheckInDate`:

- Mesma data → não faz nada, já houve check-in hoje.
- Exatamente um dia de calendário depois → `checkInStreak++`.
- Qualquer intervalo maior (ou primeira execução) → `checkInStreak = 1`.

## Recompensa

Determinada por `checkInStreak` depois de atualizado:

| Sequência | Recompensa |
|---|---|
| 1–3 | 1 talento |
| 4+ | 3 talentos |

Sem teto e sem expiração nos talentos já acumulados — só o contador de sequência reinicia.

## Interface

Painel modal reutilizando o mecanismo do `DailyQuiz` (`ModalRoot.Push`), mostrado uma vez
depois que o boot assenta — reaproveita o padrão "espera uma tela quieta" de
`DailyQuiz.IsScreenQuiet`, para nunca cair em cima da abertura do dia 1. Conteúdo: "+1 talento"
ou "+3 talentos", botão de fechar, sem celebração — é um recibo, não uma tela de caça-níquel
(checklist do cheiro, regra 13, proíbe luz dourada e qualquer coisa que pareça prêmio de
slot machine).

Talentos ganham um contador na tira de materiais do `BackpackPanel`
(`Assets/Scripts/UI/BackpackPanel.cs`, `BuildMaterials`), quarta célula ao lado de
pedra/madeira/blocos — o lugar onde o jogador já confere o que tem.

## Persistência

Escrito via `SaveSystem.Save(state)`, o mesmo caminho que `DailyQuiz` usa, imediatamente quando
a recompensa é calculada — para que fechar o app no meio do toast não repita a recompensa.

## Telemetria

Novo evento `check_in`, com `streak` e `talents_awarded`, seguindo o formato de eventos
existentes como `watch_posted_d1`.

## Fora de escopo

- Notificação push lembrando o jogador de voltar — decisão de produto e permissão de SO maior,
  não faz parte deste corte.
- Tela de gasto de talentos — não há economia de gasto definida ainda; este desenho só cunha e
  mostra o saldo.

## Teste

**Manual**: o gatilho depende de data real de calendário, então reiniciar o app sozinho não
reproduz nada. Editar `lastCheckInDate` diretamente no arquivo de save antes de relançar é o
caminho:

- Save: `~/Library/Application Support/Create Hack` (Play mode no editor) ou
  `~/Library/Application Support/com.Create-Hack.Porta-das-Ovelhas` (player buildado).
- Voltar `lastCheckInDate` um dia → dispara o próximo tier da sequência.
- Voltar mais de um dia → dispara o reinício da sequência.
- `tools/ios-sim.sh reset` apaga o save inteiro para zerar a sequência do zero.

**Automatizado** (parte da implementação, não deste desenho): um critério em
`AcceptanceHarness`/`tools/acceptance.sh` que constrói um `GameState` direto, sem UI, ajusta
`lastCheckInDate` (ontem, uma semana atrás, hoje) e confere que `checkInStreak`/`talents` batem
com a tabela acima — essa é a camada que testa a matemática de datas sem depender de dias reais
passarem. Mais uma passagem em `tools/e2e.sh` confirmando que o toast aparece e fecha num boot
limpo, nos dois idiomas.
