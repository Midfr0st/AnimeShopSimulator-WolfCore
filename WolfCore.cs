using HarmonyLib;
using Il2CppProject.Code.Core.Services;
using Il2CppProject.Code.Gameplay.UI.GameMenu;
using Il2CppProject.Code.Gameplay.UI.Computer;
using MelonLoader;
using MelonLoader.Utils;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UI;

[assembly: MelonInfo(typeof(WolfCore.WolfCoreMod), "WolfCore", "0.2.5", "WolfMods")]

namespace WolfCore;

public sealed class WolfModRegistration
{
    public WolfModRegistration(
        string id,
        string displayName,
        string version,
        string description,
        Action<bool>? onEnabledChanged = null,
        Action? drawSettings = null,
        Func<bool>? isCapturingInput = null,
        bool defaultEnabled = true,
        bool systemMod = false)
    {
        Id = id;
        DisplayName = displayName;
        Version = version;
        Description = description;
        OnEnabledChanged = onEnabledChanged;
        DrawSettings = drawSettings;
        IsCapturingInput = isCapturingInput;
        DefaultEnabled = defaultEnabled;
        SystemMod = systemMod;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string Version { get; }
    public string Description { get; }
    public Action<bool>? OnEnabledChanged { get; }
    public Action? DrawSettings { get; }
    public Func<bool>? IsCapturingInput { get; }
    public bool DefaultEnabled { get; }
    public bool SystemMod { get; }
}

internal sealed class RegisteredWolfMod
{
    public RegisteredWolfMod(WolfModRegistration registration, bool enabled)
    {
        Registration = registration;
        Enabled = enabled;
    }

    public WolfModRegistration Registration { get; set; }
    public bool Enabled { get; set; }
}

internal sealed class WolfCoreState
{
    public Dictionary<string, bool> EnabledMods { get; set; } = new(StringComparer.Ordinal);
    public List<string> TerminalTileOrder { get; set; } = new();
}

public static class WolfModRegistry
{
    private static readonly Dictionary<string, RegisteredWolfMod> Mods = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, bool> SavedEnabled = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, WolfTerminalPageRegistration> TerminalPages = new(StringComparer.Ordinal);
    private static readonly List<string> SavedTerminalTileOrder = new();
    private static string _settingsPath = string.Empty;
    private static bool _initialized;

    public static void Register(
        string id,
        string displayName,
        string version,
        string description,
        Action<bool>? onEnabledChanged,
        Action? drawSettings,
        Func<bool>? isCapturingInput,
        bool defaultEnabled = true)
    {
        Register(new WolfModRegistration(
            id,
            displayName,
            version,
            description,
            onEnabledChanged,
            drawSettings,
            isCapturingInput,
            defaultEnabled));
    }

    public static void Register(WolfModRegistration registration)
    {
        if (registration == null)
            throw new ArgumentNullException(nameof(registration));
        if (string.IsNullOrWhiteSpace(registration.Id))
            throw new ArgumentException("WolfMod id cannot be empty.", nameof(registration));

        var enabled = registration.SystemMod
            ? true
            : SavedEnabled.TryGetValue(registration.Id, out var saved)
                ? saved
                : registration.DefaultEnabled;

        Mods[registration.Id] = new RegisteredWolfMod(registration, enabled);
        ApplyEnabled(registration, enabled);
        if (_initialized && !SavedEnabled.ContainsKey(registration.Id))
        {
            SavedEnabled[registration.Id] = enabled;
            Save();
        }

        MelonLogger.Msg(
            $"WolfCore: зарегистрирован {registration.DisplayName} {registration.Version} " +
            $"({(enabled ? "включён" : "выключен")}).");
    }

    public static bool IsEnabled(string id)
    {
        return Mods.TryGetValue(id, out var mod) && mod.Enabled;
    }

    public static void RegisterTerminalPage(
        string modId,
        string tileId,
        string label,
        byte[] tilePng,
        Func<ComputerWorldView, bool> openPage,
        Action closePage,
        Func<bool> isPageOpen,
        Action tickPage)
    {
        if (string.IsNullOrWhiteSpace(modId) || string.IsNullOrWhiteSpace(tileId))
            throw new ArgumentException("WolfMod terminal page id cannot be empty.");
        if (!Mods.ContainsKey(modId))
            throw new InvalidOperationException($"WolfMod {modId} must be registered before its terminal page.");
        if (tilePng == null || tilePng.Length == 0)
            throw new ArgumentException("WolfMod terminal tile image cannot be empty.", nameof(tilePng));

        TerminalPages[tileId] = new WolfTerminalPageRegistration(
            modId, tileId, label, tilePng, openPage, closePage, isPageOpen, tickPage);
        if (!SavedTerminalTileOrder.Contains(tileId, StringComparer.Ordinal))
        {
            SavedTerminalTileOrder.Add(tileId);
            if (_initialized)
                Save();
        }
        WolfTerminalMenuController.NotifyRegistryChanged();
        MelonLogger.Msg($"WolfCore: терминальная вкладка '{label}' зарегистрирована в слоте {SavedTerminalTileOrder.IndexOf(tileId) + 1}.");
    }

    public static void Unregister(string id)
    {
        var tileIds = TerminalPages.Values
            .Where(page => string.Equals(page.ModId, id, StringComparison.Ordinal))
            .Select(page => page.TileId)
            .ToArray();
        foreach (var tileId in tileIds)
            TerminalPages.Remove(tileId);
        Mods.Remove(id);
        WolfTerminalMenuController.NotifyRegistryChanged();
    }

    public static bool IsSettingsOpen(string id)
    {
        return WolfCoreMod.IsSettingsOpen(id);
    }

    public static void SuppressEscapeForCurrentFrame()
    {
        WolfCoreMod.SuppressEscapeForCurrentFrame();
    }

    internal static IReadOnlyList<RegisteredWolfMod> GetRegisteredMods()
    {
        return Mods.Values
            .OrderByDescending(mod => mod.Registration.SystemMod)
            .ThenBy(mod => mod.Registration.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    internal static RegisteredWolfMod? Get(string id)
    {
        return Mods.TryGetValue(id, out var mod) ? mod : null;
    }

    internal static void SetEnabled(string id, bool enabled)
    {
        if (!Mods.TryGetValue(id, out var mod) || mod.Registration.SystemMod)
            return;
        if (mod.Enabled == enabled)
            return;

        mod.Enabled = enabled;
        SavedEnabled[id] = enabled;
        ApplyEnabled(mod.Registration, enabled);
        Save();
        WolfTerminalMenuController.NotifyRegistryChanged();
    }

    internal static void Initialize()
    {
        // Functional mods can be initialized before WolfCore by MelonLoader.
        // Keep terminal pages registered during that early phase: Load() clears
        // the persisted-order buffer before filling it from disk.
        var earlyTerminalTileOrder = SavedTerminalTileOrder
            .Concat(TerminalPages.Keys)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        _settingsPath = Path.Combine(MelonEnvironment.UserDataDirectory, "WolfCore.settings.json");
        Load();
        foreach (var tileId in earlyTerminalTileOrder)
        {
            if (!SavedTerminalTileOrder.Contains(tileId, StringComparer.Ordinal))
                SavedTerminalTileOrder.Add(tileId);
        }
        _initialized = true;

        foreach (var mod in Mods.Values)
        {
            var enabled = mod.Registration.SystemMod
                ? true
                : SavedEnabled.TryGetValue(mod.Registration.Id, out var saved)
                    ? saved
                    : mod.Registration.DefaultEnabled;
            mod.Enabled = enabled;
            SavedEnabled[mod.Registration.Id] = enabled;
            ApplyEnabled(mod.Registration, enabled);
        }

        Save();
        WolfTerminalMenuController.NotifyRegistryChanged();
    }

    private static void ApplyEnabled(WolfModRegistration registration, bool enabled)
    {
        try
        {
            registration.OnEnabledChanged?.Invoke(enabled);
        }
        catch (Exception exception)
        {
            MelonLogger.Error(
                $"WolfCore: мод {registration.DisplayName} не применил состояние: {exception}");
        }
    }

    private static void Load()
    {
        SavedEnabled.Clear();
        SavedTerminalTileOrder.Clear();
        if (!File.Exists(_settingsPath))
            return;

        try
        {
            var state = JsonConvert.DeserializeObject<WolfCoreState>(File.ReadAllText(_settingsPath));
            if (state?.EnabledMods == null)
                return;
            foreach (var pair in state.EnabledMods)
                SavedEnabled[pair.Key] = pair.Value;
            if (state.TerminalTileOrder != null)
            {
                foreach (var tileId in state.TerminalTileOrder.Where(id => !string.IsNullOrWhiteSpace(id)))
                {
                    if (!SavedTerminalTileOrder.Contains(tileId, StringComparer.Ordinal))
                        SavedTerminalTileOrder.Add(tileId);
                }
            }
        }
        catch (Exception exception)
        {
            MelonLogger.Warning($"WolfCore: настройки не прочитаны: {exception.Message}");
        }
    }

    private static void Save()
    {
        if (string.IsNullOrWhiteSpace(_settingsPath))
            return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            var state = new WolfCoreState
            {
                EnabledMods = new Dictionary<string, bool>(SavedEnabled, StringComparer.Ordinal),
                TerminalTileOrder = new List<string>(SavedTerminalTileOrder)
            };
            var temporaryPath = _settingsPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonConvert.SerializeObject(state, Formatting.Indented));
            File.Move(temporaryPath, _settingsPath, true);
        }
        catch (Exception exception)
        {
            MelonLogger.Error($"WolfCore: настройки не сохранены: {exception}");
        }
    }

    internal static IReadOnlyList<(string TileId, WolfTerminalPageRegistration? Registration)> GetTerminalSlots()
    {
        return SavedTerminalTileOrder
            // Persist every known tile id so re-enabling a mod restores its
            // relative order, but only active installed mods take visible
            // terminal slots and contribute to the number of pages.
            .Where(tileId => TerminalPages.TryGetValue(tileId, out var page) && IsEnabled(page.ModId))
            .Select(tileId => (tileId, (WolfTerminalPageRegistration?)TerminalPages[tileId]))
            .ToArray();
    }
}

public sealed class WolfCoreMod : MelonMod
{
    private readonly List<(Button Button, bool Interactable)> _pauseButtonStates = new();
    private Vector2 _scrollPosition;
    private GUIStyle? _modsButtonStyle;
    private GUIStyle? _windowStyle;
    private GUIStyle? _titleStyle;
    private GUIStyle? _descriptionStyle;
    private GUIStyle? _cardStyle;
    private static GameMenuView? _pauseView;
    private static bool _pauseVisible;
    private static bool _menuOpen;
    private static string? _selectedModId;
    private static int _suppressEscapeThroughFrame = -1;

    internal static bool IsMenuOpen => _menuOpen;

    public override void OnInitializeMelon()
    {
        Instance = this;
        WolfModRegistry.Initialize();
        WolfModRegistry.Register(new WolfModRegistration(
            "wolfmods.core",
            "WolfCore",
            "0.2.5",
            "Общее меню и ядро для набора модов WolfMods.",
            systemMod: true));
        PauseMenuPatches.Install();
        LoggerInstance.Msg("WolfCore 0.2.5 загружен. Меню модов и реестр терминальных вкладок готовы.");
    }

    public override void OnUpdate()
    {
        WolfTerminalMenuController.Tick();
        if (!_menuOpen || !Input.GetKeyDown(KeyCode.Escape))
            return;

        var selected = string.IsNullOrEmpty(_selectedModId)
            ? null
            : WolfModRegistry.Get(_selectedModId);
        var settingsCaptureActive = false;
        try
        {
            settingsCaptureActive = selected?.Registration.IsCapturingInput?.Invoke() == true;
        }
        catch
        {
            settingsCaptureActive = false;
        }

        if (!settingsCaptureActive && Time.frameCount > _suppressEscapeThroughFrame)
            CloseMenu();
    }

    public override void OnGUI()
    {
        if (!_pauseVisible)
            return;

        EnsureStyles();
        GUI.depth = -10000;

        if (!_menuOpen)
        {
            if (GUI.Button(new Rect(18f, Screen.height - 72f, 160f, 50f), "Mods", _modsButtonStyle!))
                OpenMenu();
            return;
        }

        var oldColor = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.72f);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = oldColor;

        var width = Math.Min(760f, Screen.width - 30f);
        var height = Math.Min(680f, Screen.height - 50f);
        var rect = new Rect((Screen.width - width) / 2f, (Screen.height - height) / 2f, width, height);
        GUILayout.BeginArea(rect, GUIContent.none, _windowStyle!);
        if (string.IsNullOrEmpty(_selectedModId))
            DrawModsList();
        else
            DrawModSettings(_selectedModId);
        GUILayout.EndArea();
    }

    internal static bool IsSettingsOpen(string id)
    {
        return _menuOpen && string.Equals(_selectedModId, id, StringComparison.Ordinal);
    }

    public static void SuppressEscapeForCurrentFrame()
    {
        _suppressEscapeThroughFrame = Time.frameCount;
    }

    internal static void NotifyPauseActivated(GameMenuView view)
    {
        _pauseView = view;
        _pauseVisible = true;
    }

    internal static void NotifyPauseDeactivated()
    {
        Instance?.CloseMenu();
        _pauseVisible = false;
        _pauseView = null;
    }

    private static WolfCoreMod? Instance { get; set; }

    public override void OnDeinitializeMelon()
    {
        WolfTerminalMenuController.Shutdown();
        CloseMenu();
        Instance = null;
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        WolfTerminalMenuController.OnSceneChanged();
    }

    private void OpenMenu()
    {
        _menuOpen = true;
        _selectedModId = null;
        _scrollPosition = Vector2.zero;
        DisablePauseButtons();
    }

    private void CloseMenu()
    {
        if (!_menuOpen && _pauseButtonStates.Count == 0)
            return;

        _menuOpen = false;
        _selectedModId = null;
        RestorePauseButtons();
    }

    private void DrawModsList()
    {
        GUILayout.Label("Wolf Mods", _titleStyle!);
        GUILayout.Label("Установленные моды", _descriptionStyle!);
        GUILayout.Space(10f);

        _scrollPosition = GUILayout.BeginScrollView(_scrollPosition);
        foreach (var mod in WolfModRegistry.GetRegisteredMods())
        {
            GUILayout.BeginVertical(_cardStyle!);
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical();
            GUILayout.Label($"{mod.Registration.DisplayName}  v{mod.Registration.Version}", _titleStyle!);
            GUILayout.Label(mod.Registration.Description, _descriptionStyle!);
            GUILayout.EndVertical();

            if (mod.Registration.SystemMod)
            {
                GUILayout.Label("Системный\nВсегда включён", _descriptionStyle!, GUILayout.Width(150f));
            }
            else
            {
                var nextEnabled = GUILayout.Toggle(
                    mod.Enabled,
                    mod.Enabled ? "Включён" : "Выключен",
                    GUILayout.Width(130f),
                    GUILayout.Height(42f));
                if (nextEnabled != mod.Enabled)
                    WolfModRegistry.SetEnabled(mod.Registration.Id, nextEnabled);
            }

            if (mod.Registration.DrawSettings != null &&
                GUILayout.Button("Настройки", GUILayout.Width(130f), GUILayout.Height(42f)))
            {
                _selectedModId = mod.Registration.Id;
                _scrollPosition = Vector2.zero;
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.Space(8f);
        }
        GUILayout.EndScrollView();

        if (GUILayout.Button("Закрыть", GUILayout.Height(42f)))
            CloseMenu();
    }

    private void DrawModSettings(string id)
    {
        var mod = WolfModRegistry.Get(id);
        if (mod == null)
        {
            _selectedModId = null;
            return;
        }

        GUILayout.Label($"{mod.Registration.DisplayName}  v{mod.Registration.Version}", _titleStyle!);
        GUILayout.Label(mod.Registration.Description, _descriptionStyle!);
        GUILayout.Space(12f);

        _scrollPosition = GUILayout.BeginScrollView(_scrollPosition);
        try
        {
            mod.Registration.DrawSettings?.Invoke();
        }
        catch (Exception exception)
        {
            GUILayout.Label("Настройки этого мода не удалось показать. Подробности записаны в журнал.", _descriptionStyle!);
            MelonLogger.Error($"WolfCore: ошибка панели {mod.Registration.DisplayName}: {exception}");
        }
        GUILayout.EndScrollView();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("← К списку модов", GUILayout.Height(42f)))
        {
            _selectedModId = null;
            _scrollPosition = Vector2.zero;
        }
        if (GUILayout.Button("Закрыть", GUILayout.Height(42f)))
            CloseMenu();
        GUILayout.EndHorizontal();
    }

    private void DisablePauseButtons()
    {
        _pauseButtonStates.Clear();
        if (_pauseView == null)
            return;

        foreach (var button in EnumeratePauseButtons(_pauseView))
        {
            if (button == null)
                continue;
            _pauseButtonStates.Add((button, button.interactable));
            button.interactable = false;
        }
    }

    private void RestorePauseButtons()
    {
        foreach (var state in _pauseButtonStates)
        {
            if (state.Button != null)
                state.Button.interactable = state.Interactable;
        }
        _pauseButtonStates.Clear();
    }

    private static IEnumerable<Button?> EnumeratePauseButtons(GameMenuView view)
    {
        yield return view._continueButton;
        yield return view._inviteFriendsButton;
        yield return view._commandButton;
        yield return view._saveButton;
        yield return view._settingsButton;
        yield return view._menuButton;
        yield return view._feedBackButton;
        yield return view._stuckButton;
    }

    private void EnsureStyles()
    {
        if (_windowStyle != null)
            return;

        _modsButtonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 22,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        _windowStyle = new GUIStyle(GUI.skin.box)
        {
            padding = new RectOffset(20, 20, 18, 18)
        };
        _titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 21,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            wordWrap = true
        };
        _titleStyle.normal.textColor = Color.white;
        _descriptionStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 15,
            alignment = TextAnchor.MiddleLeft,
            wordWrap = true
        };
        _descriptionStyle.normal.textColor = Color.white;
        _cardStyle = new GUIStyle(GUI.skin.box)
        {
            padding = new RectOffset(12, 12, 10, 10)
        };
    }
}

internal static class PauseMenuPatches
{
    private static readonly HarmonyLib.Harmony Harmony = new("wolfmods.core");

    public static void Install()
    {
        var activate = AccessTools.Method(typeof(GameMenuView), "OnActivateView");
        var deactivate = AccessTools.Method(typeof(GameMenuView), "OnDeactivateView");
        var escape = AccessTools.Method(typeof(InputService), "OnEscInput");
        if (activate == null || deactivate == null || escape == null)
            throw new MissingMethodException("WolfCore: методы меню паузы не найдены.");

        Harmony.Patch(activate, postfix: new HarmonyMethod(typeof(PauseMenuPatches), nameof(OnPauseActivatedPostfix)));
        Harmony.Patch(deactivate, prefix: new HarmonyMethod(typeof(PauseMenuPatches), nameof(OnPauseDeactivatedPrefix)));
        Harmony.Patch(escape, prefix: new HarmonyMethod(typeof(PauseMenuPatches), nameof(OnEscapePrefix)));
    }

    private static void OnPauseActivatedPostfix(GameMenuView __instance)
    {
        WolfCoreMod.NotifyPauseActivated(__instance);
    }

    private static void OnPauseDeactivatedPrefix()
    {
        WolfCoreMod.NotifyPauseDeactivated();
    }

    private static bool OnEscapePrefix()
    {
        return !WolfCoreMod.IsMenuOpen;
    }
}
