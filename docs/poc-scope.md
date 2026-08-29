# A Primeira Porta — escopo do POC

> POC descartável. Decide entre três futuros: ir fundo neste jogo, voltar para o Cânon, ou fundir os dois.

Uma porta, uma ameaça, três sessões. **Vinte minutos de jogo** que existem para responder uma pergunta só: o laço central parece jogo de verdade, e o texto chega como recompensa em vez de sermão?

Para a especificação técnica que implementa este escopo, ver [`../POC-IMPLEMENTATION.md`](../POC-IMPLEMENTATION.md).

## O brainstorm do time já concorda em mais do que parece

**A convergência — ler o texto é o que te fortalece.** Matheus escreveu barra de stamina e armadura mais forte. Juliana, leitura recomendada que vale pontuação. Cris, o capítulo completo ao apertar "saber mais". Pedro, vocação acumulada pelas ações. São quatro formulações do mesmo laço, e é exatamente a métrica-norte do Cânon. Está decidido — só falta escolher a palavra.

**A convergência que ninguém percebeu.** "Resistir às tentações de falar com estranhos para continuar focado na missão" é, literalmente, as quatro cartas chamando Neemias a Ono, e a resposta dele em `NEH.6.3`. O João desenhou a mecânica sem estar olhando para o capítulo. Quando o material-fonte produz a mesma mecânica que a intuição do time, é sinal de que o material aguenta o jogo.

**Também alinhado:** apresentar a história sem dizer que é Neemias (funciona porque você é um convocado, não o líder); e vocação em vez de classe (a camada que sobrevive à troca de temporada).

## As três sessões

### 01 — A convocação
- Escolha de personagem: masculino ou feminino, três peças cosméticas. Sem cadastro, sem pergunta de religião.
- Você chega numa cidade em ruínas. As pessoas não conseguem voltar para casa — são saqueadas. **A dor primeiro, a missão depois.**
- O governador te chama e te dá um trecho. Ele nunca é nomeado nesta sessão.
- Conversa com três moradores. Um deles cita, de boca, um versículo literal buscado por referência.
- Cata entulho, assenta as primeiras pedras. **A muralha sobe na tela.**
- Fim do dia: dividir a sua gente entre obra e guarda.

*Prova: abre sem cheiro, e o verbo central tem retorno visível.*

### 02 — Os que chamam de fora
- Amanhece. Você vê o que a noite fez com o trecho — e o que teria feito sem guarda.
- Os moradores sabem coisas novas. Um viu cavaleiros na estrada. Um ouviu falar dos juros. **Um deles está errado**, e o jogo não avisa qual.
- Chega o convite: homens querem conversar com você, longe da obra. Ir custa um dia e piora o trecho — o jogo cobra, não repreende.
- Recusar entrega a linha de graça (`NEH.6.3`).
- Constrói, e divide de novo — agora sabendo o que a divisão custa.

*Prova: conversar é mecânica, e a informação tem peso.*

### 03 — A brecha, e a leitura
- A investida chega. **Prova de ânimo por turnos** — sem barra de vida, sem contador de mortes.
- No meio da prova, o jogo mostra uma página. A tática está escrita nela (`NEH.4.17`). **A revelação é uma arma, não um aviso.**
- A porta fecha. **O seu nome fica gravado no trecho**, como o capítulo 3 faz.
- "Saber mais" abre o capítulo inteiro **dentro do app**. Ler faz subir uma coisa visível.
- O jogo te dá um nome: a sua vocação, deduzida do que você fez nas três sessões.

*Prova: o texto chega como prêmio, e a métrica-norte é medida.*

## O corte

| No POC | Fora do POC |
|---|---|
| Uma porta, quatro estágios visíveis | Multiplayer de qualquer tipo — solo primeiro |
| Seis moradores, três com fala (nomes do cap. 3) | A aldeia como base separada |
| Divisão obra/guarda, e uma noite que resolve | Lojinha do templo, dracmas, qualquer compra |
| Uma prova de ânimo por turnos | A figueira diária — o quiz já cobre o hábito |
| Busca de versículo por referência + validador | Onboarding que coleta dados |
| Leitor de capítulo no app + evento `deep_read` | As outras nove portas |
| Quiz do dia (check-in vale mesmo errando) | Três das quatro ameaças — só a brecha |
| Talentos como contador simples | José do Egito e qualquer outra temporada |
| O peixe do Cris (`JHN.21.6`) | Arte própria — tileset CC0 e placeholder |
| Revelação da vocação no fim | |

## Quatro decisões que precisam de humano

### Jesus ou o Espírito Santo como guia do jogo
*Origem: comentário do João Vitor sobre o "mestre".*

Três problemas somados: é anacrônico em Neemias, que se passa quatro séculos antes; contraria a regra do próprio resumo do Cânon (*nada de colocar palavras na boca de Deus ou de Jesus*); e é o caminho mais rápido para perder o público que ainda não está dentro.

**Proposta:** o "mestre" é o governador, humano e nomeado depois. O divino fica onde o texto o coloca em Neemias — no resultado e na oração, nunca em fala gerada.
**Status:** adiado para fora do POC por decisão do Pedro. Não descartado — a leitura tipológica (apontamentos a Cristo no Antigo Testamento) é legítima e pode voltar como easter egg numa temporada que a mereça.

### Fé como barra de mana, que esvazia e trava acesso
*Origem: proposta do João, comentário do João Vitor.*

O problema é mais fundo que o nome: **fé que se gasta é fé como moeda**, e tirar acesso quando está baixa é punir — exatamente o que o documento do Cânon proíbe.

**Proposta:** manter a mecânica que todo mundo quer (ler fortalece) e mudar o que ela é. O que enche é **Ânimo**, e ele nunca tranca porta nenhuma: ânimo alto abre opções, ânimo baixo não fecha as básicas.

### Capítulo completo por e-mail, e missões compradas
*Origem: proposta do Cris.*

O e-mail é bom instinto e canal fraco: manda a leitura para fora, onde não dá para medir — e a leitura medida é a razão de ser do produto. Missão comprada que libera dracmas para recursos é pagar para avançar.

**Proposta:** leitor dentro do app, com e-mail como extra opcional. Se houver compra, que seja temporada nova ou cosmético — nunca recurso, nunca atalho de obra.

### Multiplayer dentro do MVP
*Origem: proposta do João; conflito com o escopo atual.*

O desenho atual diz solo primeiro, com os NPCs do capítulo 3 ocupando assentos que depois viram jogadores, sem migração de esquema. As duas coisas não cabem no mesmo mês.

**Proposta:** solo no POC, multiplayer na versão seguinte sem custo de reescrita.

## Como codar algo que talvez seja jogado fora

### Camada que fica, dê no que der
- Busca de versículo por referência e o validador de duas camadas
- O sistema de vocação — é o mesmo dos dois projetos
- Telemetria de aprofundamento: `deep_read`, `chapter_opened`
- Quiz do dia e o check-in que vale mesmo errando
- Integração YouVersion, com crédito de tradução

Escrevam esta camada com capricho. O Cânon precisa dela inteira.

### Camada descartável
- Tilemap, sprites, câmera e movimentação
- O simulador de obra e o ciclo dia/noite
- A prova de ânimo por turnos
- Diálogo dos moradores e a árvore de informação

Aqui vale gambiarra, asset de graça e código feio. É protótipo — o objetivo é sentir, não manter.

## As contas

**Economia.** Não pesquisem preço histórico — economia real não é balanceada para ser divertida. **A unidade é a sessão:** decidam quantas sessões a temporada deve ter e derivem tudo daí. O capítulo 3 dá tamanhos relativos de trecho; use para proporção e invente o resto. Sabor histórico vai no nome e na arte, nunca no número.

**Calendário.** 52 sessões é longo demais. Mire **12 a 15 sessões por temporada**, cada uma cobrindo alguns dias da ficção. Os 52 dias continuam sendo a façanha que o texto anuncia.

**Monetização.** **Primeira temporada gratuita e completa; temporadas seguintes pagas.** O que se vende é mais da coisa boa, e o grátis não é mutilado. Cosmético como secundário. E existe um canal que ninguém citou e que pode ser maior que o consumidor: **licença institucional** para grupos de jovens, escolas e igrejas — receita sem um único padrão escuro.

## O critério

> Se as três sessões não fizerem alguém apertar "saber mais" sem que o jogo mande, o POC respondeu — e respondeu barato.

Não é gráfico, não é quantidade de sistema, não é quantas pessoas acharam bonito. É uma pessoa saindo da fase por vontade própria para ler o capítulo.
