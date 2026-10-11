using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppScheduleOne;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.UI.Phone;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using GamePhone = Il2CppScheduleOne.UI.Phone.Phone;

namespace ModSchedule1.PhoneUI;

/// <summary>
/// In-game phone app without S1API. Same approach S1API uses: when the phone's HomeScreen starts in the Main scene,
/// create a full-stretch panel under AppsCanvas, clone the native icon prefab into HomeScreen/AppIcons and register
/// it with the HomeScreen so the game treats it like a native icon. Opening/closing mirrors the native App&lt;T&gt; flow.
/// </summary>
public abstract class PhoneApp
{
    /// <summary>All apps of the megamod. Features add theirs here.</summary>
    public static readonly List<PhoneApp> All = new() { new ControlPanelApp() };

    protected static readonly Log Log = new("Celular");

    public abstract string AppName { get; }      // nome do GameObject, único
    public abstract string IconLabel { get; }
    public virtual Color IconColor => new(0.15f, 0.55f, 0.35f);
    protected virtual bool Horizontal => true;

    private GameObject? _panel;
    private GameObject? _container;

    protected abstract void BuildUI(GameObject container);
    /// <summary>Called every time the app opens; refresh numbers/labels here.</summary>
    protected virtual void OnOpened() { }

    public bool IsOpen => _panel != null && _container != null && _container.activeSelf && GamePhone.ActiveApp == _panel;

    private void Spawn(HomeScreen home)
    {
        var appsCanvas = home.transform.parent.Find("AppsCanvas");
        if (appsCanvas == null) { Log.Error("AppsCanvas não encontrado"); return; }

        var old = appsCanvas.Find(AppName);
        if (old != null) Object.Destroy(old.gameObject);

        _panel = Ui.Stretch(AppName, appsCanvas);
        _container = Ui.Stretch("Container", _panel.transform);
        _container.AddComponent<Image>().color = new Color(0.08f, 0.09f, 0.11f, 1f);
        BuildUI(_container);
        _container.SetActive(false);

        var phone = GamePhone.Instance;
        phone.closeApps += new Action(Close);
        GameInput.RegisterExitListener(
            DelegateSupport.ConvertDelegate<GameInput.ExitDelegate>(new Action<ExitAction>(OnExit)), 1);

        SpawnIcon(home);
        Log.Info($"app '{IconLabel}' criado");
    }

    private void SpawnIcon(HomeScreen home)
    {
        var icons = Ui.FindDescendant(home.transform, "AppIcons");
        var prefab = home.appIconPrefab;
        if (icons == null || prefab == null) { Log.Error("AppIcons/appIconPrefab não encontrados"); return; }

        var icon = Object.Instantiate(prefab, icons);
        icon.name = AppName;
        icon.transform.Find("Notifications")?.gameObject.SetActive(false);

        var label = icon.transform.Find("Label")?.GetComponent<Text>();
        if (label != null) label.text = IconLabel;
        var image = icon.transform.Find("Mask/Image")?.GetComponent<Image>();
        if (image != null) image.sprite = Ui.SolidSprite(IconColor);

        var button = icon.GetComponent<Button>();
        var selectable = icon.GetComponent<UISelectable>();
        if (button == null || selectable == null) { Log.Error("prefab do ícone sem Button/UISelectable"); return; }
        home.appIcons.Add(button);
        home.uiPanel.AddSelectable(selectable);
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener((UnityEngine.Events.UnityAction)new Action(Open));
    }

    public void Open()
    {
        try
        {
            var phone = GamePhone.Instance;
            if (GamePhone.ActiveApp != null && GamePhone.ActiveApp != _panel)
                phone.RequestCloseApp();
            AppsCanvas.Instance.SetIsOpen(true);
            HomeScreen.Instance.SetIsOpen(false);
            phone.SetIsHorizontal(Horizontal);
            phone.SetLookOffsetMultiplier(Horizontal ? 0.6f : 1f);
            GamePhone.ActiveApp = _panel;
            _container!.SetActive(true);
            OnOpened();
            Log.Debug($"aberto: {AppName}");
        }
        catch (Exception e) { Log.Error($"falha ao abrir {AppName}: {e}"); }
    }

    public void Close()
    {
        if (!IsOpen) return;
        try
        {
            GamePhone.ActiveApp = null;
            var phone = GamePhone.Instance;
            phone.SetIsHorizontal(false);
            phone.SetLookOffsetMultiplier(1f);
            _container!.SetActive(false);
            AppsCanvas.Instance.SetIsOpen(false);
            HomeScreen.Instance.SetIsOpen(true);
            Log.Debug($"fechado: {AppName}");
        }
        catch (Exception e) { Log.Error($"falha ao fechar {AppName}: {e}"); }
    }

    // ESC / botão direito: fecha o app e volta para a home do celular, como os apps nativos.
    private void OnExit(ExitAction exit)
    {
        if (exit.Used || !IsOpen || !GamePhone.Instance.IsOpen) return;
        exit.Used = true;
        Close();
    }

    [HarmonyPatch(typeof(HomeScreen), "Start")]
    internal static class HomeScreenStartPatch
    {
        private static void Postfix(HomeScreen __instance)
        {
            if (SceneManager.GetActiveScene().name != "Main") return;
            foreach (var app in All)
            {
                try { app.Spawn(__instance); }
                catch (Exception e) { Log.Error($"falha ao criar app {app.AppName}: {e}"); }
            }
        }
    }
}
