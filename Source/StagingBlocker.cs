using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using UnityEngine;

#pragma warning disable CS8618, CS8600, CS8601, CS8625

// ─────────────────────────────────────────────────────────────────────────────
//  Staging activation mode
// ─────────────────────────────────────────────────────────────────────────────
public enum StagingMode { ModifierKey = 0, PressAndHold = 1, DoubleTap = 2 }

// ─────────────────────────────────────────────────────────────────────────────
//  SBLunaHelper — optional detection of LunaMultiplayer (LMP)
//
//  Uses reflection so there is no hard dependency on LMP assemblies.
//  Player identity is resolved at runtime so settings can be namespaced
//  per-user where needed in scenario storage.
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
//  StagingBlockerScenario — persists per-vessel gameplay settings
//
//  Staging-block toggle is stored per user + per vessel.
//  Modifier/mode/hold/delay are stored per user + per vessel so Luna players
//  can have their own controls without overwriting each other.
// ─────────────────────────────────────────────────────────────────────────────
[KSPScenario(ScenarioCreationOptions.AddToAllGames, new GameScenes[] { GameScenes.FLIGHT })]
public class StagingBlockerScenario : ScenarioModule
{
    public static StagingBlockerScenario Instance;

    // vessel ID (string) → KeyCode name (string)
    public readonly Dictionary<string, string> vesselModifierKeys   = new Dictionary<string, string>();
    // user|vessel ID (string) → staging-block enabled (bool stored as string)
    public readonly Dictionary<string, bool>   vesselStagingBlocked = new Dictionary<string, bool>();

    private const KeyCode DEFAULT_KEY     = KeyCode.BackQuote; // tilde ~
    private const string  KEY_PREFIX      = "vesselKey_";
    private const string  BLOCKED_PREFIX  = "vesselBlocked_";
    private const string  MODE_PREFIX     = "vesselMode_";
    private const string  HOLD_PREFIX     = "vesselHold_";
    private const string  TAP_PREFIX      = "vesselTap_";

    public readonly Dictionary<string, int>   vesselStagingMode    = new Dictionary<string, int>();
    public readonly Dictionary<string, float> vesselHoldDuration   = new Dictionary<string, float>();
    public readonly Dictionary<string, float> vesselDoubleTapDelay = new Dictionary<string, float>();

    string UserVesselKey(string vesselId, string userId)
    {
        if (string.IsNullOrEmpty(vesselId)) return string.Empty;
        string safeUser = string.IsNullOrEmpty(userId) ? "SinglePlayer" : userId;
        return safeUser + "|" + vesselId;
    }

    public StagingMode GetStagingMode(string vesselId, string userId)
    {
        string key = UserVesselKey(vesselId, userId);
        if (!string.IsNullOrEmpty(key) && vesselStagingMode.TryGetValue(key, out int m))
            return (StagingMode)m;
        if (vesselId != null && vesselStagingMode.TryGetValue(vesselId, out m)) // backward compatibility
            return (StagingMode)m;
        return StagingMode.ModifierKey;
    }
    public void SetStagingMode(string vesselId, string userId, StagingMode mode)
    {
        string key = UserVesselKey(vesselId, userId);
        if (!string.IsNullOrEmpty(key)) vesselStagingMode[key] = (int)mode;
    }

    public float GetHoldDuration(string vesselId, string userId)
    {
        string key = UserVesselKey(vesselId, userId);
        if (!string.IsNullOrEmpty(key) && vesselHoldDuration.TryGetValue(key, out float v))
            return v;
        if (vesselId != null && vesselHoldDuration.TryGetValue(vesselId, out v)) // backward compatibility
            return v;
        return 5.0f;
    }
    public void SetHoldDuration(string vesselId, string userId, float v)
    {
        string key = UserVesselKey(vesselId, userId);
        if (!string.IsNullOrEmpty(key)) vesselHoldDuration[key] = v;
    }

    public float GetDoubleTapDelay(string vesselId, string userId)
    {
        string key = UserVesselKey(vesselId, userId);
        if (!string.IsNullOrEmpty(key) && vesselDoubleTapDelay.TryGetValue(key, out float v))
            return v;
        if (vesselId != null && vesselDoubleTapDelay.TryGetValue(vesselId, out v)) // backward compatibility
            return v;
        return 0.3f;
    }
    public void SetDoubleTapDelay(string vesselId, string userId, float v)
    {
        string key = UserVesselKey(vesselId, userId);
        if (!string.IsNullOrEmpty(key)) vesselDoubleTapDelay[key] = v;
    }

    public KeyCode GetModifierKey(string vesselId, string userId)
    {
        string key = UserVesselKey(vesselId, userId);
        if (!string.IsNullOrEmpty(key) && vesselModifierKeys.TryGetValue(key, out string keyStr))
        {
            try { return (KeyCode)Enum.Parse(typeof(KeyCode), keyStr); }
            catch { }
        }
        if (vesselId != null && vesselModifierKeys.TryGetValue(vesselId, out string oldKeyStr)) // backward compatibility
        {
            try { return (KeyCode)Enum.Parse(typeof(KeyCode), oldKeyStr); }
            catch { }
        }
        return DEFAULT_KEY;
    }

    public void SetModifierKey(string vesselId, string userId, KeyCode key)
    {
        string k = UserVesselKey(vesselId, userId);
        if (!string.IsNullOrEmpty(k))
            vesselModifierKeys[k] = key.ToString();
    }

    public bool GetStagingBlocked(string vesselId, string userId)
    {
        string key = UserVesselKey(vesselId, userId);
        if (!string.IsNullOrEmpty(key) && vesselStagingBlocked.TryGetValue(key, out bool blocked))
            return blocked;
        if (vesselId != null && vesselStagingBlocked.TryGetValue(vesselId, out blocked)) // backward compatibility
            return blocked;
        return true; // default: staging is blocked
    }

    public void SetStagingBlocked(string vesselId, string userId, bool blocked)
    {
        string key = UserVesselKey(vesselId, userId);
        if (!string.IsNullOrEmpty(key))
            vesselStagingBlocked[key] = blocked;
    }

    public override void OnAwake()
    {
        base.OnAwake();
        Instance = this;
    }

    public override void OnSave(ConfigNode node)
    {
        base.OnSave(node);
        foreach (var kv in vesselModifierKeys)
            node.AddValue(KEY_PREFIX + kv.Key, kv.Value);
        foreach (var kv in vesselStagingBlocked)
            node.AddValue(BLOCKED_PREFIX + kv.Key, kv.Value.ToString());
        foreach (var kv in vesselStagingMode)
            node.AddValue(MODE_PREFIX + kv.Key, kv.Value.ToString());
        foreach (var kv in vesselHoldDuration)
            node.AddValue(HOLD_PREFIX + kv.Key, kv.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var kv in vesselDoubleTapDelay)
            node.AddValue(TAP_PREFIX + kv.Key, kv.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public override void OnLoad(ConfigNode node)
    {
        base.OnLoad(node);
        vesselModifierKeys.Clear();
        vesselStagingBlocked.Clear();
        vesselStagingMode.Clear();
        vesselHoldDuration.Clear();
        vesselDoubleTapDelay.Clear();
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
            else if (val.name.StartsWith(MODE_PREFIX))
            {
                if (int.TryParse(val.value, out int m))
                    vesselStagingMode[val.name.Substring(MODE_PREFIX.Length)] = m;
            }
            else if (val.name.StartsWith(HOLD_PREFIX))
            {
                if (float.TryParse(val.value, System.Globalization.NumberStyles.Float,
                                   System.Globalization.CultureInfo.InvariantCulture, out float hv))
                    vesselHoldDuration[val.name.Substring(HOLD_PREFIX.Length)] = hv;
            }
            else if (val.name.StartsWith(TAP_PREFIX))
            {
                if (float.TryParse(val.value, System.Globalization.NumberStyles.Float,
                                   System.Globalization.CultureInfo.InvariantCulture, out float tv))
                    vesselDoubleTapDelay[val.name.Substring(TAP_PREFIX.Length)] = tv;
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
    private const string USER_PREFS_FILE_NAME = "StagingBlocker.xml";
    // ── Window / UI state ─────────────────────────────────────────────────────
    private Rect windowRect = new Rect(20, 80, 310, 270);
    private Rect _lastSavedWindowRect = new Rect(20, 80, 310, 270);
    private bool _lastSavedShowWindow = true;
    private bool _userPrefsLoaded = false;
    private float _lastWindowSaveTime = -1f;
    private const float WINDOW_SAVE_DEBOUNCE = 0.5f;
    private string _cachedUserPrefsPath = null;
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
    private string _currentUserId = "SinglePlayer";

    // ── Staging mode ─────────────────────────────────────────────────────────
    private StagingMode _stagingMode    = StagingMode.ModifierKey;
    private float       _holdDuration   = 5.0f;
    private float       _doubleTapDelay = 0.3f;

    // ── Press-and-hold runtime state ─────────────────────────────────────────
    private bool  _isHolding = false;
    private float _holdTimer = 0f;

    // ── Double-tap runtime state ──────────────────────────────────────────────
    private float _lastTapTime = -999f;
    private float _lastTapDelay = -1f;

    // ── GUI text-field state ──────────────────────────────────────────────────
    private string _holdDurationStr   = "5.0";
    private string _doubleTapDelayStr = "0.3";

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
            Debug.Log("[StagingBlocker] LunaMultiplayer detected — control settings are stored per user + per vessel (player: "
                      + SBLunaHelper.GetCurrentPlayerName() + ")");
        }
        else
        {
            Debug.Log("[StagingBlocker] Single-player mode");
        }

        // Load local user preferences (per-machine, not scenario-synced).
        LoadUserPrefs();

        // Load persisted gameplay settings (scenario; per-vessel).
        // All LMP players share the same per-vessel state via KSP/LMP scenario sync.

        // Subscribe to vessel change so we reload the key whenever the active vessel switches
        GameEvents.onVesselChange.Add(OnVesselChange);

        // Defer vessel-state load until the flight scene is fully ready.
        // ScenarioModules finish OnLoad() before onFlightReady fires, so saved settings are available.
        GameEvents.onFlightReady.Add(OnFlightReady);

        // Stock toolbar button
        GameEvents.onGUIApplicationLauncherReady.Add(OnGUIAppLauncherReady);
        GameEvents.onGUIApplicationLauncherUnreadifying.Add(OnGUIAppLauncherUnreadifying);
        _retryButtonCoroutine = StartCoroutine(RetryAppLauncherButton());
    }

    // ─────────────────────────────────────────────────────────────────────────
    void OnVesselChange(Vessel v)
    {
        try
        {
            SaveUserPrefs();                        // persist local UI state before addon churn during vessel switch
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

    void OnFlightReady()
    {
        GameEvents.onFlightReady.Remove(OnFlightReady);
        LoadVesselState(FlightGlobals.ActiveVessel);
        ApplyStagingLock(_stagingBlocked);
        Debug.Log("[StagingBlocker] Flight ready — initial vessel state loaded from scenario");
    }

    void LoadVesselState(Vessel v)
    {
        if (v == null) return;
        _currentVesselId = v.id.ToString();
        _currentUserId = SBLunaHelper.GetCurrentPlayerName();
        var scen = StagingBlockerScenario.Instance;
        if (scen != null)
        {
            modifierKey      = scen.GetModifierKey(_currentVesselId, _currentUserId);
            _stagingBlocked  = scen.GetStagingBlocked(_currentVesselId, _currentUserId);
            _stagingMode     = scen.GetStagingMode(_currentVesselId, _currentUserId);
            _holdDuration    = scen.GetHoldDuration(_currentVesselId, _currentUserId);
            _doubleTapDelay  = scen.GetDoubleTapDelay(_currentVesselId, _currentUserId);
        }
        else
        {
            modifierKey      = KeyCode.BackQuote;
            _stagingBlocked  = true;
            _stagingMode     = StagingMode.ModifierKey;
            _holdDuration    = 5.0f;
            _doubleTapDelay  = 0.3f;
        }
        _holdDurationStr   = _holdDuration.ToString("F1");
        _doubleTapDelayStr = _doubleTapDelay.ToString("F2");
        _lastTapTime = -999f;
        _lastTapDelay = -1f;
        Debug.Log("[StagingBlocker] Loaded vessel \"" + v.vesselName
                  + "\": key=" + modifierKey + ", blocked=" + _stagingBlocked
                  + ", mode=" + _stagingMode);
    }

    void ApplyStagingLock(bool block)
    {
        _stagingBlocked = block;
        bool shouldLock = _stagingMode != StagingMode.ModifierKey || _stagingBlocked;
        if (shouldLock)
        {
            InputLockManager.SetControlLock(ControlTypes.STAGING, LOCK_ID);
            Debug.Log("[StagingBlocker] Staging lock ON (mode=" + _stagingMode
                      + ", modifier: " + modifierKey + ")");
        }
        else
        {
            InputLockManager.RemoveControlLock(LOCK_ID);
            Debug.Log("[StagingBlocker] Staging lock OFF — stage key acts normally");
        }
        SaveToScenario();
    }

    void OnStagingModeChanged(StagingMode newMode)
    {
        _stagingMode = newMode;
        _isHolding   = false;
        _holdTimer   = _holdDuration;
        _lastTapTime = -999f;
        _lastTapDelay = -1f;
        isSettingKey = false;
        ApplyStagingLock(_stagingBlocked);
    }

    // ─────────────────────────────────────────────────────────────────────────
    void Update()
    {
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

        // Mode-specific staging logic
        switch (_stagingMode)
        {
            case StagingMode.ModifierKey:
                if (_stagingBlocked && Input.GetKey(modifierKey) && IsStageKeyDown())
                    TriggerNextStage();
                break;

            case StagingMode.PressAndHold:
                if (IsStageKeyDown())
                {
                    _isHolding = true;
                    _holdTimer = _holdDuration;
                }
                else if (_isHolding && !IsStageKeyHeld())
                {
                    // released before threshold
                    _isHolding = false;
                    _holdTimer = _holdDuration;
                }
                if (_isHolding && IsStageKeyHeld())
                {
                    _holdTimer -= Time.deltaTime;
                    if (_holdTimer <= 0f)
                    {
                        _isHolding = false;
                        _holdTimer = 0f;
                        TriggerNextStage();
                    }
                }
                break;

            case StagingMode.DoubleTap:
                if (IsStageKeyDown())
                {
                    float now = Time.time;
                    if (_lastTapTime > 0f)
                    {
                        float measuredDelay = now - _lastTapTime;
                        _lastTapDelay = measuredDelay <= 5.0f ? measuredDelay : -1f;

                        if (measuredDelay <= _doubleTapDelay)
                        {
                            _lastTapTime = -999f;
                            TriggerNextStage();
                        }
                        else
                        {
                            _lastTapTime = now;
                        }
                    }
                    else
                    {
                        _lastTapTime = now;
                    }
                }
                break;
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

        if (_userPrefsLoaded &&
            (Mathf.Abs(windowRect.x - _lastSavedWindowRect.x) > 0.01f ||
             Mathf.Abs(windowRect.y - _lastSavedWindowRect.y) > 0.01f ||
             Mathf.Abs(windowRect.width - _lastSavedWindowRect.width) > 0.01f ||
             Mathf.Abs(windowRect.height - _lastSavedWindowRect.height) > 0.01f))
        {
            float _now = Time.realtimeSinceStartup;
            if (_now - _lastWindowSaveTime > WINDOW_SAVE_DEBOUNCE)
            {
                _lastWindowSaveTime = _now;
                SaveUserPrefs();
            }
        }
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
        if (_lastSavedShowWindow != showWindow)
            SaveUserPrefs();
        SaveToScenario();
        if (_appButton != null)
        {
            TrySetAppButtonState(visible);
        }
    }

    void TrySetAppButtonState(bool visible)
    {
        try
        {
            string methodName = visible ? "SetTrue" : "SetFalse";
            var appButtonType = _appButton.GetType();

            // KSP versions differ: some expose SetTrue/SetFalse(), others SetTrue/SetFalse(bool).
            var noArg = appButtonType.GetMethod(methodName,
                BindingFlags.Public | BindingFlags.Instance,
                null,
                Type.EmptyTypes,
                null);
            if (noArg != null)
            {
                noArg.Invoke(_appButton, null);
                return;
            }

            var boolArg = appButtonType.GetMethod(methodName,
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(bool) },
                null);
            if (boolArg != null)
            {
                boolArg.Invoke(_appButton, new object[] { true });
                return;
            }

            // Last-chance fallback for unexpected signatures.
            appButtonType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance)
                         ?.Invoke(_appButton, null);
        }
        catch { }
    }

    // ─────────────────────────────────────────────────────────────────────────
    void DrawWindow(int id)
    {
        string stageKeyName = GetStageKeyDisplayName();
        bool showLastTapDelay = _stagingMode == StagingMode.DoubleTap
            && _lastTapDelay > 0f
            && _lastTapDelay <= 5.0f;

        // ── X (close) button — top-right of title bar ────────────────────────
        const float CLOSE_W = 16f;
        const float CLOSE_H = 14f;
        var prevBg = GUI.backgroundColor;
        GUI.backgroundColor = new Color(0.75f, 0.12f, 0.12f);
        if (GUI.Button(new Rect(windowRect.width - CLOSE_W - 4f, 3f, CLOSE_W, CLOSE_H), "×"))
            SetWindowVisible(false);
        GUI.backgroundColor = prevBg;

        GUILayout.BeginVertical();

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

        // ── Staging mode selector ────────────────────────────────────────────
        string[] modeNames = { "Modifier Key", "Press & Hold", "Double-Tap" };
        int modeIdx    = (int)_stagingMode;
        int newModeIdx = GUILayout.Toolbar(modeIdx, modeNames);
        if (newModeIdx != modeIdx)
            OnStagingModeChanged((StagingMode)newModeIdx);

        GUILayout.Space(4);

        // ── Mode-specific settings ───────────────────────────────────────────
        GUILayout.BeginVertical("box");

        switch (_stagingMode)
        {
            case StagingMode.ModifierKey:
            {
                // Toggle row
                GUILayout.BeginHorizontal();
                GUILayout.Label("Status:", GUILayout.Width(110));
                string toggleLabel = _stagingBlocked ? "ENABLED" : "DISABLED";
                Color prevColor = GUI.backgroundColor;
                GUI.backgroundColor = _stagingBlocked ? new Color(0.7f, 0.1f, 0.1f) : new Color(0.1f, 0.55f, 0.1f);
                if (GUILayout.Button(toggleLabel, GUILayout.Width(90)))
                    ApplyStagingLock(!_stagingBlocked);
                GUI.backgroundColor = prevColor;
                GUILayout.EndHorizontal();

                if (_stagingBlocked)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Modifier Key:", GUILayout.Width(110));
                    if (isSettingKey)
                    {
                        GUILayout.Label("[ Press a key... ]", GUILayout.Width(120));
                        if (GUILayout.Button("Cancel", GUILayout.Width(70)))
                            isSettingKey = false;
                    }
                    else
                    {
                        GUILayout.Label(ModifierKeyDisplayName(modifierKey), GUILayout.Width(90));
                        if (GUILayout.Button("Change", GUILayout.Width(70)))
                            isSettingKey = true;
                    }
                    GUILayout.EndHorizontal();
                    GUILayout.Label("Hold " + ModifierKeyDisplayName(modifierKey) + " and tap " + stageKeyName + " to activate stage", GUI.skin.label);
                    
                }
                break;
            }

            case StagingMode.PressAndHold:
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Duration (s):", GUILayout.Width(110));
                string newHoldStr = GUILayout.TextField(_holdDurationStr, GUILayout.Width(55));
                if (newHoldStr != _holdDurationStr)
                {
                    _holdDurationStr = newHoldStr;
                    if (float.TryParse(newHoldStr, System.Globalization.NumberStyles.Float,
                                       System.Globalization.CultureInfo.InvariantCulture, out float hv))
                    {
                        _holdDuration = Mathf.Clamp(hv, 1.0f, 30.0f);
                        SaveToScenario();
                    }
                }
                GUILayout.Label("(1-30)", GUILayout.Width(45));
                GUILayout.EndHorizontal();

                GUILayout.Space(2);
                GUILayout.Label("Hold " + stageKeyName + " for " + _holdDuration.ToString("F1") + "s to activate stage", GUI.skin.label);

                if (_isHolding)
                {
                    GUILayout.Space(2);
                    GUILayout.Label("Holding... " + _holdTimer.ToString("F1") + "s remaining", _labelCenterStyle);
                }
                break;
            }

            case StagingMode.DoubleTap:
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Delay (s):", GUILayout.Width(110));
                string newTapStr = GUILayout.TextField(_doubleTapDelayStr, GUILayout.Width(55));
                if (newTapStr != _doubleTapDelayStr)
                {
                    _doubleTapDelayStr = newTapStr;
                    if (float.TryParse(newTapStr, System.Globalization.NumberStyles.Float,
                                       System.Globalization.CultureInfo.InvariantCulture, out float tv))
                    {
                        _doubleTapDelay = Mathf.Clamp(tv, 0.1f, 5.0f);
                        SaveToScenario();
                    }
                }
                GUILayout.Label("(0.1-5)", GUILayout.Width(45));
                GUILayout.EndHorizontal();

                GUILayout.Space(2);
                GUILayout.Label("Double-tap " + stageKeyName + " within " + _doubleTapDelay.ToString("F2") + "s to activate stage", GUI.skin.label);
                GUILayout.Label(showLastTapDelay ? ("Last Delay: " + _lastTapDelay.ToString("F1") + "s") : " ",
                    GUI.skin.label, GUILayout.Height(20f));
                break;
            }
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

    bool IsStageKeyDown()
    {
        try { return GameSettings.LAUNCH_STAGES.GetKeyDown(); }
        catch { return Input.GetKeyDown(KeyCode.Space); }
    }

    bool IsStageKeyHeld()
    {
        try { return GameSettings.LAUNCH_STAGES.GetKey(); }
        catch { return Input.GetKey(KeyCode.Space); }
    }

    string GetStageKeyDisplayName()
    {
        try
        {
            string primary = StageBindingPartDisplayName(GameSettings.LAUNCH_STAGES.primary);
            string secondary = StageBindingPartDisplayName(GameSettings.LAUNCH_STAGES.secondary);

            if (!string.IsNullOrEmpty(primary) && !string.IsNullOrEmpty(secondary))
                return primary + " / " + secondary;
            if (!string.IsNullOrEmpty(primary)) return primary;
            if (!string.IsNullOrEmpty(secondary)) return secondary;
        }
        catch { }
        return "SPACE";
    }

    string StageBindingPartDisplayName(object bindingPart)
    {
        if (bindingPart == null) return "";
        string raw = bindingPart.ToString();
        if (string.IsNullOrEmpty(raw) || string.Equals(raw, "None", StringComparison.OrdinalIgnoreCase))
            return "";

        if (Enum.TryParse(raw, out KeyCode kc))
            return ModifierKeyDisplayName(kc);

        return raw;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Persistence
    // ─────────────────────────────────────────────────────────────────────────
    void SaveToScenario()
    {
        var scen = StagingBlockerScenario.Instance;
        if (scen == null) return;
        scen.SetModifierKey(_currentVesselId, _currentUserId, modifierKey);
        scen.SetStagingBlocked(_currentVesselId, _currentUserId, _stagingBlocked);
        scen.SetStagingMode(_currentVesselId, _currentUserId, _stagingMode);
        scen.SetHoldDuration(_currentVesselId, _currentUserId, _holdDuration);
        scen.SetDoubleTapDelay(_currentVesselId, _currentUserId, _doubleTapDelay);
    }

    void LoadUserPrefs()
    {
        try
        {
            string prefsPath = GetUserPrefsPath();
            Debug.Log("[StagingBlocker] LoadUserPrefs: path=\"" + prefsPath + "\", exists=" + File.Exists(prefsPath));

            if (!File.Exists(prefsPath))
            {
                Debug.Log("[StagingBlocker] LoadUserPrefs: no file — attempting legacy migration");
                if (TryLoadLegacyUserPrefs())
                    SaveUserPrefs();
                return;
            }

            var doc = new XmlDocument();
            doc.Load(prefsPath);

            XmlElement root = doc.DocumentElement;
            if (root == null)
            {
                Debug.LogWarning("[StagingBlocker] LoadUserPrefs: XML has no root element");
                return;
            }

            showWindow = ParseBool(GetFirstChildInnerText(root, "ShowWindow", "showWindow"), showWindow);

            XmlElement window = GetFirstChildElement(root, "Window", "window");
            float x;
            float y;
            float w;
            float h;
            if (window != null)
            {
                x = ParseFloat(GetFirstNonEmpty(
                    window.GetAttribute("x"),
                    window.GetAttribute("X"),
                    GetFirstChildInnerText(window, "x", "X")),
                    windowRect.x);

                y = ParseFloat(GetFirstNonEmpty(
                    window.GetAttribute("y"),
                    window.GetAttribute("Y"),
                    GetFirstChildInnerText(window, "y", "Y")),
                    windowRect.y);

                w = ParseFloat(GetFirstNonEmpty(
                    window.GetAttribute("width"),
                    window.GetAttribute("Width"),
                    GetFirstChildInnerText(window, "width", "Width", "w", "W")),
                    windowRect.width);

                h = ParseFloat(GetFirstNonEmpty(
                    window.GetAttribute("height"),
                    window.GetAttribute("Height"),
                    GetFirstChildInnerText(window, "height", "Height", "h", "H")),
                    windowRect.height);
            }
            else
            {
                x = ParseFloat(GetFirstChildInnerText(root, "windowX", "WindowX", "x", "X"), windowRect.x);
                y = ParseFloat(GetFirstChildInnerText(root, "windowY", "WindowY", "y", "Y"), windowRect.y);
                w = ParseFloat(GetFirstChildInnerText(root, "windowW", "WindowW", "width", "Width"), windowRect.width);
                h = ParseFloat(GetFirstChildInnerText(root, "windowH", "WindowH", "height", "Height"), windowRect.height);
            }

            windowRect = new Rect(x, y, Mathf.Max(MIN_WIDTH, w), Mathf.Max(MIN_HEIGHT, h));
            _lastSavedWindowRect = windowRect;
            _lastSavedShowWindow = showWindow;
            Debug.Log("[StagingBlocker] LoadUserPrefs: rect=" + windowRect + " showWindow=" + showWindow);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[StagingBlocker] Failed to load user prefs: " + e.Message);
            _lastSavedWindowRect = windowRect;
            _lastSavedShowWindow = showWindow;
        }
        finally
        {
            _userPrefsLoaded = true;
        }
    }

    void SaveUserPrefs()
    {
        try
        {
            string prefsPath = GetUserPrefsPath();
            string prefsDir = Path.GetDirectoryName(prefsPath);
            if (!string.IsNullOrEmpty(prefsDir))
                Directory.CreateDirectory(prefsDir);

            var doc = new XmlDocument();
            XmlElement root = doc.CreateElement("StagingBlockerUserPrefs");
            doc.AppendChild(root);

            XmlElement showWindowNode = doc.CreateElement("ShowWindow");
            showWindowNode.InnerText = showWindow.ToString();
            root.AppendChild(showWindowNode);

            XmlElement windowNode = doc.CreateElement("Window");
            windowNode.SetAttribute("x", windowRect.x.ToString(CultureInfo.InvariantCulture));
            windowNode.SetAttribute("y", windowRect.y.ToString(CultureInfo.InvariantCulture));
            windowNode.SetAttribute("width", windowRect.width.ToString(CultureInfo.InvariantCulture));
            windowNode.SetAttribute("height", windowRect.height.ToString(CultureInfo.InvariantCulture));
            root.AppendChild(windowNode);

            doc.Save(prefsPath);
            Debug.Log("[StagingBlocker] SaveUserPrefs: rect=" + windowRect + " → \"" + prefsPath + "\"");

            _lastSavedWindowRect = windowRect;
            _lastSavedShowWindow = showWindow;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[StagingBlocker] Failed to save user prefs: " + e.Message);
        }
    }

    string GetUserPrefsPath()
    {
        if (_cachedUserPrefsPath != null)
            return _cachedUserPrefsPath;

        try
        {
            string root = KSPUtil.ApplicationRootPath;
            if (!string.IsNullOrEmpty(root))
            {
                _cachedUserPrefsPath = Path.Combine(
                    root.TrimEnd('/', '\\'),
                    "GameData", "StagingBlocker", "PluginData", USER_PREFS_FILE_NAME);
                return _cachedUserPrefsPath;
            }
        }
        catch { }

        string assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (!string.IsNullOrEmpty(assemblyDir))
        {
            var pluginDir = new DirectoryInfo(assemblyDir);
            DirectoryInfo modDir = pluginDir.Parent;
            if (modDir != null)
            {
                _cachedUserPrefsPath = Path.Combine(modDir.FullName, "PluginData", USER_PREFS_FILE_NAME);
                return _cachedUserPrefsPath;
            }

            _cachedUserPrefsPath = Path.Combine(assemblyDir, USER_PREFS_FILE_NAME);
            return _cachedUserPrefsPath;
        }

        _cachedUserPrefsPath = USER_PREFS_FILE_NAME;
        return _cachedUserPrefsPath;
    }

    bool TryLoadLegacyUserPrefs()
    {
        try
        {
            var prefs = KSP.IO.PluginConfiguration.CreateForType<StagingBlockerFlight>();
            prefs.load();

            showWindow = prefs.GetValue<bool>("showWindow", showWindow);

            float x = prefs.GetValue<float>("windowX", windowRect.x);
            float y = prefs.GetValue<float>("windowY", windowRect.y);
            float w = prefs.GetValue<float>("windowW", windowRect.width);
            float h = prefs.GetValue<float>("windowH", windowRect.height);

            windowRect = new Rect(x, y, Mathf.Max(MIN_WIDTH, w), Mathf.Max(MIN_HEIGHT, h));
            _lastSavedWindowRect = windowRect;
            _lastSavedShowWindow = showWindow;
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[StagingBlocker] Legacy user prefs migration failed: " + e.Message);
            return false;
        }
    }

    static float ParseFloat(string? value, float fallback)
    {
        float parsed;
        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            return parsed;
        return fallback;
    }

    static string GetFirstNonEmpty(params string[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrEmpty(value))
                return value;
        }
        return string.Empty;
    }

    static XmlElement? GetFirstChildElement(XmlElement parent, params string[] names)
    {
        foreach (XmlNode node in parent.ChildNodes)
        {
            var element = node as XmlElement;
            if (element == null) continue;

            foreach (var name in names)
            {
                if (string.Equals(element.Name, name, StringComparison.OrdinalIgnoreCase))
                    return element;
            }
        }
        return null;
    }

    static string GetFirstChildInnerText(XmlElement parent, params string[] names)
    {
        var child = GetFirstChildElement(parent, names);
        return child != null ? child.InnerText : string.Empty;
    }

    static bool ParseBool(string? value, bool fallback)
    {
        bool parsed;
        if (bool.TryParse(value, out parsed))
            return parsed;
        return fallback;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Stock AppLauncher (toolbar button)
    // ─────────────────────────────────────────────────────────────────────────
    void OnGUIAppLauncherReady()
    {
        AddAppLauncherButton("onGUIApplicationLauncherReady");
    }

    void OnGUIAppLauncherUnreadifying(GameScenes _)
    {
        // Mirror SCANsat's lifecycle pattern: remove before AppLauncher rebuild.
        RemoveAppLauncherButton("onGUIApplicationLauncherUnreadifying");
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
                SetWindowVisible(true);
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
                SetWindowVisible(false);
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
        bool isActiveInstance = _activeInstance == this;

        if (isActiveInstance)
            _activeInstance = null;

        // Always remove the staging lock when leaving flight
        InputLockManager.RemoveControlLock(LOCK_ID);
        Debug.Log("[StagingBlocker] Staging lock removed");

        GameEvents.onVesselChange.Remove(OnVesselChange);
        GameEvents.onFlightReady.Remove(OnFlightReady);

        if (!isActiveInstance)
        {
            Debug.Log("[StagingBlocker] Skipping persistence for duplicate destroyed flight addon instance.");
        }
        else
        {
            SaveUserPrefs();
            SaveToScenario();
        }

        GameEvents.onGUIApplicationLauncherReady.Remove(OnGUIAppLauncherReady);
        GameEvents.onGUIApplicationLauncherUnreadifying.Remove(OnGUIAppLauncherUnreadifying);
        if (_retryButtonCoroutine != null)
        {
            StopCoroutine(_retryButtonCoroutine);
            _retryButtonCoroutine = null;
        }

        RemoveAppLauncherButton("OnDestroy");
    }

    void RemoveAppLauncherButton(string source)
    {
        if (_appButton == null)
            return;

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
                var rem = alType.GetMethod("RemoveModApplication", BindingFlags.Public | BindingFlags.Instance);
                if (rem != null && inst != null)
                {
                    Debug.Log("[StagingBlocker] AppLauncher remove attempt starting (" + source + ").");
                    rem.Invoke(inst, new object[] { _appButton });
                    removed = true;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[StagingBlocker] AppLauncher remove failed (" + source + "): " + e.GetType().Name + ": " + e.Message);
        }

        Debug.Log("[StagingBlocker] AppLauncher remove " + (removed ? "succeeded." : "could not run (instance/method missing).") + " (" + source + ")");
        _appButton = null;
    }
}
