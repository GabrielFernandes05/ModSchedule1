using System;
using HarmonyLib;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Growing;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.PlayerTasks;
using Il2CppScheduleOne.PlayerTasks.Tasks;
using Il2CppScheduleOne.Product.Packaging;
using UnityEngine;

namespace ModSchedule1.Features;

/// <summary>
/// Pula as animações/minigames das tarefas do jogador (como o Operations Manager): colocar terra, plantar, aditivos,
/// regar, colher, cogumelos e as mesas (embalagem, mixer, química, caldeirão, forno, prensa). Cada tarefa termina pelo PRÓPRIO método de conclusão do jogo, então consumo de itens,
/// lixo, XP, contadores de quest e RPCs de rede são exatamente os mesmos do jeito manual.
/// </summary>
public sealed class FastActions : Feature<FastActions>
{
    public override string Id => "FastActions";
    public override string Name => "Ações rápidas";
    public override string Description => "Pula as animações de plantio, rega, colheita, cogumelos e das mesas (embalagem, mixer, química, caldeirão, forno, prensa).";
    public override bool Shared => false;   // pessoal: cada jogador decide para as próprias ações

    private Pref<bool> _soil = null!, _seed = null!, _additive = null!, _water = null!, _harvest = null!, _mushroom = null!,
        _packaging = null!, _mixing = null!, _chemistry = null!, _cauldron = null!, _oven = null!, _press = null!;
    private IntPtr _handled;   // tarefa já resolvida (ex.: colheita parou por inventário cheio: segue manual)

    protected override void OnInit()
    {
        _soil = Setting("Soil", true, "Colocar terra/substrato");
        _seed = Setting("Seeds", true, "Plantar sementes");
        _additive = Setting("Additives", true, "Fertilizante, PGR e Speed Grow");
        _water = Setting("Water", true, "Regar e encher o regador");
        _harvest = Setting("Harvest", true, "Colher (todos os botões de uma vez)");
        _mushroom = Setting("Mushrooms", true, "Cogumelos: inocular, spawn, borrifar e colher");
        _packaging = Setting("Packaging", true, "Embalar (embala tudo o que der)");
        _mixing = Setting("Mixing", true, "Mixer");
        _chemistry = Setting("Chemistry", true, "Estação de química (sem risco de explosão)");
        _cauldron = Setting("Cauldron", true, "Caldeirão");
        _oven = Setting("LabOven", true, "Forno: colocar e quebrar o produto");
        _press = Setting("BrickPress", true, "Prensa de tijolos");
    }

    private void Skip(Task t)
    {
        if (t.TryCast<PourSoilTask>() is { } soil) { if (_soil.Value) Soil(soil); }
        else if (t.TryCast<SowSeedTask>() is { } sow) { if (_seed.Value) sow.Success(); }
        else if (t.TryCast<ApplyAdditiveToPot>() is { } add) { if (_additive.Value) add.FullyCovered(); }
        else if (t.TryCast<PourWaterTask>() is { } water) { if (_water.Value) Water(water); }
        else if (t.TryCast<HarvestPlant>() is { } harvest) { if (_harvest.Value) Harvest(harvest); }
        else if (t.TryCast<ApplyShroomSpawnTask>() is { } spawn) { if (_mushroom.Value) spawn.Success(); }
        else if (t.TryCast<MistMushroomBedTask>() is { } mist) { if (_mushroom.Value) Mist(mist); }
        else if (t.TryCast<HarvestMushroomBedTask>() is { } shrooms) { if (_mushroom.Value) HarvestShrooms(shrooms); }
        else if (t.TryCast<InocculateGrainBagTask>() is { } inoculate) { if (_mushroom.Value) inoculate.Success(); }
        else if (t.TryCast<FillWaterContainer>() is { } fill) { if (_water.Value) Fill(fill); }
        // mesas: o Success de cada tarefa é o que o jogo chama no fim do minigame (consome, gera lixo, inicia a operação)
        else if (t.TryCast<PackageProductTask>() is { } pack) { if (_packaging.Value) pack.Success(); }   // o jogo reabre a próxima sozinho
        else if (t.TryCast<PackageProductTaskMk2>() is { } mk2) { if (_packaging.Value) PackMk2(mk2); }
        else if (t.TryCast<UseMixingStationTask>() is { } mix) { if (_mixing.Value) mix.Success(); }
        else if (t.TryCast<UseChemistryStationTask>() is { } chem) { if (_chemistry.Value) Chemistry(chem); }
        else if (t.TryCast<CauldronTask>() is { } cauldron) { if (_cauldron.Value) cauldron.Success(); }
        else if (t.TryCast<StartLabOvenTask>() is { } ovenLiquid) { if (_oven.Value) ovenLiquid.Success(); }
        else if (t.TryCast<LabOvenSolidTask>() is { } ovenSolid) { if (_oven.Value) ovenSolid.Success(); }
        else if (t.TryCast<FinalizeLabOven>() is { } ovenDone) { if (_oven.Value) { ovenDone.Shatter(); ovenDone.Success(); } }
        else if (t.TryCast<UseBrickPress>() is { } press) { if (_press.Value) { press.currentStep = UseBrickPress.EStep.Complete; press.StopTask(); } }
    }

    // Mk2: a ferramenta embala vários pacotes por tarefa; o jogo chama PackSingleInstance por pacote e Success no fim.
    private void PackMk2(PackageProductTaskMk2 t)
    {
        var st = t.station;
        var def = st.PackagingSlot.ItemInstance?.Definition?.TryCast<PackagingDefinition>();
        if (def == null) return;
        int per = Math.Max(1, def.Quantity);
        int packs = Math.Min(st.ProductSlot.Quantity, st.PackagingSlot.Quantity * per) / per;
        int done = 0;
        for (; done < packs && st.GetState(PackagingStation.EMode.Package) == PackagingStation.EState.CanBegin; done++) st.PackSingleInstance();
        t.Success();
        Log.Debug($"embalados {done} pacotes (Mk2)");
    }

    // Química: o Success guarda cor/nível do frasco só para o visual do cozimento; sem o minigame o frasco está vazio.
    private void Chemistry(UseChemistryStationTask c)
    {
        var flask = c.Station.BoilingFlask.LiquidContainer;
        if (flask.CurrentLiquidLevel < 0.05f)
        {
            flask.SetLiquidColor(c.Recipe.FinalLiquidColor, true, true);
            flask.SetLiquidLevel(0.6f, false);
        }
        c.Success();
    }

    private void Fill(FillWaterContainer f)
    {
        var can = f._waterContainerItem;
        if (can == null) return;
        can.SetFillAmount(can.WaterContainerDefinition.Capacity);
        f.Success();
    }

    // Terra: abre o saco (consome o item e gera o lixo), define o solo e enche o vaso; sucesso só se encheu (como no jogo).
    private void Soil(PourSoilTask s)
    {
        var bag = s._pourableSoil;
        var pot = s._growContainer;
        for (int i = 0; i < 20 && !bag.IsOpen; i++) bag.Cut();
        if (!bag.IsOpen) { Log.Warn("saco de terra não abriu; termine manualmente"); return; }
        s.OnInitialPour();
        pot.ChangeSoilAmount(bag.Unlimited ? pot.SoilCapacity : bag.StartQuantity);
        if (pot.NormalizedSoilAmount >= 1f) s.Success(); else s.Fail();
        Log.Debug("terra colocada");
    }

    // Rega: cada "acerto" do jogo adiciona 20% de umidade; gastamos a água de um acerto perfeito por vez.
    private void Water(PourWaterTask w)
    {
        var can = w.item?.TryCast<WaterContainerInstance>();
        if (can == null) return;
        float perHit = w.pourable.PourRate_L * w.SUCCESS_TIME;   // ponytail: água mínima por acerto; calibrar se gastar pouco demais
        for (int i = 0; i < 10 && w.TaskActive && can.CurrentFillAmount >= perHit; i++)
        {
            can.ChangeFillAmount(-perHit);
            w.TargetReached();
        }
        if (w.TaskActive) w.CancelTask();   // regador vazio: no jogo a tarefa só fica parada
        Log.Debug($"rega concluída (regador com {can.CurrentFillAmount:0.0} L)");
    }

    // Colheita: cada botão pelo próprio Harvest do jogo, checando inventário antes de cada um (igual ao manual).
    private void Harvest(HarvestPlant h)
    {
        var pot = h.pot;
        var plant = pot?.Plant;
        if (plant == null) return;
        var inv = PlayerSingleton<PlayerInventory>.Instance;
        var sites = plant.FinalGrowthStage.GrowthSites;
        var active = plant.ActiveHarvestables.ToArray();
        int harvested = 0;
        foreach (int idx in active)
        {
            if (pot!.Plant == null) break;
            var bud = sites[idx].GetComponentInChildren<PlantHarvestable>();
            if (bud == null) continue;
            if (!inv.CanItemFitInInventory(plant.GetHarvestedProduct(1), bud.ProductQuantity)) { Log.Info("inventário cheio: colheita parou"); break; }
            bud.Harvest(true);
            h.HarvestCount++;
            harvested++;
        }
        if (pot!.Plant == null) h.Success();
        Log.Debug($"colhidos {harvested} botões");
    }

    private void Mist(MistMushroomBedTask m)
    {
        for (int i = 0; i < 60 && m.TaskActive; i++)   // cada borrifada gasta 2% do borrifador: termina sozinho
        {
            m.OnSpray();
            m.OnSuccessfulSpray();
        }
    }

    private void HarvestShrooms(HarvestMushroomBedTask hm)
    {
        var bed = hm._mushroomBed;
        var colony = bed?.CurrentColony;
        if (colony == null) return;
        var inv = PlayerSingleton<PlayerInventory>.Instance;
        foreach (var shroom in colony._growingShrooms.ToArray())
        {
            if (bed!.CurrentColony == null || !inv.CanItemFitInInventory(colony.GetHarvestedShroom(1))) break;
            shroom.Harvest();
            hm._harvestCount++;
        }
        if (bed!.CurrentColony == null) hm.Success();
    }

    // TaskManager.Update é chamado pelo loop da Unity (sem risco de inline) e roda CurrentTask.Update antes do postfix.
    [HarmonyPatch(typeof(TaskManager), nameof(TaskManager.Update))]
    private static class TaskManagerPatch
    {
        private static void Postfix(TaskManager __instance)
        {
            if (!I.Active) return;
            var t = __instance.CurrentTask;
            if (t == null || !t.TaskActive || t.Pointer == I._handled) return;
            I._handled = t.Pointer;
            I.Guard("tarefa " + t.GetType().Name, () => I.Skip(t));
        }
    }
}
