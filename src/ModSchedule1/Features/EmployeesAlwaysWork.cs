using HarmonyLib;
using Il2CppScheduleOne.Employees;

namespace ModSchedule1.Features;

/// <summary>Funcionários não param às 4h ("my shift ends at 4AM"): continuam trabalhando até alguém dormir.</summary>
public sealed class EmployeesAlwaysWork : Feature<EmployeesAlwaysWork>
{
    public override string Id => "EmployeesAlwaysWork";
    public override string Name => "Employees Always Work";
    public override string Description => "Funcionários continuam trabalhando depois das 4h (precisam de casa e estar pagos).";
    public override bool HostOnly => true;   // UpdateBehaviour só decide trabalho no servidor

    // Employee.OnTick -> UpdateBehaviour (override por cargo) -> base.UpdateBehaviour + CanWork(), todos checam IsEndOfDay.
    // Patchamos cada override para que a checagem de CanWork() das subclasses também veja 3:59.
    [HarmonyPatch]
    private static class UpdateBehaviourPatch
    {
        private static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Employee), nameof(Employee.UpdateBehaviour));
            yield return AccessTools.Method(typeof(Botanist), nameof(Botanist.UpdateBehaviour));
            yield return AccessTools.Method(typeof(Chemist), nameof(Chemist.UpdateBehaviour));
            yield return AccessTools.Method(typeof(Packager), nameof(Packager.UpdateBehaviour));
            yield return AccessTools.Method(typeof(Cleaner), nameof(Cleaner.UpdateBehaviour));
        }

        private static void Prefix(out bool __state)
        {
            __state = I.Active && EndOfDay.IsNow;
            if (__state) EndOfDay.Enter();
        }

        private static void Postfix(bool __state)
        {
            if (__state) EndOfDay.Exit();
        }
    }
}
