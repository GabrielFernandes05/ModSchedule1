using HarmonyLib;
using Il2CppScheduleOne.Employees;
using Il2CppScheduleOne.EntityFramework;
using MelonLoader;

namespace ModSchedule1.Features;

/// <summary>
/// Vários funcionários dividem a mesma cama/armário. O jogo já guarda a casa em cada funcionário (config) e cada um
/// desconta o próprio salário do dinheiro guardado nela; o que impede é só o filtro "já atribuída" da prancheta.
/// Remover o mod não quebra o save (o próprio jogo carrega duas pessoas na mesma casa sem reclamar).
/// </summary>
public sealed class EmployeeFacilities : Feature<EmployeeFacilities>
{
    public override string Id => "EmployeeFacilities";
    public override string Name => "Employee Facilities";
    public override string Description => "Vários funcionários podem dividir a mesma cama/armário.";
    // A prancheta valida na máquina de quem atribui; o servidor não confere. Rótulos são locais. Todos os PCs.

    private Pref<int> _cap = null!;

    protected override void OnInit()
    {
        _cap = Setting("EmployeesPerHome", 4, "Funcionários por cama/armário", "Jogo: 1. Coloque dinheiro suficiente para o salário de todos");
    }

    private static int Occupants(EmployeeHome home, out float wages)
    {
        wages = 0;
        if (!EmployeeManager.InstanceExists) return 0;
        var all = EmployeeManager.Instance.AllEmployees;
        int n = 0;
        for (int i = 0; i < all.Count; i++)
        {
            var e = all[i];
            if (e == null || e.Fired) continue;
            var h = e.GetHome();
            if (h == null || h.Pointer != home.Pointer) continue;
            n++;
            wages += e.DailyWage;
        }
        return n;
    }

    // Filtro da prancheta: a original recusa qualquer casa com AssignedEmployee. Aqui: até N ocupantes.
    [HarmonyPatch(typeof(EmployeeHome), nameof(EmployeeHome.IsBuildableEntityAValidEmployeeHome))]
    private static class FilterPatch
    {
        private static bool Prefix(BuildableItem obj, ref string reason, ref bool __result)
        {
            if (!I.Active || obj == null) return true;
            try
            {
                var home = obj.GetComponent<EmployeeHome>();
                if (home == null) return true;   // deixa o jogo responder "não é uma casa"
                int n = Occupants(home, out _);
                __result = n < System.Math.Max(1, I._cap.Value);
                reason = __result ? string.Empty : $"Cheia ({n}/{I._cap.Value})";
                return false;
            }
            catch (System.Exception e) { I.Fault("IsBuildableEntityAValidEmployeeHome", e); return true; }
        }
    }

    // Quando um ocupante sai/é demitido, o jogo limpa o rótulo da casa mesmo com outros morando nela: reassume.
    [HarmonyPatch(typeof(Employee), nameof(Employee.UpdateBehaviour))]
    private static class HealPrimaryPatch
    {
        private static void Postfix(Employee __instance)
        {
            if (!I.Active || __instance.Fired) return;
            try
            {
                var home = __instance.GetHome();
                if (home != null && home.AssignedEmployee == null) home.SetAssignedEmployee(__instance);
            }
            catch (System.Exception e) { I.Fault("UpdateBehaviour", e); }
        }
    }

    // Texto da maleta/armário: mostra quantos dividem e o total de salários a deixar ali por dia.
    [HarmonyPatch(typeof(EmployeeHome), nameof(EmployeeHome.UpdateStorageText))]
    private static class StorageTextPatch
    {
        private static void Postfix(EmployeeHome __instance)
        {
            if (!I.Active) return;
            try
            {
                int n = Occupants(__instance, out float wages);
                if (n > 1 && __instance.Storage != null)
                    __instance.Storage.StorageEntitySubtitle = $"Dividida por {n} funcionários: deixe {PhoneUI.Ui.Money(wages)} por dia";
            }
            catch (System.Exception e) { I.Fault("UpdateStorageText", e); }
        }
    }
}
