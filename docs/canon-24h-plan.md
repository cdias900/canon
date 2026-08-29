# Cânon em 24h — plano de corte do hackathon

> Create Hack 2026 · 5 pessoas · plano de corte
> **Status: histórico.** Escrito para a entrega do hackathon. Mantido porque as decisões de produto e os guardrails continuam valendo para os dois projetos.

Os documentos originais descrevem seis fases. O time tinha um dia. Este plano corta tudo que não prova a tese e protege as três coisas que provam: **o texto é loot**, **a IA é real**, **o versículo nunca é inventado**.

## Três decisões que ninguém reabre

### 01 · Tradução — YouVersion
O maior bloqueio do projeto caiu, e ainda dá acesso às traduções que a faixa etária de fato lê. Antes de escrever uma linha, confirmar nos termos: **dá para armazenar o texto? dá para puxar em lote? quais versões a chave habilita?** As duas primeiras decidem a arquitetura — ver o corpus duplo abaixo.

### 02 · Plataforma — web mobile-first
No framework que os dois de front já dominam. Contraria o anexo técnico de propósito: Flutter continua certo para a Fase 1, não para 24h. O jurado abre um link no próprio celular sem instalar nada, e ninguém aprende stack nova na véspera.

### 03 · O caminho da demo — cinco paradas congeladas
Não são 30 nós. São cinco, escolhidos porque cada um prova uma coisa diferente para a banca.

## O que entra e o que morre

| Entra | Não entra |
|---|---|
| Abertura fria dentro de uma cena — sem cadastro | Co-op, expedições, assentos, raid (Fase 4) |
| Uma geração ao vivo: o jogador toca *escola* ou *trabalho* | Nó de Encontro com chat aberto (streaming quebra em palco) |
| Nó de Investigação com busca semântica real em pgvector | TTS, notificações, push, cache offline |
| O nó 27 — a tela sem escolha | Login, contas, LGPD de menor |
| Queda da Âncora, leitura do capítulo, upgrade | Trilha adaptativa e Companheiro com memória |
| Validador de duas camadas, com tela mostrando ele pegando um versículo inventado | Os outros 25 nós, as outras 2 regiões |
| Mapa em brasas + som e haptic | Publicação em loja, build nativo, Flutter |
| Contador de aprofundamento ao vivo | |

Guardar um `expedition_id` nulo nas tabelas de estado: cinco minutos, não muda nada hoje, e sustenta a resposta sobre multiplayer.

## As cinco brasas

Entre uma parada e outra, um card de recapitulação de quinze segundos avança o tempo.

| Parada | O que é | Prova |
|---|---|---|
| **Nó 1 · Cena** — A oferta | Abre dentro da história. Uma escolha antes de existir trilha. Gerada ao vivo a partir do contexto que o jurado tocou. | não tem cheiro |
| **Nó 13 · Investigação** — Antes de responder | O jurado digita o dilema com as palavras dele. A busca devolve a passagem. | a IA é ferramenta |
| **Nó 27 · Cena** — O olhar | Nenhuma opção de fala. Nenhum botão além de sair da tela. | é jogo, e dói |
| **O loop** — A Âncora cai | Brilha, encaixa no slot, e diz que sobe de nível se ele ler Lucas 22 inteiro. O contador sobe na tela. | a métrica é a economia |
| **Nó 30 · Chefe** — A outra fogueira | Três perguntas, sem cobrança de explicação. | a virada é desenhada |
| *Corte se atrasar:* Treino de 60s | Mostra a trilha do conhecimento ao lado da do caráter. | decide em T-10 |

## Quem faz o quê

| Frente | Entrega | Não faz |
|---|---|---|
| **Backend** *(caminho crítico)* | Corpus duplo: embeddings sobre tradução de domínio público, exibição pelo YouVersion. O índice só devolve **referência** — funciona mesmo que os termos proíbam armazenar. Depois: endpoint de geração, `events` com `deep_read`, `cost_cents`. | Nenhuma tela. Desbloqueiem esta pessoa primeiro, sempre. |
| **Frontend A** | O motor de cena: texto revelado com timing, cartas, escolhas, atributos. E o leitor de capítulo, onde a métrica acontece. | Mapa, animação, som. |
| **Frontend B** | Mapa em brasas, queda da Âncora com peso, transições, som e haptic, contador de aprofundamento. | Lógica de nó, integração. |
| **Cibersegurança** | 3h: chave só no servidor, RLS, rate limit. Depois: **o validador de duas camadas**. Entrega própria: uma tela de 20s mostrando o validador barrando um versículo alucinado. | — |
| **Product design** | 90 min: silhueta, duas cores, tipografia, tokens. Depois: cinco telas e o card de Âncora. Por fim, o roteiro da demo. | — |

**O buraco do time:** não há escritor nem teólogo, e o pipeline depende de esqueletos humanos. `Canon_regiao1_fogueira_mapa.docx` já traz premissa e âncora dos 30 nós. Adaptem cinco, ao pé da letra. **Não inventem conteúdo novo** — é a única parte que não dá para improvisar sem quebrar a promessa de ortodoxia.

## O relógio

| T-menos | O quê |
|---|---|
| **T-24** | As três decisões. Trinta minutos, todo mundo de pé, sem laptop. |
| **T-23** | Fundação em paralelo nas cinco frentes. |
| **T-19** | **Um nó ponta a ponta** — gerado, validado e jogável no celular. Enquanto não acontece, ninguém começa o segundo nó. |
| **T-14** | O caminho inteiro + rodízio de sono (dois de cada vez, quatro horas). |
| **T-8** | Pré-gera tudo e congela o conteúdo. Pré-aquecer as chamadas externas. |
| **T-6** | Última janela de código: game feel. |
| **T-4** | **FREEZE.** Nenhum commit novo de feature. |
| **T-4** | Ensaio e vídeo reserva. Cinco passadas cronometradas; vídeo gravado até T-3. |
| **T-2** | Folga e submissão. |

## Riscos e antídotos

| Risco | Antídoto |
|---|---|
| Geração ao vivo trava no palco | Uma só geração ao vivo, timeout de 8s, queda para cache. Ensaiar com a rede desligada. |
| Backend vira gargalo às 3h | Contrato de API escrito e mock no front desde T-23. |
| Sobra tempo e alguém "melhora" algo | Freeze em T-4 é regra do time. Tempo sobrando vira ensaio. |
| O leitor de capítulo sai para o YouVersion | Fora do app não se observa `deep_read`. O leitor é interno; "abrir no YouVersion" é botão secundário. |
| A demo mostra features e não a tese | Se os três minutos não terminarem com alguém saindo do jogo para ler o capítulo, a demo está errada. |

> "A métrica de sucesso do nosso jogo é quantas vezes o jogador sai do jogo para ler a Bíblia."
> — `Canon_resumo_para_o_time`, a frase para a banca
