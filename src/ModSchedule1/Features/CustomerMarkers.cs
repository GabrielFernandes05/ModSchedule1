using System;
using System.Collections.Generic;
using Il2CppScheduleOne.Economy;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ModSchedule1.Features;

/// <summary>
/// Bola rosa sobre a cabeça de quem pode virar cliente agora. Mesma regra do marcador "cliente em potencial" do mapa
/// do jogo (Customer.UpdatePotentialCustomerPoI): ainda não desbloqueado e conhece alguém que já é seu cliente
/// (IsUnlockable), mais a região liberada (sem isso o jogo recusa a amostra).
/// </summary>
public sealed class CustomerMarkers : Feature<CustomerMarkers>
{
    public override string Id => "CustomerMarkers";
    public override string Name => "Clientes em potencial";
    public override string Description => "Bola rosa sobre a cabeça dos NPCs que já podem virar clientes (aceitam amostra).";
    public override bool Shared => false;   // pessoal: só desenha na tela de quem usa

    private Pref<float> _size = null!, _height = null!;
    private readonly Dictionary<IntPtr, Transform> _markers = new();
    private float _nextScan;
    private int _lastCount = -1;
    private Material? _material;

    protected override void OnInit()
    {
        _size = Setting("Size", 0.22f, "Tamanho da bola (m)", min: 0.05f, max: 1f);
        _height = Setting("Height", 2.15f, "Altura acima do NPC (m)", min: 1f, max: 4f);
        Enabled.Changed += () => { if (!Enabled.Value) Clear(); };
    }

    public override void OnSceneLoaded(string scene)
    {
        _markers.Clear();   // as bolas são filhas dos NPCs: somem com a cena
        _nextScan = 0;
    }

    public override void OnUpdate()
    {
        if (SceneManager.GetActiveScene().name != "Main") return;
        if (Time.unscaledTime >= _nextScan) { _nextScan = Time.unscaledTime + 2f; Scan(); }
        float bob = Mathf.Sin(Time.time * 2.5f) * 0.06f;
        foreach (var m in _markers.Values)
            if (m != null)
            {
                m.localPosition = new Vector3(0, _height.Value + bob, 0);
                m.localScale = Vector3.one * _size.Value;
            }
    }

    private static bool IsPotential(Customer c)
    {
        var npc = c.NPC;
        if (npc == null || npc.RelationData == null || npc.RelationData.Unlocked || !c.IsUnlockable()) return false;
        var map = Il2CppScheduleOne.Map.Map.Instance;
        var region = map != null ? map.GetRegionData(npc.Region) : null;
        return region == null || region.IsUnlocked;
    }

    private void Scan()
    {
        var keep = new HashSet<IntPtr>();
        foreach (var c in Customer.LockedCustomers)
        {
            if (c == null || !IsPotential(c)) continue;
            keep.Add(c.Pointer);
            if (!_markers.TryGetValue(c.Pointer, out var m) || m == null) _markers[c.Pointer] = Create(c.NPC.transform);
        }
        var gone = new List<IntPtr>();
        foreach (var (key, marker) in _markers)
            if (!keep.Contains(key) || marker == null) gone.Add(key);
        foreach (var key in gone)
        {
            if (_markers[key] != null) Object.Destroy(_markers[key].gameObject);
            _markers.Remove(key);
        }
        if (_markers.Count != _lastCount) { _lastCount = _markers.Count; Log.Debug($"clientes em potencial: {_markers.Count}"); }
    }

    private Transform Create(Transform npc)
    {
        var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ball.name = "MS1_PotentialCustomer";
        Object.Destroy(ball.GetComponent<Collider>());   // não bloqueia tiro, clique nem movimento
        var renderer = ball.GetComponent<MeshRenderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sharedMaterial = Material(renderer);
        ball.transform.SetParent(npc, false);
        return ball.transform;
    }

    /// <summary>Unlit do URP (o jogo usa o mesmo nos cabos): rosa chapado, visível de noite.</summary>
    private Material Material(MeshRenderer primitive)
    {
        if (_material != null) return _material;
        var pink = new Color(1f, 0.35f, 0.75f);
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        _material = shader != null ? new Material(shader) : new Material(primitive.sharedMaterial);
        if (_material.HasProperty("_BaseColor")) _material.SetColor("_BaseColor", pink);
        _material.color = pink;
        _material.hideFlags = HideFlags.DontUnloadUnusedAsset;
        if (shader == null) Log.Warn("shader Unlit do URP não encontrado; usando o material padrão");
        return _material;
    }

    private void Clear()
    {
        foreach (var m in _markers.Values) if (m != null) Object.Destroy(m.gameObject);
        _markers.Clear();
    }
}
