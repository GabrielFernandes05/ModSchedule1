using System;
using System.Collections.Generic;
using Il2CppScheduleOne;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.UI.Shop;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace ModSchedule1;

/// <summary>
/// Itens novos criados a partir de itens do jogo. O Registry apaga itens de runtime a cada troca de cena, então
/// re-registramos no onPreLoad do LoadManager (antes do save carregar o inventário) e de novo ao carregar a cena.
/// IDs são fixos no código: todos os PCs precisam ter os mesmos (o launcher garante o mesmo DLL).
/// Sem o mod, o jogo só descarta esses itens do save (não quebra).
/// </summary>
public static class CustomItems
{
    private static readonly Log Log = new("Itens");
    private static readonly List<Func<IEnumerable<ItemDefinition>>> Builders = new();
    private static readonly List<ItemDefinition> Built = new();   // referências fortes: o GC/Unity não descarregam
    private static bool _hooked, _built;
    private static GameObject? _prefabHolder;

    /// <summary>Feature registra uma função que cria seus itens (chamada uma vez, quando o Registry já existe).</summary>
    public static void Add(Func<IEnumerable<ItemDefinition>> builder) => Builders.Add(builder);

    /// <summary>Pai inativo e persistente para prefabs clonados (não rodam Update; o jogo instancia cópias ativas).</summary>
    public static Transform PrefabHolder
    {
        get
        {
            if (_prefabHolder == null)
            {
                _prefabHolder = new GameObject("MS1_Prefabs");
                _prefabHolder.SetActive(false);
                Object.DontDestroyOnLoad(_prefabHolder);
            }
            return _prefabHolder.transform;
        }
    }

    public static T Clone<T>(T original, string id, string name) where T : ItemDefinition
    {
        var def = Object.Instantiate(original);
        def.ID = id;
        def.Name = name;
        def.name = id;
        def.hideFlags = HideFlags.DontUnloadUnusedAsset;
        return def;
    }

    public static void OnSceneLoaded()
    {
        if (!_hooked && LoadManager.InstanceExists)
        {
            LoadManager.Instance.onPreLoad.AddListener((UnityAction)new Action(RegisterAll));
            _hooked = true;
        }
        RegisterAll();
    }

    public static void RegisterAll()
    {
        if (!Registry.InstanceExists) return;
        try
        {
            if (!_built)
            {
                _built = true;
                foreach (var b in Builders)
                {
                    try { Built.AddRange(b()); }
                    catch (Exception e) { Log.Error($"falha ao criar itens: {e}"); }
                }
                Log.Info($"{Built.Count} itens novos criados");
            }
            int added = 0;
            foreach (var def in Built)
            {
                if (def == null || Registry.ItemExists(def.ID)) continue;
                Registry.Instance.AddToRegistry(def);
                added++;
            }
            if (added > 0) Log.Debug($"{added} itens registrados no Registry");
        }
        catch (Exception e) { Log.Error($"registro de itens: {e}"); }
    }

    /// <summary>Acrescenta o item à loja (estoque ilimitado: estoque limitado é sincronizado e salvo pelo jogo).</summary>
    public static bool AddListing(ShopInterface shop, StorableItemDefinition item, Color? tint = null)
    {
        if (shop.GetListing(item.ID) != null) return false;
        var listing = new ShopListing { Item = item };
        if (tint.HasValue) { listing.UseIconTint = true; listing.IconTint = tint.Value; }
        shop.Listings.Add(listing);
        listing.Initialize(shop);
        shop.CreateListingUI(listing);
        return true;
    }
}
