using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppProject.Code.Gameplay.UI.Computer;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace WolfCore;

public sealed class WolfTerminalPageRegistration
{
    public WolfTerminalPageRegistration(
        string modId,
        string tileId,
        string label,
        byte[] tilePng,
        Func<ComputerWorldView, bool> openPage,
        Action closePage,
        Func<bool> isPageOpen,
        Action tickPage)
    {
        ModId = modId;
        TileId = tileId;
        Label = label;
        TilePng = tilePng;
        OpenPage = openPage;
        ClosePage = closePage;
        IsPageOpen = isPageOpen;
        TickPage = tickPage;
    }

    public string ModId { get; }
    public string TileId { get; }
    public string Label { get; }
    public byte[] TilePng { get; }
    public Func<ComputerWorldView, bool> OpenPage { get; }
    public Action ClosePage { get; }
    public Func<bool> IsPageOpen { get; }
    public Action TickPage { get; }
}

internal static class WolfTerminalMenuController
{
    private const float DiscoveryRetrySeconds = 1.5f;
    private const string PageButtonName = "WolfMenu_TerminalPageButton";
    private const int NativeTileCount = 5;
    private const int Columns = 3;
    private const int VisibleSlots = 6;

    private static readonly Dictionary<int, NativeTileState> NativeStates = new();
    private static readonly Dictionary<string, RuntimeTile> RuntimeTiles = new(StringComparer.Ordinal);

    private static ComputerWorldView? _view;
    private static GameObject? _menuObject;
    private static GameObject? _pageButtonObject;
    private static TextMeshProUGUI? _pageButtonLabel;
    private static UnityAction? _pageButtonAction;
    private static Button.ButtonClickedEvent? _originalBackEvent;
    private static UnityAction? _backAction;
    private static WolfTerminalPageRegistration? _activePage;
    private static bool _backRewired;
    private static bool _closeRequested;
    private static bool _registryDirty = true;
    private static float _nextDiscoveryTime;
    private static int _page;
    private static Vector2[] _slotPositions = Array.Empty<Vector2>();
    private static Vector2 _employeeUnavailablePosition;
    private static bool _employeeUnavailableActive;
    private static bool _employeeUnavailableCaptured;

    private sealed class NativeTileState
    {
        public GameObject Object = null!;
        public Vector2 Position;
        public bool Active;
    }

    private sealed class RuntimeTile
    {
        public WolfTerminalPageRegistration Registration = null!;
        public GameObject ButtonObject = null!;
        public Texture2D Texture = null!;
        public Sprite Sprite = null!;
        public UnityAction ClickAction = null!;
    }

    public static void NotifyRegistryChanged()
    {
        _registryDirty = true;
    }

    public static void Tick()
    {
        // ComputerWorldView survives for the whole gameplay scene.  Re-scanning every
        // 0.20 seconds after it was already found allocates native wrappers and becomes
        // visible as periodic frame-time spikes in a busy shop.  Only retry while the
        // cached terminal is genuinely missing; a scene change resets the cache below.
        if ((_view == null || _view.gameObject == null || _view._content == null) &&
            Time.unscaledTime >= _nextDiscoveryTime &&
            _activePage == null)
        {
            _nextDiscoveryTime = Time.unscaledTime + DiscoveryRetrySeconds;
            DiscoverComputer();
        }

        if (_view == null || _view.gameObject == null || _view._content == null)
            return;

        if (_activePage != null)
        {
            if (!WolfModRegistry.IsEnabled(_activePage.ModId) ||
                _closeRequested ||
                _view._closeButton == null ||
                !_view._closeButton.gameObject.activeInHierarchy ||
                !SafeIsPageOpen(_activePage))
            {
                CloseActivePage();
                return;
            }

            try
            {
                _activePage.TickPage();
            }
            catch (Exception exception)
            {
                MelonLogger.Warning($"WolfCore: ввод страницы '{_activePage.Label}' завершился ошибкой: {exception.Message}");
            }
            return;
        }

        if (_registryDirty)
            SynchronizeTiles();

        if (_menuObject == null || !_menuObject.activeInHierarchy)
            return;

        TickPageNavigation();
    }

    private static void DiscoverComputer()
    {
        try
        {
            ComputerWorldView? first = null;
            ComputerWorldView? active = null;
            foreach (var candidate in UnityEngine.Object.FindObjectsOfType<ComputerWorldView>())
            {
                if (candidate == null || candidate.gameObject == null || candidate._content == null ||
                    candidate._ordersButton == null || candidate._storeButton == null ||
                    candidate._paymentsButton == null || candidate._upgradeButton == null ||
                    candidate._employeeButton == null)
                    continue;
                first ??= candidate;
                var menu = candidate._ordersButton.transform.parent?.gameObject;
                if (active == null && menu != null && menu.activeInHierarchy)
                    active = candidate;
            }

            var target = active ?? ((_view != null && _view.gameObject != null) ? _view : first);
            if (target == null)
                return;
            if (_view != null && _view.gameObject != null && _view.GetInstanceID() == target.GetInstanceID())
                return;

            ResetCurrentView();
            _view = target;
            _menuObject = target._ordersButton.transform.parent?.gameObject;
            CaptureNativeLayout();
            _registryDirty = true;
            SynchronizeTiles();
            MelonLogger.Msg($"WolfCore: терминальные вкладки подключены к '{target.gameObject.name}'.");
        }
        catch (Exception exception)
        {
            MelonLogger.Warning($"WolfCore: терминал пока не найден: {exception.Message}");
        }
    }

    private static void CaptureNativeLayout()
    {
        NativeStates.Clear();
        var native = GetNativeTiles();
        foreach (var tile in native)
        {
            var rect = tile.GetComponent<RectTransform>();
            if (rect == null)
                continue;
            NativeStates[tile.GetInstanceID()] = new NativeTileState
            {
                Object = tile,
                Position = rect.anchoredPosition,
                Active = tile.activeSelf
            };
        }

        if (native.Count != NativeTileCount)
            return;
        var orders = PositionOf(native[0]);
        var store = PositionOf(native[1]);
        var management = PositionOf(native[2]);
        var payments = PositionOf(native[3]);
        var employee = PositionOf(native[4]);
        var horizontalStep = store.x - orders.x;
        if (Math.Abs(horizontalStep) < 20f)
            horizontalStep = employee.x - payments.x;
        _slotPositions = new[]
        {
            orders,
            store,
            management,
            payments,
            employee,
            employee + new Vector2(horizontalStep, 0f)
        };
        if (_view?._employeeUnavailableObject != null)
        {
            var unavailableRect = _view._employeeUnavailableObject.GetComponent<RectTransform>();
            if (unavailableRect != null)
            {
                _employeeUnavailablePosition = unavailableRect.anchoredPosition;
                _employeeUnavailableActive = _view._employeeUnavailableObject.activeSelf;
                _employeeUnavailableCaptured = true;
            }
        }
    }

    private static List<GameObject> GetNativeTiles()
    {
        var result = new List<GameObject>(NativeTileCount);
        Add(result, _view?._ordersButton?.gameObject);
        Add(result, _view?._storeButton?.gameObject);
        Add(result, _view?._upgradeButton?.gameObject);
        Add(result, _view?._paymentsButton?.gameObject);
        Add(result, _view?._employeeButton?.gameObject);
        return result;
    }

    private static void SynchronizeTiles()
    {
        _registryDirty = false;
        if (_view == null || _menuObject == null || _slotPositions.Length != VisibleSlots)
            return;

        foreach (var runtime in RuntimeTiles.Values)
            DestroyRuntimeTile(runtime);
        RuntimeTiles.Clear();

        foreach (var slot in WolfModRegistry.GetTerminalSlots())
        {
            if (slot.Registration == null)
                continue;
            try
            {
                RuntimeTiles[slot.TileId] = CreateRuntimeTile(slot.Registration);
            }
            catch (Exception exception)
            {
                MelonLogger.Error($"WolfCore: плитка '{slot.TileId}' не создана: {exception}");
            }
        }

        EnsurePageButton();
        ApplyPage();
    }

    private static RuntimeTile CreateRuntimeTile(WolfTerminalPageRegistration registration)
    {
        if (_view?._employeeButton == null || _menuObject == null)
            throw new InvalidOperationException("нативный шаблон терминала недоступен");
        var clone = UnityEngine.Object.Instantiate(_view._employeeButton.gameObject, _menuObject.transform, false);
        clone.name = "WolfMenu_TerminalTile_" + registration.TileId;
        clone.hideFlags = HideFlags.DontSave;
        clone.SetActive(false);

        var button = clone.GetComponent<Button>() ??
                     throw new InvalidOperationException("штатная плитка не содержит Button");
        var action = DelegateSupport.ConvertDelegate<UnityAction>(
            new Action(() => OpenRegisteredPage(registration.TileId))) ??
                     throw new InvalidOperationException("не удалось создать обработчик плитки");
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(action);
        button.interactable = true;
        button.enabled = true;

        var label = clone.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null)
            label.text = registration.Label;

        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
        {
            name = "WolfMenu_TerminalTexture_" + registration.TileId,
            hideFlags = HideFlags.DontSave,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        var bytes = new Il2CppStructArray<byte>(registration.TilePng);
        if (!ImageConversion.LoadImage(texture, bytes, false))
        {
            UnityEngine.Object.Destroy(texture);
            UnityEngine.Object.Destroy(clone);
            throw new InvalidOperationException("Unity не смогла прочитать PNG плитки");
        }
        var sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f), 100f);
        sprite.name = "WolfMenu_TerminalSprite_" + registration.TileId;
        sprite.hideFlags = HideFlags.DontSave;
        var image = button.image ?? clone.GetComponent<Image>();
        if (image != null)
        {
            image.sprite = sprite;
            image.color = Color.white;
            image.preserveAspect = false;
            image.raycastTarget = true;
            button.image = image;
            button.targetGraphic = image;
        }

        CopyRect(_view._employeeButton.GetComponent<RectTransform>(), clone.GetComponent<RectTransform>());
        clone.transform.SetAsLastSibling();
        return new RuntimeTile
        {
            Registration = registration,
            ButtonObject = clone,
            Texture = texture,
            Sprite = sprite,
            ClickAction = action
        };
    }

    private static void ApplyPage()
    {
        if (_view == null || _slotPositions.Length != VisibleSlots)
            return;
        var terminalSlots = WolfModRegistry.GetTerminalSlots();
        var logicalCount = NativeTileCount + terminalSlots.Count;
        var maximumPage = Math.Max(0, (logicalCount - 4) / Columns);
        _page = Math.Clamp(_page, 0, maximumPage);

        foreach (var state in NativeStates.Values)
            state.Object.SetActive(false);
        foreach (var runtime in RuntimeTiles.Values)
            runtime.ButtonObject.SetActive(false);

        var logical = new List<(GameObject? Object, bool Active)>(logicalCount);
        foreach (var native in GetNativeTiles())
        {
            var state = NativeStates.GetValueOrDefault(native.GetInstanceID());
            logical.Add((native, state?.Active == true));
        }
        foreach (var slot in terminalSlots)
        {
            if (slot.Registration != null && RuntimeTiles.TryGetValue(slot.TileId, out var runtime))
                logical.Add((runtime.ButtonObject, WolfModRegistry.IsEnabled(slot.Registration.ModId)));
            else
                logical.Add((null, false));
        }

        var start = _page * Columns;
        for (var visualIndex = 0; visualIndex < VisibleSlots; visualIndex++)
        {
            var logicalIndex = start + visualIndex;
            if (logicalIndex < 0 || logicalIndex >= logical.Count)
                continue;
            var item = logical[logicalIndex];
            if (item.Object == null)
                continue;
            var rect = item.Object.GetComponent<RectTransform>();
            if (rect != null)
                rect.anchoredPosition = _slotPositions[visualIndex];
            item.Object.SetActive(item.Active);
        }

        UpdateEmployeeUnavailableOverlay(start);
        UpdatePageButton(maximumPage);
    }

    private static void UpdateEmployeeUnavailableOverlay(int logicalStart)
    {
        if (_view?._employeeUnavailableObject == null || _view._employeeButton == null)
            return;
        var overlay = _view._employeeUnavailableObject;
        if (overlay.transform.IsChildOf(_view._employeeButton.transform))
            return;
        var employeeLogicalIndex = 4;
        var visualIndex = employeeLogicalIndex - logicalStart;
        var state = NativeStates.GetValueOrDefault(_view._employeeButton.gameObject.GetInstanceID());
        if (state == null || visualIndex < 0 || visualIndex >= VisibleSlots)
        {
            overlay.SetActive(false);
            return;
        }
        var rect = overlay.GetComponent<RectTransform>();
        if (rect != null && _employeeUnavailableCaptured)
            rect.anchoredPosition = _employeeUnavailablePosition + (_slotPositions[visualIndex] - state.Position);
        overlay.SetActive(_employeeUnavailableActive);
    }

    private static void EnsurePageButton()
    {
        if (_pageButtonObject != null || _menuObject == null || _view?._employeeButton == null)
            return;

        var root = new GameObject(
            PageButtonName,
            Il2CppType.Of<RectTransform>(),
            Il2CppType.Of<Image>(),
            Il2CppType.Of<Button>())
        {
            hideFlags = HideFlags.DontSave
        };
        root.SetActive(false);
        // The native menu object is controlled by its layout group. Any direct
        // child is forcibly resized and positioned like a terminal tile, even
        // when its RectTransform has fixed anchors. Keep page navigation on the
        // terminal content layer instead, as a sibling of the tile grid.
        root.transform.SetParent(_view._content, false);
        var rect = root.GetComponent<RectTransform>();
        var employeeRect = _view._employeeButton.GetComponent<RectTransform>();
        if (rect != null && employeeRect != null)
        {
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(380f, 58f);
            rect.anchoredPosition = new Vector2(0f, 46f);
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
        }
        var image = root.GetComponent<Image>();
        image.color = new Color(0.20f, 0.22f, 0.36f, 0.94f);
        image.raycastTarget = true;
        var button = root.GetComponent<Button>();
        _pageButtonAction ??= DelegateSupport.ConvertDelegate<UnityAction>(new Action(NextPage));
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(_pageButtonAction);
        button.targetGraphic = image;

        var labelObject = new GameObject(
            "Label",
            Il2CppType.Of<RectTransform>(),
            Il2CppType.Of<TextMeshProUGUI>());
        labelObject.transform.SetParent(root.transform, false);
        var labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(8f, 4f);
        labelRect.offsetMax = new Vector2(-8f, -4f);
        var label = labelObject.GetComponent<TextMeshProUGUI>();
        var nativeLabel = _view._employeeButton.GetComponentInChildren<TextMeshProUGUI>(true);
        if (nativeLabel != null)
            label.font = nativeLabel.font;
        label.text = "‹  ЭКРАН 1 / 1  ›";
        label.fontSize = 26f;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.raycastTarget = false;
        _pageButtonObject = root;
        _pageButtonLabel = label;
        root.transform.SetAsLastSibling();
    }

    private static void UpdatePageButton(int maximumPage)
    {
        if (_pageButtonObject == null || _pageButtonLabel == null)
            return;
        var visible = maximumPage > 0 && _menuObject != null && _menuObject.activeInHierarchy;
        _pageButtonLabel.text = $"‹  ЭКРАН {_page + 1} / {maximumPage + 1}  ›";
        _pageButtonObject.SetActive(visible);
    }

    private static void TickPageNavigation()
    {
        var slots = WolfModRegistry.GetTerminalSlots();
        var maximumPage = Math.Max(0, (NativeTileCount + slots.Count - 4) / Columns);
        if (maximumPage <= 0)
            return;
        try
        {
            var wheel = Input.mouseScrollDelta.y;
            if (wheel < -0.01f && _page < maximumPage)
            {
                _page++;
                ApplyPage();
            }
            else if (wheel > 0.01f && _page > 0)
            {
                _page--;
                ApplyPage();
            }
        }
        catch (Exception exception)
        {
            MelonLogger.Warning($"WolfCore: прокрутка терминала недоступна: {exception.Message}");
        }
    }

    private static void NextPage()
    {
        var slots = WolfModRegistry.GetTerminalSlots();
        var maximumPage = Math.Max(0, (NativeTileCount + slots.Count - 4) / Columns);
        if (maximumPage <= 0)
            return;
        _page = (_page + 1) % (maximumPage + 1);
        ApplyPage();
    }

    private static void OpenRegisteredPage(string tileId)
    {
        if (_view == null || _menuObject == null || !_menuObject.activeInHierarchy ||
            !RuntimeTiles.TryGetValue(tileId, out var runtime) ||
            !WolfModRegistry.IsEnabled(runtime.Registration.ModId))
            return;
        try
        {
            _pageButtonObject?.SetActive(false);
            _menuObject.SetActive(false);
            _view.ShowSection(true);
            if (!runtime.Registration.OpenPage(_view))
            {
                _view.ShowSection(false);
                _menuObject.SetActive(true);
                ApplyPage();
                return;
            }
            _activePage = runtime.Registration;
            RewireNativeBackButton();
        }
        catch (Exception exception)
        {
            MelonLogger.Error($"WolfCore: страница '{runtime.Registration.Label}' не открыта: {exception}");
            try { runtime.Registration.ClosePage(); } catch { }
            try { _view.ShowSection(false); } catch { }
            _menuObject.SetActive(true);
            ApplyPage();
        }
    }

    private static void CloseActivePage()
    {
        var page = _activePage;
        _activePage = null;
        _closeRequested = false;
        RestoreNativeBackButton();
        if (page != null)
        {
            try { page.ClosePage(); }
            catch (Exception exception)
            {
                MelonLogger.Warning($"WolfCore: страница '{page.Label}' закрыта не полностью: {exception.Message}");
            }
        }
        try
        {
            _view?.ShowSection(false);
            _menuObject?.SetActive(true);
            ApplyPage();
        }
        catch (Exception exception)
        {
            MelonLogger.Warning($"WolfCore: главный экран терминала восстановлен не полностью: {exception.Message}");
        }
    }

    private static bool SafeIsPageOpen(WolfTerminalPageRegistration page)
    {
        try { return page.IsPageOpen(); }
        catch { return false; }
    }

    private static void RewireNativeBackButton()
    {
        if (_view?._closeButton == null || _backRewired)
            return;
        _originalBackEvent = _view._closeButton.onClick;
        _backAction ??= DelegateSupport.ConvertDelegate<UnityAction>(new Action(() => _closeRequested = true));
        var replacement = new Button.ButtonClickedEvent();
        replacement.AddListener(_backAction);
        _view._closeButton.onClick = replacement;
        _backRewired = true;
    }

    private static void RestoreNativeBackButton()
    {
        if (!_backRewired)
            return;
        try
        {
            if (_view?._closeButton != null && _originalBackEvent != null)
                _view._closeButton.onClick = _originalBackEvent;
        }
        catch { }
        _originalBackEvent = null;
        _backRewired = false;
    }

    public static void OnSceneChanged()
    {
        ResetCurrentView();
        _nextDiscoveryTime = 0f;
    }

    public static void Shutdown()
    {
        ResetCurrentView();
        _pageButtonAction = null;
        _backAction = null;
    }

    private static void ResetCurrentView()
    {
        if (_activePage != null)
            CloseActivePage();
        RestoreNativeLayout();
        foreach (var runtime in RuntimeTiles.Values)
            DestroyRuntimeTile(runtime);
        RuntimeTiles.Clear();
        if (_pageButtonObject != null)
        {
            _pageButtonObject.SetActive(false);
            UnityEngine.Object.Destroy(_pageButtonObject);
        }
        _pageButtonObject = null;
        _pageButtonLabel = null;
        NativeStates.Clear();
        _slotPositions = Array.Empty<Vector2>();
        _employeeUnavailableCaptured = false;
        _page = 0;
        _view = null;
        _menuObject = null;
        _registryDirty = true;
    }

    private static void RestoreNativeLayout()
    {
        foreach (var state in NativeStates.Values)
        {
            if (state.Object == null)
                continue;
            var rect = state.Object.GetComponent<RectTransform>();
            if (rect != null)
                rect.anchoredPosition = state.Position;
            state.Object.SetActive(state.Active);
        }
        if (_view?._employeeUnavailableObject != null && _employeeUnavailableCaptured)
        {
            var rect = _view._employeeUnavailableObject.GetComponent<RectTransform>();
            if (rect != null)
                rect.anchoredPosition = _employeeUnavailablePosition;
            _view._employeeUnavailableObject.SetActive(_employeeUnavailableActive);
        }
    }

    private static void DestroyRuntimeTile(RuntimeTile runtime)
    {
        if (runtime.ButtonObject != null)
        {
            runtime.ButtonObject.SetActive(false);
            UnityEngine.Object.Destroy(runtime.ButtonObject);
        }
        if (runtime.Sprite != null)
            UnityEngine.Object.Destroy(runtime.Sprite);
        if (runtime.Texture != null)
            UnityEngine.Object.Destroy(runtime.Texture);
    }

    private static Vector2 PositionOf(GameObject item) =>
        NativeStates.TryGetValue(item.GetInstanceID(), out var state) ? state.Position : Vector2.zero;

    private static void CopyRect(RectTransform? source, RectTransform? target)
    {
        if (source == null || target == null)
            return;
        target.anchorMin = source.anchorMin;
        target.anchorMax = source.anchorMax;
        target.pivot = source.pivot;
        target.sizeDelta = source.sizeDelta;
        target.localScale = source.localScale;
        target.localRotation = source.localRotation;
    }

    private static void Add(ICollection<GameObject> target, GameObject? item)
    {
        if (item != null)
            target.Add(item);
    }
}
