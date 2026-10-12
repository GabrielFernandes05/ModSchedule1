using HarmonyLib;
using Il2CppScheduleOne.Delivery;
using Il2CppScheduleOne.Economy;

namespace ModSchedule1.Features;

/// <summary>
/// Entregas rápidas. Nos dois casos só encurtamos o contador que o próprio jogo decrementa a cada minuto no servidor:
/// a chegada segue o fluxo nativo (doca livre, caminhão, notificação; dead drop com mensagem e missão de coleta).
/// Às 4:00 o relógio para, então pedidos feitos nessa hora chegam depois de dormir.
/// </summary>
public sealed class FastDeliveries : Feature<FastDeliveries>
{
    public override string Id => "FastDeliveries";
    public override string Name => "Entregas rápidas";
    public override string Description => "Entregas das lojas (app Delivery) e dead drops dos fornecedores chegam em minutos.";
    public override bool HostOnly => true;   // a chegada é decidida no servidor

    private Pref<int> _shopMinutes = null!, _supplierMinutes = null!;

    protected override void OnInit()
    {
        _shopMinutes = Setting("ShopMinutes", 0, "Lojas: minutos de jogo até chegar (0 = na hora)", min: 0, max: 360);
        _supplierMinutes = Setting("SupplierMinutes", 1, "Fornecedores: minutos de jogo até o dead drop", min: 1, max: 360);
    }

    // Roda todo minuto de jogo (e depois de dormir) em todos os PCs; a chegada só é checada no servidor.
    [HarmonyPatch(typeof(DeliveryManager), nameof(DeliveryManager.OnTimePass))]
    private static class ShopPatch
    {
        private static void Prefix(DeliveryManager __instance)
        {
            if (!I.Active) return;
            var list = __instance.Deliveries;
            for (int i = 0; i < list.Count; i++)
            {
                var d = list[i];
                if (d != null && d.Status == EDeliveryStatus.InTransit && d.TimeUntilArrival > I._shopMinutes.Value)
                    d.TimeUntilArrival = I._shopMinutes.Value;
            }
        }
    }

    // O fornecedor completa o dead drop quando o contador chega a 0 no MinPass. Nunca deixamos em 0 com o pedido
    // preparando (um save nesse instante perderia o pedido): no mínimo 1, e o próprio MinPass completa.
    [HarmonyPatch(typeof(Supplier), nameof(Supplier.MinPass))]
    private static class SupplierPatch
    {
        private static void Prefix(Supplier __instance)
        {
            if (!I.Active || !Net.IsHost || !__instance._deadDropPreparing) return;
            int target = System.Math.Max(1, I._supplierMinutes.Value);
            if (__instance.MinsUntilDeaddropReady > target) __instance.MinsUntilDeaddropReady = target;
        }
    }
}
