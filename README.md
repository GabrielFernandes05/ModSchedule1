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

| Mod | O que faz |
|---|---|
| Better Stacks | Pilhas maiores por categoria; mixing station com mais capacidade |
| No More Trash | Remove o lixo gerado no mundo (só host) |
| Compass Distances | Distância de todos os marcadores na bússola |
| Warehouse Always Open | Warehouse aberto 24h (desbloqueio sem rank opcional) |
| Plants Always Grow | Plantas e cogumelos crescem depois das 4h |
| Employees Always Work | Funcionários trabalham depois das 4h (só host) |
| Absorbent Soil | Aditivos ficam no solo entre colheitas |
| Station Speed | Estações mais rápidas (multiplicador por tipo) |
| Better Employees | Trabalho/passo mais rápidos, salário ajustável, mais estações por funcionário |
| Auto Clear Completed Deals | Esconde conversas de clientes sem negócio pendente |
| Employee Facilities | Funcionários dividem cama/armário |
| Apps: Mods, Banco, Lavagem, Dealers, Receitas, Entregas | No celular do jogo |

Próximos (precisam de assets novos, a partir da 0.8): More Guns, More Clothing, Minimap, Robbery.

**Config:** cada mod tem "Ajustes" no app Mods do celular; tudo fica em `UserData/ModSchedule1.cfg`.
No multiplayer, Better Stacks e Station Speed precisam da mesma config em todos os PCs.

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
