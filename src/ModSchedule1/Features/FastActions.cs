using System;
using HarmonyLib;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Growing;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.PlayerTasks;
using Il2CppScheduleOne.PlayerTasks.Tasks;
using UnityEngine;

namespace ModSchedule1.Features;

/// <summary>
/// Pula as animações/minigames das tarefas do jogador (como o Operations Manager): colocar terra, plantar, aditivos,
/// regar, colher, cogumelos. Cada tarefa termina pelo PRÓPRIO método de conclusão do jogo, então consumo de itens,
/// lixo, XP, contadores de quest e RPCs de rede são exatamente os mesmos do jeito manual.
/// </summary>
public sealed class FastActions : Feature<FastActions>
{
    public override string Id => "FastActions";
    public override string Name => "Ações rápidas";
    public override string Description => "Pula as animações de terra, plantio, aditivos, rega, colheita e cogumelos.";
    public override bool Shared => false;   // pessoal: cada jogador decide para as próprias ações

    private Pref<bool> _soil = null!, _seed = null!, _additive = null!, _water = null!, _harvest = null!, _mushroom = null!;
    private IntPtr _handled;   // tarefa já resolvida (ex.: colheita parou por inventário cheio: segue manual)

    protected override void OnInit()
    {
        _soil = Setting("Soil", true, "Colocar terra/substrato");
        _seed = Setting("Seeds", true, "Plantar sementes");
        _additive = Setting("Additives", true, "Fertilizante, PGR e Speed Grow");
        _water = Setting("Water", true, "Regar com regador");
        _harvest = Setting("Harvest", true, "Colher (todos os botões de uma vez)");
        _mushroom = Setting("Mushrooms", true, "Cogumelos: spawn, borrifar e colher");
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
