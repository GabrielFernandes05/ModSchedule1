# ModSchedule1

Megamod para **Schedule I**: um único `ModSchedule1.dll` que substitui a pilha de mods do Nexus. Sem S1API, sem Mod Manager e sem dependências entre mods. O MelonLoader fica escondido e é gerenciado pelo launcher.

## Para jogar

1. Baixe **[ModSchedule1.bat](https://raw.githubusercontent.com/GabrielFernandes05/ModSchedule1/master/ModSchedule1.bat)** (clique com o botão direito em "Salvar link como") e dê dois cliques nele.
2. A janela do launcher:
   - acha o jogo pela Steam ou pergunta onde ele está;
   - detecta a versão (Normal/IL2CPP ou Mono);
   - baixa a última versão da branch `master` deste repo;
   - instala o MelonLoader 0.7.3 sem console e sem splash;
   - move qualquer outro mod ou loader para `_ModSchedule1_backup` (nada é apagado).
3. Clique em **Iniciar jogo**. A primeira abertura demora alguns minutos, porque o MelonLoader gera os assemblies.

O botão **Instalação limpa** refaz tudo do zero. A configuração fica em `<jogo>\UserData\ModSchedule1.cfg` (uma seção por mod, com `Enabled = true/false`). Os logs ficam em `<jogo>\MelonLoader\Latest.log` (botão **Log do jogo**) e `%LOCALAPPDATA%\ModSchedule1\launcher.log`.

**Multiplayer:** todos precisam abrir pelo launcher para ter o mesmo DLL. Mods marcados como "só host" rodam apenas em quem hospeda. Usem a mesma config de Better Stacks em todos os PCs.

## Mods

| Mod | Status |
|---|---|
| Better Stacks | v0.1 |
| No More Trash | v0.1 |
| Painel no celular (substitui o Mod Manager) | em andamento |
| Absorbent Soil, Compass Distances, Auto Clear Deals, Warehouse Always Open, Employees Always Work/Plants Grow, Quality Bonus, Station Upgrades, Better Employees | próximos (fáceis) |
| Bank, Laundry, DealersPlus, Operations, Delivery++, Recipe List, Employee Facilities | médio (apps no celular) |
| More Guns, More Clothing, Minimap, Robbery | v2 (assets novos) |

## Desenvolvimento

O mod é compilado contra os assemblies que o MelonLoader gera na máquina com o jogo. Eles são proprietários e **não entram no git**.

1. No Windows, depois de abrir o jogo uma vez pelo launcher, copie da pasta do jogo para `lib/game/` neste repo:
   - `MelonLoader/net6/`
   - `MelonLoader/Il2CppAssemblies/`
2. Compile e publique:
   ```
   dotnet build src/ModSchedule1 -c Release
   ```
   O DLL vai para `release/il2cpp/ModSchedule1.dll`. Faça commit dele junto com `release/features.json` (atualize a `version` lá e em `Core.Version`). Os jogadores recebem a atualização na próxima vez que abrirem o `.bat`.
   Para compilar direto da pasta do jogo: `dotnet build src/ModSchedule1 -c Release -p:GameDir="D:\...\Schedule I"`.

**Estrutura:**
- Cada mod é uma classe em `src/ModSchedule1/Features/` que herda `Feature<T>`.
- Patches Harmony ficam como classes aninhadas e checam `I.Active`.
- Um erro desativa só aquela feature.
- Para registrar uma feature, adicione-a em `Core.Features`.
