using System;
using System.Collections.Generic;

namespace ModSchedule1;

/// <summary>Conta pura da junção de munição (sem tipos do jogo: testada no EngineCheck).</summary>
public static class AmmoMerge
{
    /// <summary>
    /// rounds[i] = balas no slot i (0 = slot sem essa munição). Redistribui o total enchendo os slots com munição na
    /// ordem, cada um até <paramref name="cap"/>; slots que sobram ficam com 0 (o chamador remove o item).
    /// O total nunca muda: se algum slot já passa do limite (limite foi reduzido), ele vira o limite.
    /// </summary>
    public static int[] Merge(IReadOnlyList<int> rounds, int cap)
    {
        var result = new int[rounds.Count];
        int total = 0, limit = Math.Max(1, cap);
        foreach (var r in rounds) { total += Math.Max(0, r); limit = Math.Max(limit, r); }
        for (int i = 0; i < rounds.Count; i++)
        {
            if (rounds[i] <= 0) continue;
            result[i] = Math.Min(limit, total);
            total -= result[i];
        }
        return result;
    }
}
