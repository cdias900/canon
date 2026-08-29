# Create Hack 2026 — contexto de produto

Repositório de um jogo de engajamento bíblico. Este arquivo é o contexto que todo agente lê antes de qualquer coisa. Os detalhes estão em `docs/`; **as regras abaixo não estão lá — estão aqui porque não podem ser violadas.**

## O que estamos construindo

Dois conceitos irmãos, com a mesma tese e mecânicas diferentes.

**Cânon** — RPG narrativo em texto (chat fiction) com trilha de progressão. O jogador vive dilemas da vida real dele e descobre, no fim de cada capítulo, que alguém já viveu aquilo. Roupagem moderna, discrição total, o texto bíblico entra como *loot* equipável ("Âncoras"). Ver `docs/canon-24h-plan.md`.

**Cinquenta e Dois Dias** — jogo de construção e defesa por turnos, ambientado no livro de Neemias. O jogador é um dos convocados que reconstroem a muralha de Jerusalém. O texto bíblico entra como *guia de estratégia*. Ver `docs/nehemiah-game-design.md`.

**Em desenvolvimento agora:** o POC do segundo — três dias de jogo, uma porta, Unity. Ver `POC-IMPLEMENTATION.md`.

Os dois compartilham a métrica, os guardrails, o sistema de vocação e o pipeline de versículo. Não compartilham engine.

## A métrica-norte

**Taxa de aprofundamento: o jogador sai do jogo, por vontade própria, para ler o capítulo inteiro.**

O evento é `deep_read`. Toda decisão de produto se resolve perguntando se ela aumenta ou diminui esse número. Se uma feature bonita não move isso, ela não é prioridade. Se uma feature move isso, ela é o produto.

Corolário prático: **nunca mande a leitura para fora do app.** Deep link para o YouVersion, envio por e-mail, abrir o navegador — tudo isso destrói a medição, que é a razão de ser do produto. Leitor interno sempre; canal externo é botão secundário, opcional.

## Regras inegociáveis

### Integridade do texto

1. **O LLM nunca escreve Escritura.** O modelo escolhe a *referência*; o texto literal é buscado por essa referência. Elimina alucinação de versículo por construção, não por prompt.
2. **Nem código, nem spec, nem prompt contém versículo escrito à mão.** O que circula é `NEH.4.6`. Verificável por grep, e é critério de aceite.
3. **Deus nunca fala em texto gerado.** Nada de colocar palavras na boca de Deus, de Jesus ou do Espírito Santo.
4. **Figura canônica só diz o que está atestado.** Personagem nomeado no texto mas *sem fala registrada* (os 40+ construtores de Neemias 3) pode receber diálogo autoral — a Bíblia os nomeia e não os cita. Personagem inventado fala à vontade.
5. **Sem viés denominacional.** Onde há divergência de leitura, mostrar as leituras.
6. **Validador de duas camadas.** Determinística: a referência existe? o trecho bate caractere a caractere? há termo do checklist do cheiro? Por modelo: o texto afirma algo que a passagem não sustenta? Conteúdo sem validação não chega ao jogador.

### Desenho

7. **Nunca punir.** Punir erro num jogo sobre culpa é autogol. Regra operacional: **dá para perder o amanhã, nunca o ontem** — ausência atrasa, nunca retrocede; progresso concluído jamais regride; não existe tela de game over.
8. **Sem botão de oração como poder.** Oração devolve *informação*, nunca força, e custa tempo. A regra está em `NEH.4.9`: oraram **e** puseram guarda. Só oração perde a muralha; só guarda perde o sentido.
9. **Sem contador de mortes.** A defesa é dissuasão, não abate — que é o que o texto de fato descreve. Barra de **ânimo**, não barra de vida; vence quem faz o outro desistir.
10. **Nunca mostrar progresso de vocação.** Se o jogador vê que faltam três ações para virar Zelote, a descoberta vira lista de tarefas. Acumula escondido, revela o nome.
11. **Ninguém vê a escolha de ninguém antes de escolher.** E a regra mora no servidor — no cliente é uma linha de DevTools.

### Discrição

12. **Não anunciar não é esconder.** A referência está visível desde o primeiro minuto, no rodapé do card, no mesmo corpo de qualquer outro metadado. Quem quiser saber, sabe. Adiar um nome é legítimo (um pedreiro não sabe quem é o homem que veio da capital); **trocar ou remover um nome não é**.
13. **Checklist do cheiro — o que nunca entra.**
    - *Palavras:* bênção, propósito, jornada de fé, devocional, versículo do dia, testemunho, "Deus tem um plano".
    - *Arte:* luz dourada, pomba, cruz, mãos em oração, túnica, sandália.
    - *Mecânica:* botão de oração, convite para igreja, campo "qual sua igreja", pergunta de religião no onboarding. **Nunca.**
    - *Voz:* narrador que sabe a resposta certa e corrige o jogador moralmente. Pode discordar; não pode pastorear.
14. **A revelação é presente, não aviso.** Projetem o momento em que a ficha cai como capítulo. No POC, ele acontece quando o texto vira a arma mais forte disponível.

### Privacidade e segurança

15. **O perfil de escolhas é um dossiê moral.** Não existe painel de líder, mentor ou pastor. Vê-se a classe do outro, nunca os atributos.
16. **Nenhuma chave de IA no cliente.** Toda chamada por função no servidor, com rate limit por jogador e teto de gasto diário.
17. **Menores (13-17) são arquitetura, não configuração.** Faixa etária no perfil, matchmaking bloqueado, entrada em time só por código, falas pré-formadas com moderação. Times de menores e adultos não se misturam — constraint no banco.
18. **Sem pagar para avançar.** Num jogo sobre obra erguida por sacrifício voluntário, vender o atalho refuta o tema. Monetização: temporada nova ou cosmético. Nunca recurso, nunca timer, nunca atalho de obra.

## Decisões tomadas

| Tema | Decisão |
|---|---|
| Engine do POC | **Unity 6 LTS · 2D URP.** Mais C#/Unity em treino de modelo que GDScript, e a implementação é feita por agentes. |
| Fonte do texto | **YouVersion** (acesso concedido para o projeto). Licença destravada. |
| Arquitetura do corpus | **Corpus duplo:** embeddings sobre tradução de domínio público (índice devolve só *referência*); exibição via YouVersion. Funciona mesmo se os termos proibirem armazenar. |
| Modo do POC | Single player, sem cadastro, offline em runtime. |
| Classes | **Vocação / Ofício / Posto** em três camadas. Vocação é arquétipo portável entre temporadas, descoberta pelo comportamento — nunca escolhida em menu. |
| Multiplayer | Fase seguinte. NPCs do capítulo 3 ocupam assentos que depois viram jogadores, **sem migração de esquema**. |
| Calendário | 12 a 15 sessões por temporada. Os 52 dias são a façanha que o texto anuncia, não a contagem de sessões. |
| Monetização | Primeira temporada gratuita e completa; temporadas seguintes pagas. Cosmético secundário. Licença institucional é o canal não explorado. |

## Decisões em aberto

- **`version_id` da tradução** — depende de três perguntas nos termos do YouVersion: dá para armazenar? dá para puxar em lote? quais versões a chave habilita? Enquanto não sair, usar domínio público e trocar o id depois.
- **Jesus/Espírito Santo como guia** — adiado para fora do POC. A leitura tipológica é legítima; volta como easter egg numa temporada que a mereça, nunca como fala gerada.
- **Qual conceito vence** — o POC decide entre ir fundo em Neemias, voltar ao Cânon, ou fundir. Por isso a camada compartilhada (versículo, vocação, telemetria) se escreve com capricho e a camada de jogo se escreve descartável.
- **Persona** — o campo segue vazio no brainstorm do time, e os dois conceitos miram públicos diferentes: o Cânon fala com quem rejeita o formato; Neemias fala com quem já está disposto.

## O time

Cinco pessoas: 1 backend, 2 frontend, 1 especialista em cibersegurança, 1 product designer. **Ninguém é game dev, e não há escritor nem teólogo no time** — a implementação é feita por agentes, e o conteúdo narrativo se adapta do material já escrito em vez de ser inventado do zero.

Brainstorm ativo do time (Google Docs) tem contribuições de João, Cris, Pedro, Juliana e Matheus, consolidadas em `docs/poc-scope.md`.

## Mapa dos documentos

| Arquivo | O que é |
|---|---|
| `POC-IMPLEMENTATION.md` | **Spec de implementação do POC.** Estrutura de projeto, esquemas de dados, sistemas, conteúdo dos três dias, prova de ânimo, critérios de aceite. É o que se executa. |
| `docs/poc-scope.md` | Escopo e justificativa do POC: as três sessões, o corte, as quatro decisões pendentes, o que é descartável e o que fica. |
| `docs/nehemiah-game-design.md` | Desenho completo do jogo de Neemias: vocações, loop dia/noite, quatro ameaças, prova de ânimo, discrição, produção, riscos. |
| `docs/canon-24h-plan.md` | Plano do hackathon para o Cânon. Histórico, mas os guardrails e a divisão de frentes seguem válidos. |

## Convenções

- Prosa e strings de jogador em **pt-BR**; identificadores, chaves de JSON e nomes de arquivo em **inglês**.
- Referências bíblicas no formato **`LIVRO.CAP.VERS`** (`NEH.4.17`, `JHN.21.6`), sempre em código e em spec.
- Conteúdo de jogo vive em JSON sob `Resources/Data/`, não em ScriptableObject — precisa ser editável fora do Unity.
- `verses.json` é **gerado**. Nunca editar à mão.
