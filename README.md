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
   - atualiza a si mesmo quando sai uma versão nova.
3. Clique em **JOGAR**. Deixe o launcher aberto: ele mostra as mensagens e erros do mod em tempo real e, se o jogo crashar, guarda o log em `%LOCALAPPDATA%\ModSchedule1\crash-logs`.

A primeira abertura do jogo demora alguns minutos, porque o MelonLoader gera os assemblies.

**Ferramentas:** instalação limpa, abrir o log do jogo, abrir a config (`UserData\ModSchedule1.cfg`) e abrir a pasta do jogo.

**Multiplayer:** todos precisam abrir o jogo pelo launcher para ter o mesmo DLL. Mods marcados como "só host" rodam apenas em quem hospeda.

## Mods

| Mod | O que faz | Config |
|---|---|---|
| Better Stacks | Pilhas maiores por categoria; mixing station com mais capacidade | do host |
| No More Trash | Remove o lixo gerado no mundo (só host) | do host |
| Warehouse Always Open | Warehouse aberto 24h (desbloqueio sem rank opcional) | do host |
| Plants Always Grow | Plantas e cogumelos crescem depois das 4h | do host |
| Employees Always Work | Funcionários trabalham depois das 4h (só host) | do host |
| Absorbent Soil | Aditivos ficam no solo entre colheitas | do host |
| Station Speed | Estações mais rápidas (multiplicador por tipo) | do host |
| Better Employees | Trabalho/passo mais rápidos, salário ajustável, mais estações por funcionário | do host |
| Employee Facilities | Funcionários dividem cama/armário | do host |
| Ações rápidas | Pula as animações de terra, plantio, aditivos, rega, colheita e cogumelos | pessoal |
| Compass Distances | Distância de todos os marcadores na bússola | pessoal |
| Auto Clear Completed Deals | Esconde conversas de clientes sem negócio pendente | pessoal |
| Dealers+ | Coletar dinheiro no app de dealers do jogo | pessoal |
| Recipe List | Receita completa no app de produtos do jogo | pessoal |
| DeliveryApp++ | Favoritos e pedidos recorrentes no app Delivery do jogo | pessoal |
| Bank + Laundry | App **Finanças** no celular: banco, lavagem, dealers e transações | pessoal |
| More Guns | Submetralhadora, pistola de pente estendido, Magnum e escopeta automática no Stan (visual das armas do jogo) | do host |
| More Clothing | Peças que só NPCs usavam (uniformes etc.) à venda na loja de roupas | do host |

**Configurações:** menu do jogo (principal ou pausa) → **Configurações → aba Mods**. Tudo fica salvo em `UserData/ModSchedule1.cfg`.

**Multiplayer:** as configs marcadas "do host" são enviadas pelo host para todos (via dados do lobby da Steam) e aparecem travadas para os outros jogadores durante a sessão; o cfg de cada um não é alterado. As "pessoais" cada um ajusta a seu gosto.

Próximos: Minimap, Robbery. Armas/roupas com modelos 3D próprios exigiriam AssetBundles feitos na Unity.

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
   Verificação da lógica de arquivos do launcher: `dotnet run --project launcher/EngineCheck -c Release`. Faça commit dele junto com `release/features.json` (atualize a `version` lá e em `Core.Version`). Os jogadores recebem a atualização na próxima vez que abrirem o `.bat`.
   Para compilar direto da pasta do jogo: `dotnet build src/ModSchedule1 -c Release -p:GameDir="D:\...\Schedule I"`.

**Estrutura:**
- Cada mod é uma classe em `src/ModSchedule1/Features/` que herda `Feature<T>`.
- Patches Harmony ficam como classes aninhadas e checam `I.Active`.
- Um erro desativa só aquela feature.
- Para registrar uma feature, adicione-a em `Core.Features`.
