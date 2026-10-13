using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ModSchedule1.Features;

/// <summary>
/// Todas as misturas possíveis a partir de uma droga base, sem tocar no jogo: os dados do mapa de mistura já vêm
/// copiados para arrays (efeitos = índices). <see cref="Mix"/> faz o mesmo que EffectMixCalculator.MixProperties.
/// </summary>
public static class MixSearch
{
    public const int MaxEffects = 8;   // EffectMixCalculator.MAX_PROPERTIES

    public sealed record Node(int[] Effects, Node? Parent, int Mixer, float Cost, int Value, int Steps, ulong Used)
    {
        public float Profit => Value - Cost;

        /// <summary>Ingredientes na ordem em que entram na mistura.</summary>
        public List<int> Mixers()
        {
            var list = new List<int>();
            for (var n = this; n.Parent != null; n = n.Parent) list.Add(n.Mixer);
            list.Reverse();
            return list;
        }
    }

    /// <summary>
    /// Igual ao jogo: cada efeito que já existe reage (ou não) com o ingrediente, as reações entram na ordem da lista
    /// e só trocam se o resultado ainda não está lá; depois entra o efeito do ingrediente, se couber.
    /// react[efeito, ingrediente] = efeito resultante (-1 = não reage); adds[ingrediente] = efeito que ele adiciona.
    /// </summary>
    public static int[] Mix(int[] effects, int mixer, int[,] react, int[] adds)
    {
        var list = new List<int>(effects);
        foreach (var e in effects)
        {
            int to = react[e, mixer];
            if (to >= 0 && !list.Contains(to)) list[list.IndexOf(e)] = to;
        }
        if (!list.Contains(adds[mixer]) && list.Count < MaxEffects) list.Add(adds[mixer]);
        return list.ToArray();
    }

    /// <summary>
    /// Busca em largura por número de ingredientes; devolve a receita mais barata de cada conjunto de efeitos
    /// alcançado (sem a base), da que mais lucra para a que menos. Os filtros entram na busca, não depois dela:
    /// a receita mais barata de um conjunto pode não usar o ingrediente pedido mesmo existindo outra que usa.
    /// allowed = bits dos ingredientes que podem entrar; required = bits dos que a receita tem que ter.
    /// </summary>
    public static List<Node> Search(int[] start, int[,] react, int[] adds, float[] price, Func<int[], int> value,
        int maxSteps, int beam, ulong allowed = ulong.MaxValue, ulong required = 0)
    {
        // a ordem dos efeitos muda as próximas reações; e o mesmo estado com/sem os obrigatórios são caminhos diferentes
        var seen = new HashSet<(ulong, ulong)> { (Key(start), 0) };
        var best = new Dictionary<ulong, Node>();       // mesmo conjunto = mesmo produto/preço
        var layer = new List<Node> { new(start, null, -1, 0f, value(start), 0, 0) };
        for (int step = 1; step <= maxSteps && layer.Count > 0; step++)
        {
            var next = new List<Node>();
            foreach (var n in layer)
                for (int m = 0; m < adds.Length; m++)
                {
                    if ((allowed >> m & 1) == 0) continue;
                    var fx = Mix(n.Effects, m, react, adds);
                    ulong used = n.Used | 1UL << m;
                    if (!seen.Add((Key(fx), used & required))) continue;
                    var child = new Node(fx, n, m, n.Cost + price[m], value(fx), step, used);
                    next.Add(child);
                    if ((used & required) != required) continue;
                    ulong mask = Mask(fx);
                    if (!best.TryGetValue(mask, out var old) || child.Cost < old.Cost) best[mask] = child;
                }
            // ponytail: feixe — exato enquanto o passo tem até `beam` estados (~3 ingredientes); depois segue pelos que
            // já têm mais obrigatórios e, entre eles, pelos que mais lucram. Mistura que só fica boa depois de passar
            // por uma ruim pode escapar; subir o feixe resolve.
            layer = next.Count > beam
                ? next.OrderByDescending(c => BitOperations.PopCount(c.Used & required)).ThenByDescending(c => c.Profit).Take(beam).ToList()
                : next;
        }
        best.Remove(Mask(start));
        return best.Values.OrderByDescending(n => n.Profit).ThenBy(n => n.Steps).ToList();
    }

    /// <summary>Conjunto de efeitos como bits (até 64 efeitos no jogo).</summary>
    public static ulong Mask(IEnumerable<int> effects)
    {
        ulong mask = 0;
        foreach (var e in effects) mask |= 1UL << e;
        return mask;
    }

    // lista ordenada em 6 bits por efeito: 8 efeitos x 6 = 48 bits
    private static ulong Key(int[] effects)
    {
        ulong key = 0;
        foreach (var e in effects) key = (key << 6) | (uint)(e + 1);
        return key;
    }
}
