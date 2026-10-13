# ModSchedule1

Megamod para **Schedule I**: um único `ModSchedule1.dll` que substitui a pilha de mods do Nexus. Sem S1API, sem Mod Manager e sem dependências entre mods. O MelonLoader fica escondido e é gerenciado pelo launcher.

## Para jogar

1. Baixe o **[ModSchedule1.exe](https://raw.githubusercontent.com/GabrielFernandes05/ModSchedule1/master/release/launcher/ModSchedule1.exe)**.
   - Se o Windows mostrar "O Windows protegeu o computador", clique em **Mais informações** e depois em **Executar assim mesmo**. O aviso aparece porque o .exe não é assinado.
   - Quem já tem o `ModSchedule1.bat` pode rodá-lo uma vez: ele instala o .exe em `%LOCALAPPDATA%\ModSchedule1` e cria um atalho na Área de Trabalho.
2. O launcher faz tudo sozinho:
   - acha o jogo pela Steam (ou pergunta onde ele está);
   - confere se o jogo está na versão Normal (na Mono, ele bloqueia e explica como trocar);
   - baixa a última versão do `master`;
   - instala o MelonLoader 0.7.3 escondido;
   - move outros mods e loaders para `_ModSchedule1_backup` (nada é apagado);
   - atualiza a si mesmo quando sai uma versão nova;
   - aberto, confere o GitHub a cada 5 minutos e mostra o botão **Atualizar** quando sai versão nova (com o jogo aberto, peça para fechá-lo antes).
3. Clique em **JOGAR**. Deixe o launcher aberto: ele mostra as mensagens e erros do mod em tempo real e, se o jogo crashar, guarda o log em `%LOCALAPPDATA%\ModSchedule1\crash-logs`.

A primeira abertura do jogo demora alguns minutos, porque o MelonLoader gera os assemblies.

**Ferramentas:** instalação limpa, abrir o log do jogo, abrir a config (`UserData\ModSchedule1.cfg`) e abrir a pasta do jogo.

**Multiplayer:** todos precisam abrir o jogo pelo launcher para ter o mesmo DLL. Mods marcados como "só host" rodam apenas em quem hospeda.

## Mods

| Mod | O que faz | Config |
|---|---|---|
| Better Stacks | Pilhas maiores por categoria (armas, regador e roupas ficam com a pilha original: guardam balas/água/cor por item). Pentes empilham por quantidade ("3x pente de 7", o scroll divide; 5x por padrão); pente parcial não empilha com cheio e a recarga separa um pente da pilha, então nenhuma bala some. Mixer com mais capacidade | do host |
| No More Trash | Remove o lixo gerado no mundo, inclusive o que os outros jogadores geram (o host varre a lista de lixo) | do host |
| Warehouse Always Open | Warehouse aberto 24h (desbloqueio sem rank opcional) | do host |
| Plants Always Grow | Plantas e cogumelos crescem depois das 4h | do host |
| Employees Always Work | Funcionários trabalham depois das 4h (só host) | do host |
| Absorbent Soil | Aditivos ficam no solo entre colheitas | do host |
| Station Speed | Estações mais rápidas (multiplicador por tipo); a melhoria comprada multiplica por cima | do host |
| Better Employees | Trabalho/passo mais rápidos, salário ajustável, mais estações por funcionário; **salário pago sozinho pelo banco** (sem dinheiro no armário), **2x vagas por imóvel** (ou um mínimo fixo, ex.: 20; muda na hora) e **10 rotas por handler** (a lista da prancheta rola). Baixar as vagas não demite ninguém; tirar o mod faz o jogo descartar quem passar do limite | do host |
| Employee Facilities | Funcionários dividem cama/armário (4 por padrão, configurável) | do host |
| Ações rápidas | Pula as animações de terra, plantio, aditivos, rega, encher regador, colheita, cogumelos e das mesas: embalagem (embala tudo), mixer, química (sem explosão), caldeirão, forno e prensa | pessoal |
| Compass Distances | Distância de todos os marcadores na bússola | pessoal |
| Auto Clear Completed Deals | Esconde conversas de clientes sem negócio pendente | pessoal |
| Dealers+ | Coletar dinheiro no app de dealers do jogo | pessoal |
| Recipe List | **F8** abre o livro de receitas em abas: produtos (efeitos, valor e receita completa), ingredientes, efeitos, química, estações (forno, caldeirão, secador, prensa) e cultivo, com os tempos já com o mod e as melhorias. Também mostra a receita completa no app de produtos | pessoal |
| DeliveryApp++ | Favoritos e pedidos recorrentes no app Delivery do jogo | pessoal |
| Bank + Laundry | App **Finanças** no celular, em abas: Banco, Lavagem, Fornecedores (pagar dívida na hora), Dealers (coletar, ver estoque e abrir o inventário do dealer à distância), Melhorias e Histórico | pessoal |
| More Guns | Submetralhadora, pistola de pente estendido, Magnum e escopeta automática no Stan (visual das armas do jogo) | do host |
| More Clothing | Peças que só NPCs usavam (uniformes etc.) à venda na loja de roupas | do host |
| Minimap | Minimapa redondo no canto da tela, com os ícones do mapa do celular; gira com a câmera. **F7** mostra/esconde (configurável) | pessoal |
| Robbery | Na tela da loja: **Assaltar o caixa** ($350–750, 1x por dia por loja; armado sempre dá certo) e **Furtar o carrinho** (chance de ser visto). O caixa chama a polícia | do host |
| Acelerar o tempo | **F6** liga/desliga o relógio acelerado (10x, configurável). Tudo passa minuto a minuto como no normal; para sozinho às 4:00 (hora de dormir) | só host |
| Clientes em potencial | Bola rosa sobre a cabeça de quem já pode virar cliente (mesma regra do marcador do mapa: conhece um cliente seu e a região está liberada) | pessoal |
| Fornecedores: pagar dívida | Pagar na hora: app Finanças > Fornecedores. Esta opção liga o **automático** (desligado por padrão): deposita no esconderijo à distância e o jogo abate como se você tivesse ido lá | pessoal |
| Entregas rápidas | Entregas das lojas (app Delivery) chegam na hora e o dead drop dos fornecedores em 1 minuto de jogo (pela chegada normal do jogo: doca livre, caminhão, mensagem) | só host |
| Melhorias | Compradas no app Finanças (só o host, pelo banco): **Crescimento** até 2x (plantas e cogumelos), **Lavagem** até 3x e cada mesa até 3x (forno, mixer, química, caldeirão, secador, embalagem dos funcionários). Nada acelera sem comprar; ficam salvas por save | do host |
| Dealers mandam mensagem | Cada venda ("vendi 2x OG Kush pra Fulano por $X"), aviso de produto acabando e resumo do dia à meia-noite, no app Messages | só host |
| Relógio e XP na tela | Hora, dia e barra de XP do rank sempre visíveis, logo abaixo da bússola (ou num canto) | pessoal |

**Configurações:** botão **Mods** no menu do jogo (principal ou pausa), logo abaixo de Settings; também em Configurações → aba Mods. Clique no nome de um mod para ver as opções. Tudo fica salvo em `UserData/ModSchedule1.cfg`.

**Multiplayer:** as configs marcadas "do host" são enviadas pelo host para todos (via dados do lobby da Steam) e aparecem travadas para os outros jogadores durante a sessão; o cfg de cada um não é alterado. As "pessoais" cada um ajusta a seu gosto.

Armas/roupas com modelos 3D próprios exigiriam AssetBundles feitos na Unity.

## Desenvolvimento

O mod é compilado contra os assemblies que o MelonLoader gera na máquina com o jogo. Eles são proprietários e **não entram no git**.

1. No Windows, depois de abrir o jogo uma vez pelo launcher, copie da pasta do jogo para `lib/game/` neste repo:
   - `MelonLoader/net6/`
   - `MelonLoader/Il2CppAssemblies/`
2. Compile e publique (mod e launcher):
   ```
   dotnet build src/ModSchedule1 -c Release
   ```
   O DLL vai para `release/il2cpp/ModSchedule1.dll`. O launcher: `dotnet build launcher/ModSchedule1.Launcher -c Release` gera `release/launcher/ModSchedule1.exe` e `version.txt` (suba o `<Version>` no csproj para os launchers instalados se atualizarem).
   Verificação da lógica de arquivos do launcher: `dotnet run --project launcher/EngineCheck -c Release`. Faça commit dele junto com `release/features.json` (atualize a `version` lá e em `Core.Version`). Os jogadores recebem a atualização ao abrir o launcher, ou pelo botão Atualizar se ele já estiver aberto (só contam commits que mexem em `release/`).
   Para compilar direto da pasta do jogo: `dotnet build src/ModSchedule1 -c Release -p:GameDir="D:\...\Schedule I"`.

**Estrutura:**
- Cada mod é uma classe em `src/ModSchedule1/Features/` que herda `Feature<T>`.
- Patches Harmony ficam como classes aninhadas e checam `I.Active`.
- Um erro desativa só aquela feature.
- Para registrar uma feature, adicione-a em `Core.Features`.
