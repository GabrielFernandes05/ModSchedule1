using System.Collections.Generic;
using Il2CppScheduleOne.MiniGames;
using Il2CppScheduleOne.Trash;
using MelonLoader;
using UnityEngine;

namespace ModSchedule1.Features;

/// <summary>Remove o lixo assim que ele nasce no mundo (sacos de solo vazios, garrafas, bitucas...).</summary>
public sealed class NoMoreTrash : Feature<NoMoreTrash>
{
    public override string Id => "NoMoreTrash";
    public override string Name => "No More Trash";
    public override string Description => "Some com o lixo gerado no mundo. Itens específicos podem ser mantidos na config.";
    public override bool HostOnly => true;   // o lixo é objeto de rede: só o host destrói, os clientes recebem o despawn

    private Pref<string> _keep = null!;
    private Pref<bool> _keepBags = null!;
    private readonly List<TrashItem> _pending = new();

    protected override void OnInit()
    {
        _keep = Setting("KeepIds", "", "IDs de lixo para manter",
            "Lista separada por vírgula (ex.: glassbottle,cigarette). O ID de cada lixo aparece no log com DebugLogging ligado.");
        _keepBags = Setting("KeepTrashBags", true, "Manter sacos de lixo", "Não remove os sacos de lixo que você ou os funcionários fecham");
    }

    private bool ShouldKeep(TrashItem trash)
    {
        // A bolinha do beer pong é um TrashItem; sem ela a mesa recria a cada 1 s e o mod destrói de novo (RPC em loop).
        // Pelo tipo porque o ID dela só existe no prefab do jogo.
        if (trash.TryCast<TableTennisBall>() != null) return true;
        var id = trash.ID;
        if (_keepBags.Value && id.Contains("trashbag")) return true;
        foreach (var k in _keep.Value.Split(','))
            if (k.Trim().Equals(id, System.StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    // Varre a lista de lixo do jogo no host. Hook na criação não basta: lixo criado por OUTRO jogador chega por RPC e o
    // jogo cria pelo mesmo método, mas chamado direto do código compilado (inlinado), e o patch não vê.
    // Varrer também pega lixo de NPC, do gerador e do save. Destruir no host sincroniza com todos (DestroyTrash é RPC).
    // Só destrói o que já estava na varredura anterior: lixo criado NO HOST vai primeiro do cliente do host para o
    // servidor dele (1-2 ticks) e só então é retransmitido. Destruir antes disso manda o "destruir" na frente do "criar";
    // o cliente não acha nada para destruir e depois cria o lixo, que fica lá para sempre.
    // ponytail: idade mínima = 1 varredura (0,25 s e 3 frames); aumentar se o tick rate do jogo for menor que ~10 Hz.
    private float _next;
    private int _nextFrame;
    private HashSet<int> _seen = new(), _fresh = new();

    public override void OnUpdate()
    {
        if (Time.unscaledTime < _next || Time.frameCount < _nextFrame || !TrashManager.InstanceExists) return;
        _next = Time.unscaledTime + 0.25f;
        _nextFrame = Time.frameCount + 3;
        var list = TrashManager.Instance._trashItems;
        _pending.Clear();
        _fresh.Clear();
        for (int i = 0; i < list.Count; i++)
        {
            var trash = list[i];
            if (trash == null || ShouldKeep(trash)) continue;
            int id = trash.GetInstanceID();
            if (_seen.Contains(id)) _pending.Add(trash);
            else _fresh.Add(id);
        }
        (_seen, _fresh) = (_fresh, _seen);
        foreach (var trash in _pending)
        {
            Log.Debug($"removido: {trash.ID}");
            trash.DestroyTrash();
        }
        _pending.Clear();
    }
}
