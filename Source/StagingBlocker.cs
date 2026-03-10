using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;

#pragma warning disable CS8618, CS8600, CS8601, CS8625

// ─────────────────────────────────────────────────────────────────────────────
//  SBLunaHelper — optional detection of LunaMultiplayer (LMP)
//
//  Uses reflection so there is no hard dependency on LMP assemblies.
//  The modifier hotkey is stored globally (no player prefix) in
//  StagingBlockerScenario, so KSP/LMP scenario-sync automatically propagates
//  the same key to every connected player — no additional work needed.
// ─────────────────────────────────────────────────────────────────────────────
public static class SBLunaHelper
{
    private static bool? _isLunaAvailable = null;
    private static string _cachedPlayerName = null;

    public static bool IsLunaEnabled
    {
        get
        {
            if (_isLunaAvailable == null)
                _isLunaAvailable = DetectLuna();
            return _isLunaAvailable.Value;
        }
    }

    private static bool DetectLuna()
    {
        try
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                string name = a.GetName().Name;
                if (name.Contains("LunaMultiplayer") || name == "LMP.Client")
                    return true;
            }
        }
        catch { }
        return false;
    }

    /// <summary>Returns the LMP player name, or "SinglePlayer" when LMP is absent.</summary>
    public static string GetCurrentPlayerName()
    {
        if (_cachedPlayerName != null) return _cachedPlayerName;
        try
        {
            if (IsLunaEnabled)
            {
                // Probe common LMP type/property paths without a hard reference
                string[] typeNames =
                {
                    "LunaClient.Systems.PlayerConnection.PlayerConnectionSystem",
                    "LMP.Client.Systems.PlayerConnection.PlayerConnectionSystem"
                };
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    foreach (var typeName in typeNames)
                    {
                        var t = a.GetType(typeName);
                        if (t == null) continue;
                        var singleton = t.GetProperty("Singleton",
                            BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                        if (singleton == null) continue;
                        var playerName = singleton.GetType()
                            .GetProperty("PlayerName")?.GetValue(singleton) as string;
                        if (playerName != null)
                        {
                            _cachedPlayerName = playerName;
                            return playerName;
                        }
                    }
                }
            }
        }
        catch { }
        _cachedPlayerName = "SinglePlayer";
        return _cachedPlayerName;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  StagingBlockerScenario — persists window state and per-vessel settings
//
//  Both the modifier key and the staging-block toggle are stored per vessel ID
//  (no player prefix) so that every player in a LunaMultiplayer session shares
//  the same state for each vessel via KSP/LMP automatic scenario sync.
// ─────────────────────────────────────────────────────────────────────────────
[KSPScenario(ScenarioCreationOptions.AddToAllGames, new GameScenes[] { GameScenes.FLIGHT })]
public class StagingBlockerScenario : ScenarioModule
{
    public static StagingBlockerScenario Instance;

    public bool showWindow = true;

    // vessel ID (string) → KeyCode name (string)
    public readonly Dictionary<string, string> vesselModifierKeys   = new Dictionary<string, string>();
    // vessel ID (string) → staging-block enabled (bool stored as string)
    public readonly Dictionary<string, bool>   vesselStagingBlocked = new Dictionary<string, bool>();

    private const KeyCode DEFAULT_KEY     = KeyCode.BackQuote; // tilde ~
    private const string  KEY_PREFIX      = "vesselKey_";
    private const string  BLOCKED_PREFIX  = "vesselBlocked_";

    public KeyCode GetModifierKey(string vesselId)
    {
        if (vesselId != null && vesselModifierKeys.TryGetValue(vesselId, out string keyStr))
        {
            try { return (KeyCode)Enum.Parse(typeof(KeyCode), keyStr); }
            catch { }
        }
        return DEFAULT_KEY;
    }

    public void SetModifierKey(string vesselId, KeyCode key)
    {
        if (vesselId != null)
            vesselModifierKeys[vesselId] = key.ToString();
    }

    public bool GetStagingBlocked(string vesselId)
    {
        if (vesselId != null && vesselStagingBlocked.TryGetValue(vesselId, out bool blocked))
            return blocked;
        return true; // default: staging is blocked
    }

    public void SetStagingBlocked(string vesselId, bool blocked)
    {
        if (vesselId != null)
            vesselStagingBlocked[vesselId] = blocked;
    }

    public override void OnAwake()
    {
        base.OnAwake();
        Instance = this;
    }

    public override void OnSave(ConfigNode node)
    {
        base.OnSave(node);
        node.SetValue("showWindow", showWindow.ToString(), true);
        foreach (var kv in vesselModifierKeys)
            node.AddValue(KEY_PREFIX + kv.Key, kv.Value);
        foreach (var kv in vesselStagingBlocked)
            node.AddValue(BLOCKED_PREFIX + kv.Key, kv.Value.ToString());
    }

    public override void OnLoad(ConfigNode node)
    {
        base.OnLoad(node);
        if (node.HasValue("showWindow"))
            bool.TryParse(node.GetValue("showWindow"), out showWindow);

        vesselModifierKeys.Clear();
        vesselStagingBlocked.Clear();
        foreach (ConfigNode.Value val in node.values)
        {
            if (val.name.StartsWith(KEY_PREFIX))
            {
                vesselModifierKeys[val.name.Substring(KEY_PREFIX.Length)] = val.value;
            }
            else if (val.name.StartsWith(BLOCKED_PREFIX))
            {
                if (bool.TryParse(val.value, out bool b))
                    vesselStagingBlocked[val.name.Substring(BLOCKED_PREFIX.Length)] = b;
            }
        }
        Debug.Log("[StagingBlocker] Scenario loaded — " + vesselModifierKeys.Count + " vessel key(s), "
                  + vesselStagingBlocked.Count + " blocked state(s)");
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  StagingBlockerFlight — runs in Flight scene
//  Responsibilities:
//    • Block spacebar staging unless modifier key is held
//    • Provide STAGING MANAGER GUI with manual stage trigger
//    • Stock AppLauncher integration
// ─────────────────────────────────────────────────────────────────────────────
[KSPAddon(KSPAddon.Startup.Flight, false)]
public class StagingBlockerFlight : MonoBehaviour
{
    private static StagingBlockerFlight _activeInstance = null;

    // ── Window / UI state ─────────────────────────────────────────────────────
    private Rect windowRect = new Rect(20, 80, 310, 270);
    private bool showWindow = true;
    private bool isSettingKey = false;

    // ── Window resize ─────────────────────────────────────────────────────────
    private bool _isResizing = false;
    private ResizeEdges _resizeEdge = ResizeEdges.None;
    private Vector2 _resizeDragStart;
    private Rect _resizeOrigRect;
    private const float RESIZE_BORDER = 8f;
    private const float MIN_WIDTH  = 310f;
    private const float MIN_HEIGHT = 270f;

    [System.Flags]
    private enum ResizeEdges { None = 0, Left = 1, Right = 2, Top = 4, Bottom = 8 }

    // ── Modifier key (held while pressing SPACE to allow staging) ─────────────
    private KeyCode modifierKey = KeyCode.BackQuote; // default: ~ (tilde)

    // ── GUI styles (created once after GUI system initialises) ────────────────
    private GUIStyle _redButtonStyle;
    private GUIStyle _labelCenterStyle;
    private bool _stylesReady = false;

    // ── Stock AppLauncher button ───────────────────────────────────────────────
    private object _appButton = null;
    private bool _isAddingAppButton = false;
    private Coroutine _retryButtonCoroutine = null;
    
    // ── Input lock ID ─────────────────────────────────────────────────────────
    private const string LOCK_ID = "StagingBlocker_SpaceLock";

    // ── Staging block toggle (global) ─────────────────────────────────────────
    private bool _stagingBlocked = true;

    // ── Active vessel tracking (for per-vessel key load/save) ─────────────────
    private string _currentVesselId = null;
    
    // ── Cached reflection handles for StageManager ────────────────────────────
    private static MethodInfo _activateNextStageMethod = null;
    private static bool _stagingMethodSearched = false;

    // ─────────────────────────────────────────────────────────────────────────
    void Awake()
    {
        // Defensive singleton guard. If multiple flight addons are instantiated,
        // each one could register its own AppLauncher button.
        if (_activeInstance != null && _activeInstance != this)
        {
            Debug.LogWarning("[StagingBlocker] Duplicate flight addon instance detected; destroying duplicate");
            Destroy(this);
            return;
        }
        _activeInstance = this;
    }

    // ─────────────────────────────────────────────────────────────────────────
    void Start()
    {
        // Awake() may have called Destroy(this) if a duplicate was detected, but Unity still
        // invokes Start() on the same frame. Abort before subscribing to any events.
        if (_activeInstance != this)
        {
            Debug.LogWarning("[StagingBlocker] Start() called on non-active duplicate instance; aborting.");
            return;
        }

        Debug.Log("[StagingBlocker] Flight addon started");

        // Log multiplayer status
        if (SBLunaHelper.IsLunaEnabled)
        {
            Debug.Log("[StagingBlocker] LunaMultiplayer detected — modifier key is shared across all " +
                      "players via scenario storage (player: " + SBLunaHelper.GetCurrentPlayerName() + ")");
        }
        else
        {
            Debug.Log("[StagingBlocker] Single-player mode");
        }

        // Load persisted settings — showWindow is global; modifier key and staging-block are per-vessel.
        // All LMP players share the same per-vessel state via KSP/LMP scenario sync.
        var scen = StagingBlockerScenario.Instance;
        if (scen != null)
            showWindow = scen.showWindow;

        // Subscribe to vessel change so we reload the key whenever the active vessel switches
        GameEvents.onVesselChange.Add(OnVesselChange);

        // Load per-vessel state for the vessel that is active right now
        LoadVesselState(FlightGlobals.ActiveVessel);

        // Apply (or skip) the staging lock based on the persisted toggle state
        ApplyStagingLock(_stagingBlocked);

        // Stock toolbar button
        GameEvents.onGUIApplicationLauncherReady.Add(OnGUIAppLauncherReady);
        _retryButtonCoroutine = StartCoroutine(RetryAppLauncherButton());
    }

    // ─────────────────────────────────────────────────────────────────────────
    void OnVesselChange(Vessel v)
    {
        try
        {
            SaveToScenario();                        // persist the outgoing vessel's state first
            LoadVesselState(v);                      // load the incoming vessel's state
            ApplyStagingLock(_stagingBlocked);       // apply the new vessel's block toggle
            Debug.Log("[StagingBlocker] Vessel switch complete");
        }
        catch (Exception e)
        {
            Debug.LogWarning("[StagingBlocker] Error during vessel change: " + e.Message);
        }
    }

    void LoadVesselState(Vessel v)
    {
        if (v == null) return;
        _currentVesselId = v.id.ToString();
        var scen = StagingBlockerScenario.Instance;
        if (scen != null)
        {
            modifierKey     = scen.GetModifierKey(_currentVesselId);
            _stagingBlocked = scen.GetStagingBlocked(_currentVesselId);
        }
        else
        {
            modifierKey     = KeyCode.BackQuote;
            _stagingBlocked = true;
        }
        Debug.Log("[StagingBlocker] Loaded vessel \"" + v.vesselName
                  + "\": key=" + modifierKey + ", blocked=" + _stagingBlocked);
    }

    void ApplyStagingLock(bool block)
    {
        _stagingBlocked = block;
        if (block)
        {
            InputLockManager.SetControlLock(ControlTypes.STAGING, LOCK_ID);
            Debug.Log("[StagingBlocker] Staging lock ON — SPACE requires modifier: " + modifierKey);
        }
        else
        {
            InputLockManager.RemoveControlLock(LOCK_ID);
            Debug.Log("[StagingBlocker] Staging lock OFF — SPACE acts normally");
        }
        SaveToScenario();
    }

    // ─────────────────────────────────────────────────────────────────────────
    void Update()
    {
        // Fallback hotkey to toggle window visibility (All 4 arrow keys)
        // This is a safety net if the toolbar button fails to respond
        if (Input.GetKeyDown(KeyCode.UpArrow) && Input.GetKey(KeyCode.DownArrow) 
            && Input.GetKey(KeyCode.LeftArrow) && Input.GetKey(KeyCode.RightArrow))
        {
            SetWindowVisible(!showWindow);
            Debug.Log("[StagingBlocker] Window toggled via fallback hotkey (All 4 arrow keys): " + showWindow);
            return;
        }

        // While waiting for key-bind input, capture next keypress
        if (isSettingKey)
        {
            foreach (KeyCode kc in Enum.GetValues(typeof(KeyCode)))
            {
                if (Input.GetKeyDown(kc) && kc != KeyCode.Escape && kc != KeyCode.Mouse0
                    && kc != KeyCode.Mouse1 && kc != KeyCode.Mouse2)
                {
                    modifierKey = kc;
                    isSettingKey = false;

                    // Re-log the new modifier
                    Debug.Log("[StagingBlocker] Modifier key changed to: " + modifierKey);
                    SaveToScenario();
                    return;
                }
            }
            if (Input.GetKeyDown(KeyCode.Escape))
                isSettingKey = false;
            return; // don't process staging while capturing
        }

        // When blocking is active, allow staging only with modifier + SPACE
        if (_stagingBlocked && Input.GetKey(modifierKey) && Input.GetKeyDown(KeyCode.Space))
        {
            TriggerNextStage();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    void TriggerNextStage()
    {
        try
        {
            if (FlightGlobals.ActiveVessel == null) return;

            // Resolve StageManager.ActivateNextStage() once via reflection
            if (!_stagingMethodSearched)
            {
                _stagingMethodSearched = true;
                string[] typeNames = { "StageManager", "KSP.UI.Screens.StageManager", "Staging" };
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    foreach (var typeName in typeNames)
                    {
                        var t = a.GetType(typeName);
                        if (t == null) continue;
                        var m = t.GetMethod("ActivateNextStage",
                            BindingFlags.Public | BindingFlags.Static);
                        if (m != null) { _activateNextStageMethod = m; break; }
                    }
                    if (_activateNextStageMethod != null) break;
                }
                Debug.Log("[StagingBlocker] ActivateNextStage resolved: " + (_activateNextStageMethod != null));
            }

            if (_activateNextStageMethod != null)
            {
                _activateNextStageMethod.Invoke(null, null);
                Debug.Log("[StagingBlocker] Stage activated");
            }
            else
            {
                Debug.LogWarning("[StagingBlocker] StageManager.ActivateNextStage not found");
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[StagingBlocker] TriggerNextStage failed: " + e.Message);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  GUI
    // ─────────────────────────────────────────────────────────────────────────
    void OnGUI()
    {
        if (!showWindow) return;
        EnsureStyles();
        HandleResize(); // must run before Window so resize events take priority over drag
        windowRect = GUILayout.Window(
            GetInstanceID(), windowRect, DrawWindow,
            "STAGING MANAGER",
            GUILayout.Width(windowRect.width),
            GUILayout.Height(windowRect.height));
    }

    void EnsureStyles()
    {
        if (_stylesReady) return;

        _redButtonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 16,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        _redButtonStyle.normal.textColor  = Color.white;
        _redButtonStyle.hover.textColor   = Color.white;
        _redButtonStyle.active.textColor  = Color.yellow;

        var redNorm  = MakeTex(new Color(0.75f, 0.10f, 0.10f));
        var redHover = MakeTex(new Color(0.95f, 0.20f, 0.20f));
        var redPress = MakeTex(new Color(0.55f, 0.05f, 0.05f));
        _redButtonStyle.normal.background  = redNorm;
        _redButtonStyle.hover.background   = redHover;
        _redButtonStyle.active.background  = redPress;

        _labelCenterStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize  = 13,
            fontStyle = FontStyle.Bold
        };

        _stylesReady = true;
    }

    static Texture2D MakeTex(Color col)
    {
        var t = new Texture2D(4, 4);
        var pixels = new Color[16];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = col;
        t.SetPixels(pixels);
        t.Apply();
        return t;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Resize handling
    // ─────────────────────────────────────────────────────────────────────────
    void HandleResize()
    {
        var e = Event.current;
        if (e == null) return;

        if (e.type == EventType.MouseDown && e.button == 0 && !_isResizing)
        {
            _resizeEdge = GetResizeEdge(e.mousePosition);
            if (_resizeEdge != ResizeEdges.None)
            {
                _isResizing = true;
                _resizeDragStart = e.mousePosition;
                _resizeOrigRect  = windowRect;
                e.Use();
            }
        }
        else if (e.type == EventType.MouseDrag && _isResizing)
        {
            ApplyResize(e.mousePosition);
            e.Use();
        }
        else if (e.type == EventType.MouseUp && _isResizing)
        {
            _isResizing = false;
            _resizeEdge = ResizeEdges.None;
            e.Use();
        }
    }

    ResizeEdges GetResizeEdge(Vector2 mouse)
    {
        // Quick reject: outside the window + border zone entirely
        if (mouse.x < windowRect.x       - RESIZE_BORDER ||
            mouse.x > windowRect.xMax    + RESIZE_BORDER ||
            mouse.y < windowRect.y       - RESIZE_BORDER ||
            mouse.y > windowRect.yMax    + RESIZE_BORDER)
            return ResizeEdges.None;

        bool nearLeft   = mouse.x < windowRect.x    + RESIZE_BORDER;
        bool nearRight  = mouse.x > windowRect.xMax - RESIZE_BORDER;
        bool nearTop    = mouse.y < windowRect.y    + RESIZE_BORDER;
        bool nearBottom = mouse.y > windowRect.yMax - RESIZE_BORDER;

        // Not near any edge → interior click, nothing to do
        if (!nearLeft && !nearRight && !nearTop && !nearBottom)
            return ResizeEdges.None;

        ResizeEdges edges = ResizeEdges.None;
        if (nearLeft)   edges |= ResizeEdges.Left;
        if (nearRight)  edges |= ResizeEdges.Right;
        if (nearTop)    edges |= ResizeEdges.Top;
        if (nearBottom) edges |= ResizeEdges.Bottom;

        // Top-only would fight with title-bar dragging; only allow it as part of a corner
        if (edges == ResizeEdges.Top) return ResizeEdges.None;

        return edges;
    }

    void ApplyResize(Vector2 mouse)
    {
        Vector2 delta = mouse - _resizeDragStart;
        Rect r = _resizeOrigRect;

        if ((_resizeEdge & ResizeEdges.Right) != 0)
            r.width = Mathf.Max(MIN_WIDTH, _resizeOrigRect.width + delta.x);

        if ((_resizeEdge & ResizeEdges.Bottom) != 0)
            r.height = Mathf.Max(MIN_HEIGHT, _resizeOrigRect.height + delta.y);

        if ((_resizeEdge & ResizeEdges.Left) != 0)
        {
            float w = Mathf.Max(MIN_WIDTH, _resizeOrigRect.width - delta.x);
            r.x     = _resizeOrigRect.xMax - w;
            r.width = w;
        }

        if ((_resizeEdge & ResizeEdges.Top) != 0)
        {
            float h  = Mathf.Max(MIN_HEIGHT, _resizeOrigRect.height - delta.y);
            r.y      = _resizeOrigRect.yMax - h;
            r.height = h;
        }

        windowRect = r;
    }

    // Show/hide the window and keep the AppLauncher button in sync
    void SetWindowVisible(bool visible)
    {
        showWindow = visible;
        SaveToScenario();
        if (_appButton != null)
        {
            try
            {
                _appButton.GetType()
                    .GetMethod(visible ? "SetTrue" : "SetFalse",
                               BindingFlags.Public | BindingFlags.Instance)
                    ?.Invoke(_appButton, null);
            }
            catch { }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    void DrawWindow(int id)
    {
        // ── X (close) button — top-right of title bar ────────────────────────
        const float CLOSE_W = 18f;
        const float CLOSE_H = 16f;
        var prevBg = GUI.backgroundColor;
        GUI.backgroundColor = new Color(0.75f, 0.12f, 0.12f);
        if (GUI.Button(new Rect(windowRect.width - CLOSE_W - 4f, 2f, CLOSE_W, CLOSE_H), "×"))
            SetWindowVisible(false);
        GUI.backgroundColor = prevBg;

        GUILayout.BeginVertical();

        // ── Fallback hotkey info (small text) ──────────────────────────────
        GUILayout.BeginHorizontal();
        var smallLabelStyle = new GUIStyle(GUI.skin.label) { fontSize = 10 };
        GUILayout.Label("(All 4 arrow keys to toggle window)", smallLabelStyle);
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        var vessel = FlightGlobals.ActiveVessel;

        // ── Stage info ───────────────────────────────────────────────────────
        GUILayout.Space(4);

        if (vessel != null)
        {
            int nextToFire = vessel.currentStage;          // stage that fires on next STAGE press
            int followingStage = Mathf.Max(0, nextToFire - 1);

            GUILayout.BeginVertical("box");
            GUILayout.Label("Current Stage:   " + nextToFire,   _labelCenterStyle);
            GUILayout.Label("Next to Trigger: Stage " + followingStage, _labelCenterStyle);
            GUILayout.EndVertical();

            GUILayout.Space(6);

            // ── Big red STAGE button ─────────────────────────────────────────
            if (GUILayout.Button("▶  ACTIVATE STAGE " + nextToFire, _redButtonStyle, GUILayout.Height(52)))
            {
                TriggerNextStage();
            }
        }
        else
        {
            GUILayout.BeginVertical("box");
            GUILayout.Label("No active vessel", _labelCenterStyle);
            GUILayout.EndVertical();
            GUILayout.Space(6);
            GUILayout.Button("▶  ACTIVATE STAGE", _redButtonStyle, GUILayout.Height(52));
        }

        GUILayout.Space(8);

        // ── Spacebar block toggle + modifier key ────────────────────────────
        GUILayout.BeginVertical("box");

        // Toggle row
        GUILayout.BeginHorizontal();
        GUILayout.Label("Modifier Key Required:", GUILayout.Width(150));
        string toggleLabel = _stagingBlocked ? "ENABLED" : "DISABLED";
        Color prevColor = GUI.backgroundColor;
        GUI.backgroundColor = _stagingBlocked ? new Color(0.7f, 0.1f, 0.1f) : new Color(0.1f, 0.55f, 0.1f);
        if (GUILayout.Button(toggleLabel, GUILayout.Width(90)))
            ApplyStagingLock(!_stagingBlocked);
        GUI.backgroundColor = prevColor;
        GUILayout.EndHorizontal();

        if (_stagingBlocked)
        {
            GUILayout.Label(">>Hold Modifier Key + Press SPACE to STAGE<<", GUI.skin.label);
            GUILayout.Space(2);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Modifier Key:", GUILayout.Width(100));
            if (isSettingKey)
            {
                GUILayout.Label("[ Press a key... ]", GUILayout.Width(130));
                if (GUILayout.Button("Cancel", GUILayout.Width(60)))
                    isSettingKey = false;
            }
            else
            {
                GUILayout.Label(ModifierKeyDisplayName(modifierKey), GUILayout.Width(100));
                if (GUILayout.Button("Change", GUILayout.Width(70)))
                    isSettingKey = true;
            }
            GUILayout.EndHorizontal();
        }

        GUILayout.EndVertical();

        GUILayout.EndVertical();
        // Drag zone: title bar only, excluding the X button and side resize borders
        GUI.DragWindow(new Rect(RESIZE_BORDER, 0f, windowRect.width - CLOSE_W - RESIZE_BORDER - 8f, 20f));
    }

    static string ModifierKeyDisplayName(KeyCode kc)
    {
        switch (kc)
        {
            case KeyCode.BackQuote:    return "~ (Tilde)";
            case KeyCode.LeftControl:  return "Left Ctrl";
            case KeyCode.RightControl: return "Right Ctrl";
            case KeyCode.LeftAlt:      return "Left Alt";
            case KeyCode.RightAlt:     return "Right Alt";
            case KeyCode.LeftShift:    return "Left Shift";
            case KeyCode.RightShift:   return "Right Shift";
            default:                   return kc.ToString();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Persistence
    // ─────────────────────────────────────────────────────────────────────────
    void SaveToScenario()
    {
        var scen = StagingBlockerScenario.Instance;
        if (scen == null) return;
        scen.showWindow = showWindow;
        scen.SetModifierKey(_currentVesselId, modifierKey);
        scen.SetStagingBlocked(_currentVesselId, _stagingBlocked);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Stock AppLauncher (toolbar button)
    // ─────────────────────────────────────────────────────────────────────────
    void OnGUIAppLauncherReady()
    {
        AddAppLauncherButton("onGUIApplicationLauncherReady");
    }
    
    IEnumerator RetryAppLauncherButton()
    {
        for (int i = 0; i < 30 && _appButton == null; i++)
        {
            AddAppLauncherButton("retry-" + i);
            if (_appButton != null)
            {
                _retryButtonCoroutine = null;
                yield break;
            }
            yield return new WaitForSeconds(1f);
        }

        _retryButtonCoroutine = null;
    }

    void AddAppLauncherButton(string source)
    {
        if (_appButton != null || _isAddingAppButton) return;
        _isAddingAppButton = true;
        try
        {
            Type alType = null;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                alType = a.GetType("ApplicationLauncher")
                       ?? a.GetType("KSP.UI.Screens.ApplicationLauncher");
                if (alType != null) break;
            }
            if (alType == null)
            {
                Debug.LogWarning("[StagingBlocker] AppLauncher add failed (" + source + "): type not found");
                return;
            }

            var instanceProp = alType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            var instance = instanceProp?.GetValue(null, null);
            if (instance == null)
            {
                Debug.Log("[StagingBlocker] AppLauncher add deferred (" + source + "): Instance not yet available");
                return;
            }

            // AppScenes.FLIGHT | AppScenes.MAPVIEW = 0x40 | 0x80 = 192
            Type appScenesType = alType.GetNestedType("AppScenes", BindingFlags.Public);
            object scenes = appScenesType != null
                ? (object)((int)Enum.Parse(appScenesType, "FLIGHT") | (int)Enum.Parse(appScenesType, "MAPVIEW"))
                : (object)192;

            // Find the Callback delegate type
            Type callbackType = alType.Assembly.GetType("Callback");
            if (callbackType == null)
            {
                Debug.LogWarning("[StagingBlocker] AppLauncher add failed (" + source + "): Callback type not found");
                return;
            }
            
            Delegate onTrue = Delegate.CreateDelegate(callbackType, this, "OnAppTrue");
            Delegate onFalse = Delegate.CreateDelegate(callbackType, this, "OnAppFalse");

            Texture2D icon = GameDatabase.Instance.GetTexture("StagingBlocker/Textures/icon", false);
            if (icon == null)
            {
                Debug.LogWarning("[StagingBlocker] Icon texture not found — using 1x1 fallback");
                icon = new Texture2D(1, 1);
                icon.SetPixel(0, 0, new Color(0.75f, 0.1f, 0.1f));
                icon.Apply();
            }

            var addMethod = alType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "AddModApplication" && m.GetParameters().Length == 8);
            if (addMethod == null)
            {
                Debug.LogWarning("[StagingBlocker] AppLauncher add failed (" + source + "): AddModApplication(8) not found");
                return;
            }

            _appButton = addMethod.Invoke(instance, new object[]
                { onTrue, onFalse, null, null, null, null, scenes, icon });
            Debug.Log("[StagingBlocker] AppLauncher add " + (_appButton != null ? "succeeded" : "returned null") + " (" + source + ")");
        }
        catch (Exception e)
        {
            Debug.LogWarning("[StagingBlocker] AppLauncher add failed (" + source + "): " + e.GetType().Name + ": " + e.Message);
        }
        finally
        {
            _isAddingAppButton = false;
        }
    }

    void OnAppTrue()
    {
        try
        {
            if (!showWindow)  // Only log/save if actually changing state
            {
                showWindow = true;
                SaveToScenario();
                Debug.Log("[StagingBlocker] Window opened via toolbar");
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[StagingBlocker] OnAppTrue callback failed: " + e.GetType().Name + ": " + e.Message);
            try { showWindow = true; } catch { }
        }
    }

    void OnAppFalse()
    {
        try
        {
            if (showWindow)  // Only log/save if actually changing state
            {
                showWindow = false;
                SaveToScenario();
                Debug.Log("[StagingBlocker] Window closed via toolbar");
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[StagingBlocker] OnAppFalse callback failed: " + e.GetType().Name + ": " + e.Message);
            try { showWindow = false; } catch { }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Cleanup
    // ─────────────────────────────────────────────────────────────────────────
    void OnDestroy()
    {
        if (_activeInstance == this)
            _activeInstance = null;

        // Always remove the staging lock when leaving flight
        InputLockManager.RemoveControlLock(LOCK_ID);
        Debug.Log("[StagingBlocker] Staging lock removed");

        GameEvents.onVesselChange.Remove(OnVesselChange);
        SaveToScenario();

        GameEvents.onGUIApplicationLauncherReady.Remove(OnGUIAppLauncherReady);
        if (_retryButtonCoroutine != null)
        {
            StopCoroutine(_retryButtonCoroutine);
            _retryButtonCoroutine = null;
        }

        // Remove stock AppLauncher button
        if (_appButton != null)
        {
            bool removed = false;
            try
            {
                Type alType = null;
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    alType = a.GetType("ApplicationLauncher")
                          ?? a.GetType("KSP.UI.Screens.ApplicationLauncher");
                    if (alType != null) break;
                }
                if (alType != null)
                {
                    var inst = alType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)
                                    ?.GetValue(null, null);
                    var rem  = alType.GetMethod("RemoveModApplication", BindingFlags.Public | BindingFlags.Instance);
                    if (rem != null && inst != null)
                    {
                        Debug.Log("[StagingBlocker] AppLauncher remove attempt starting.");
                        rem.Invoke(inst, new object[] { _appButton });
                        removed = true;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[StagingBlocker] AppLauncher remove failed: " + e.GetType().Name + ": " + e.Message);
            }

            Debug.Log("[StagingBlocker] AppLauncher remove " + (removed ? "succeeded." : "could not run (instance/method missing)."));
            _appButton = null;
        }
    }
}
