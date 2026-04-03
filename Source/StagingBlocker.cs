using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEngine;

#pragma warning disable CS8618, CS8600, CS8601, CS8625

// ─────────────────────────────────────────────────────────────────────────────
//  LunaMultiplayerHelper — optional detection of LunaMultiplayer (LMP)
//
//  Uses reflection so there is no hard dependency on LMP assemblies.
//  The modifier hotkey is stored globally (no player prefix) in
//  StagingBlockerScenario, so KSP/LMP scenario-sync automatically propagates
//  the same key to every connected player — no additional work needed.
// ─────────────────────────────────────────────────────────────────────────────
public static class LunaMultiplayerHelper
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
                if (name == "LmpClient" || name.Contains("LunaMultiplayer") || name == "LMP.Client")
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
                // Probe LMP type/property paths without a hard reference.
                // Current LmpClient: SettingsSystem.CurrentSettings.PlayerName
                string[] settingsTypeNames =
                {
                    "LmpClient.Systems.SettingsSys.SettingsSystem"
                };
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    foreach (var typeName in settingsTypeNames)
                    {
                        var t = a.GetType(typeName);
                        if (t == null) continue;
                        var currentSettings = t.GetProperty("CurrentSettings",
                            BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                        if (currentSettings == null) continue;
                        var playerName = currentSettings.GetType()
                            .GetProperty("PlayerName")?.GetValue(currentSettings) as string;
                        if (!string.IsNullOrEmpty(playerName))
                        {
                            _cachedPlayerName = playerName;
                            return playerName;
                        }
                    }
                }

                // Legacy probe paths (older LMP builds)
                string[] legacyTypeNames =
                {
                    "LunaClient.Systems.PlayerConnection.PlayerConnectionSystem",
                    "LMP.Client.Systems.PlayerConnection.PlayerConnectionSystem"
                };
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    foreach (var typeName in legacyTypeNames)
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
    public enum StagingTriggerMode
    {
        HoldToStage,
        DoubleTap
    }

    public static StagingBlockerScenario Instance;

    public bool showWindow = true;

    // vessel ID (string) → KeyCode name (string)
    public readonly Dictionary<string, string> vesselModifierKeys   = new Dictionary<string, string>();
    // vessel ID (string) → staging-block enabled (bool stored as string)
    public readonly Dictionary<string, bool>   vesselStagingBlocked = new Dictionary<string, bool>();
    // vessel ID (string) → trigger mode
    public readonly Dictionary<string, string> vesselTriggerModes   = new Dictionary<string, string>();
    // vessel ID (string) → hold seconds
    public readonly Dictionary<string, float>  vesselHoldSeconds    = new Dictionary<string, float>();
    // vessel ID (string) → double-tap window seconds
    public readonly Dictionary<string, float>  vesselDoubleTapSeconds = new Dictionary<string, float>();

    private const KeyCode DEFAULT_KEY     = KeyCode.BackQuote; // tilde ~
    private const StagingTriggerMode DEFAULT_MODE = StagingTriggerMode.HoldToStage;
    private const float DEFAULT_HOLD_SECONDS = 5.00f;
    private const float DEFAULT_DOUBLE_TAP_SECONDS = 0.20f;
    private const string  KEY_PREFIX      = "vesselKey_";
    private const string  BLOCKED_PREFIX  = "vesselBlocked_";
    private const string  MODE_PREFIX     = "vesselMode_";
    private const string  HOLD_PREFIX     = "vesselHoldSec_";
    private const string  DOUBLE_TAP_PREFIX = "vesselDoubleTapSec_";

    // ── User-configurable defaults (applied to new/unconfigured vessels) ─────
    private const string  DEFAULT_KEY_NODE     = "defaultKey";
    private const string  DEFAULT_BLOCKED_NODE = "defaultBlocked";
    private const string  DEFAULT_MODE_NODE    = "defaultMode";
    private const string  DEFAULT_HOLD_NODE    = "defaultHoldSec";
    private const string  DEFAULT_DOUBLE_TAP_NODE = "defaultDoubleTapSec";
    private string _userDefaultKey     = null;   // null = use hardcoded DEFAULT_KEY
    private bool?  _userDefaultBlocked = null;   // null = use hardcoded true
    private StagingTriggerMode? _userDefaultMode = null;
    private float? _userDefaultHoldSeconds = null;
    private float? _userDefaultDoubleTapSeconds = null;

    public KeyCode GetModifierKey(string vesselId)
    {
        if (vesselId != null && vesselModifierKeys.TryGetValue(vesselId, out string keyStr))
        {
            try { return (KeyCode)Enum.Parse(typeof(KeyCode), keyStr); }
            catch { }
        }
        // Fall back to user default, then hardcoded default
        if (_userDefaultKey != null)
        {
            try { return (KeyCode)Enum.Parse(typeof(KeyCode), _userDefaultKey); }
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
        // Fall back to user default, then hardcoded default
        return _userDefaultBlocked ?? true;
    }

    public void SetStagingBlocked(string vesselId, bool blocked)
    {
        if (vesselId != null)
            vesselStagingBlocked[vesselId] = blocked;
    }

    public StagingTriggerMode GetTriggerMode(string vesselId)
    {
        if (vesselId != null && vesselTriggerModes.TryGetValue(vesselId, out string modeStr))
        {
            if (Enum.TryParse(modeStr, out StagingTriggerMode mode))
                return mode;
        }
        return _userDefaultMode ?? DEFAULT_MODE;
    }

    public void SetTriggerMode(string vesselId, StagingTriggerMode mode)
    {
        if (vesselId != null)
            vesselTriggerModes[vesselId] = mode.ToString();
    }

    public float GetHoldSeconds(string vesselId)
    {
        if (vesselId != null && vesselHoldSeconds.TryGetValue(vesselId, out float hold))
            return Mathf.Clamp(hold, 0.05f, 10.0f);
        return Mathf.Clamp(_userDefaultHoldSeconds ?? DEFAULT_HOLD_SECONDS, 0.05f, 10.0f);
    }

    public void SetHoldSeconds(string vesselId, float holdSeconds)
    {
        if (vesselId != null)
            vesselHoldSeconds[vesselId] = Mathf.Clamp(holdSeconds, 0.05f, 10.0f);
    }

    public float GetDoubleTapSeconds(string vesselId)
    {
        if (vesselId != null && vesselDoubleTapSeconds.TryGetValue(vesselId, out float dt))
            return Mathf.Clamp(dt, 0.10f, 1.0f);
        return Mathf.Clamp(_userDefaultDoubleTapSeconds ?? DEFAULT_DOUBLE_TAP_SECONDS, 0.10f, 1.0f);
    }

    public void SetDoubleTapSeconds(string vesselId, float doubleTapSeconds)
    {
        if (vesselId != null)
            vesselDoubleTapSeconds[vesselId] = Mathf.Clamp(doubleTapSeconds, 0.10f, 1.0f);
    }

    /// <summary>Save current vessel behavior as defaults for unconfigured vessels.</summary>
    public void SetDefaults(KeyCode key, bool blocked, StagingTriggerMode mode, float holdSeconds, float doubleTapSeconds)
    {
        _userDefaultKey     = key.ToString();
        _userDefaultBlocked = blocked;
        _userDefaultMode = mode;
        _userDefaultHoldSeconds = Mathf.Clamp(holdSeconds, 0.05f, 10.0f);
        _userDefaultDoubleTapSeconds = Mathf.Clamp(doubleTapSeconds, 0.10f, 1.0f);
    }

    /// <summary>True when user-configured defaults differ from the hardcoded fallback.</summary>
    public bool HasUserDefaults => _userDefaultKey != null
        || _userDefaultBlocked != null
        || _userDefaultMode != null
        || _userDefaultHoldSeconds != null
        || _userDefaultDoubleTapSeconds != null;

    public KeyCode DefaultKey => _userDefaultKey != null
        ? (KeyCode)Enum.Parse(typeof(KeyCode), _userDefaultKey)
        : DEFAULT_KEY;

    public bool DefaultBlocked => _userDefaultBlocked ?? true;
    public StagingTriggerMode DefaultMode => _userDefaultMode ?? DEFAULT_MODE;
    public float DefaultHoldSeconds => Mathf.Clamp(_userDefaultHoldSeconds ?? DEFAULT_HOLD_SECONDS, 0.05f, 10.0f);
    public float DefaultDoubleTapSeconds => Mathf.Clamp(_userDefaultDoubleTapSeconds ?? DEFAULT_DOUBLE_TAP_SECONDS, 0.10f, 1.0f);

    public override void OnAwake()
    {
        base.OnAwake();
        Instance = this;
    }

    public override void OnSave(ConfigNode node)
    {
        base.OnSave(node);
        node.SetValue("showWindow", showWindow.ToString(), true);
        if (_userDefaultKey != null)
            node.SetValue(DEFAULT_KEY_NODE, _userDefaultKey, true);
        if (_userDefaultBlocked != null)
            node.SetValue(DEFAULT_BLOCKED_NODE, _userDefaultBlocked.Value.ToString(), true);
        if (_userDefaultMode != null)
            node.SetValue(DEFAULT_MODE_NODE, _userDefaultMode.Value.ToString(), true);
        if (_userDefaultHoldSeconds != null)
            node.SetValue(DEFAULT_HOLD_NODE, _userDefaultHoldSeconds.Value.ToString(CultureInfo.InvariantCulture), true);
        if (_userDefaultDoubleTapSeconds != null)
            node.SetValue(DEFAULT_DOUBLE_TAP_NODE, _userDefaultDoubleTapSeconds.Value.ToString(CultureInfo.InvariantCulture), true);
        foreach (var kv in vesselModifierKeys)
            node.AddValue(KEY_PREFIX + kv.Key, kv.Value);
        foreach (var kv in vesselStagingBlocked)
            node.AddValue(BLOCKED_PREFIX + kv.Key, kv.Value.ToString());
        foreach (var kv in vesselTriggerModes)
            node.AddValue(MODE_PREFIX + kv.Key, kv.Value);
        foreach (var kv in vesselHoldSeconds)
            node.AddValue(HOLD_PREFIX + kv.Key, kv.Value.ToString(CultureInfo.InvariantCulture));
        foreach (var kv in vesselDoubleTapSeconds)
            node.AddValue(DOUBLE_TAP_PREFIX + kv.Key, kv.Value.ToString(CultureInfo.InvariantCulture));
    }

    public override void OnLoad(ConfigNode node)
    {
        base.OnLoad(node);
        if (node.HasValue("showWindow"))
            bool.TryParse(node.GetValue("showWindow"), out showWindow);

        // Load user-configurable defaults
        if (node.HasValue(DEFAULT_KEY_NODE))
            _userDefaultKey = node.GetValue(DEFAULT_KEY_NODE);
        if (node.HasValue(DEFAULT_BLOCKED_NODE))
        {
            if (bool.TryParse(node.GetValue(DEFAULT_BLOCKED_NODE), out bool b))
                _userDefaultBlocked = b;
        }
        if (node.HasValue(DEFAULT_MODE_NODE))
        {
            if (Enum.TryParse(node.GetValue(DEFAULT_MODE_NODE), out StagingTriggerMode mode))
                _userDefaultMode = mode;
        }
        if (node.HasValue(DEFAULT_HOLD_NODE))
        {
            if (float.TryParse(node.GetValue(DEFAULT_HOLD_NODE), NumberStyles.Float, CultureInfo.InvariantCulture, out float hold))
                _userDefaultHoldSeconds = hold;
        }
        if (node.HasValue(DEFAULT_DOUBLE_TAP_NODE))
        {
            if (float.TryParse(node.GetValue(DEFAULT_DOUBLE_TAP_NODE), NumberStyles.Float, CultureInfo.InvariantCulture, out float dt))
                _userDefaultDoubleTapSeconds = dt;
        }

        vesselModifierKeys.Clear();
        vesselStagingBlocked.Clear();
        vesselTriggerModes.Clear();
        vesselHoldSeconds.Clear();
        vesselDoubleTapSeconds.Clear();
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
                vesselTriggerModes[val.name.Substring(MODE_PREFIX.Length)] = val.value;
            }
            else if (val.name.StartsWith(HOLD_PREFIX))
            {
                if (float.TryParse(val.value, NumberStyles.Float, CultureInfo.InvariantCulture, out float hold))
                    vesselHoldSeconds[val.name.Substring(HOLD_PREFIX.Length)] = hold;
            }
            else if (val.name.StartsWith(DOUBLE_TAP_PREFIX))
            {
                if (float.TryParse(val.value, NumberStyles.Float, CultureInfo.InvariantCulture, out float dt))
                    vesselDoubleTapSeconds[val.name.Substring(DOUBLE_TAP_PREFIX.Length)] = dt;
            }
        }
        Debug.Log("[StagingBlocker] Scenario loaded — " + vesselModifierKeys.Count + " vessel key(s), "
                  + vesselStagingBlocked.Count + " blocked state(s), "
                  + vesselTriggerModes.Count + " mode(s)"
                  + (HasUserDefaults
                      ? ", user defaults: key=" + DefaultKey
                        + " blocked=" + DefaultBlocked
                        + " mode=" + DefaultMode
                        + " hold=" + DefaultHoldSeconds.ToString("F2", CultureInfo.InvariantCulture)
                        + " doubleTap=" + DefaultDoubleTapSeconds.ToString("F2", CultureInfo.InvariantCulture)
                      : ""));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  StagingBlockerFlight — runs in Flight scene
//  Responsibilities:
//    • Block spacebar staging unless modifier key is held
//    • Provide STAGING MANAGER GUI with manual stage trigger
//    • Stock AppLauncher + optional ToolbarController button
// ─────────────────────────────────────────────────────────────────────────────
[KSPAddon(KSPAddon.Startup.Flight, false)]
public class StagingBlockerFlight : MonoBehaviour
{
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
    private StagingBlockerScenario.StagingTriggerMode _triggerMode = StagingBlockerScenario.StagingTriggerMode.HoldToStage;
    private float _holdToStageSeconds = 5.00f;
    private float _doubleTapWindowSeconds = 0.20f;
    private float _holdStartRealtime = -1f;
    private bool _holdTriggeredThisPress = false;
    private float _lastSpaceTapRealtime = -10f;

    // ── GUI styles (created once after GUI system initialises) ────────────────
    private GUIStyle _redButtonStyle;
    private GUIStyle _labelCenterStyle;
    private bool _stylesReady = false;

    // ── Stock AppLauncher button ───────────────────────────────────────────────
    private object _appButton = null;

    // ── ToolbarController button (optional mod, persisted across vessel switches)
    private static bool _tcCreated = false;
    private static bool _tcInitAttempted = false;  // flag to prevent repeated attempts
    private static bool _tcToolbarRegistered = false;  // flag to prevent re-registering AddToAllToolbars
    private static GameObject _tcGO = null;
    private Component _tcButton = null;
    
    // ── Current instance for static toolbar callbacks ────────────────────────
    private static StagingBlockerFlight _currentInstance = null;

    // ── Input lock ID ─────────────────────────────────────────────────────────
    private const string LOCK_ID = "StagingBlocker_SpaceLock";

    // ── Staging block toggle (global) ─────────────────────────────────────────
    private bool _stagingBlocked = true;

    // ── Active vessel tracking (for per-vessel key load/save) ─────────────────
    private string _currentVesselId = null;
    
    // ── Toolbar button monitoring ──────────────────────────────────────────────
    private float _lastToolbarStatusCheck = 0f;
    private const float TOOLBAR_STATUS_CHECK_INTERVAL = 5f;  // Check every 5 seconds

    // ── Cached reflection handles for StageManager ────────────────────────────
    private static MethodInfo _activateNextStageMethod = null;
    private static bool _stagingMethodSearched = false;

    // ─────────────────────────────────────────────────────────────────────────
    void Start()
    {
        Debug.Log("[StagingBlocker] Flight addon started");
        
        // Update the current instance so static toolbar callbacks can forward to us
        _currentInstance = this;

        // Log multiplayer status
        if (LunaMultiplayerHelper.IsLunaEnabled)
        {
            Debug.Log("[StagingBlocker] LunaMultiplayer detected — modifier key is shared across all " +
                      "players via scenario storage (player: " + LunaMultiplayerHelper.GetCurrentPlayerName() + ")");
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
        StartCoroutine(RetryAppLauncherButton());

        // ToolbarController button (optional mod)
        TryCreateToolbarControllerButton();
    }

    // ─────────────────────────────────────────────────────────────────────────
    void OnVesselChange(Vessel v)
    {
        try
        {
            SaveToScenario();                        // persist the outgoing vessel's state first
            LoadVesselState(v);                      // load the incoming vessel's state
            ApplyStagingLock(_stagingBlocked);       // apply the new vessel's block toggle
            Debug.Log("[StagingBlocker] Vessel switch complete. Toolbar state preserved.");
            
            // Validate toolbar state after vessel change
            StartCoroutine(ValidateToolbarAfterVesselSwitch());
        }
        catch (Exception e)
        {
            Debug.LogWarning("[StagingBlocker] Error during vessel change: " + e.Message);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Validates and repairs toolbar state after vessel switch
    // ─────────────────────────────────────────────────────────────────────────
    IEnumerator ValidateToolbarAfterVesselSwitch()
    {
        yield return new WaitForSeconds(0.1f);  // Brief delay to let vessel change stabilize
        
        try
        {
            bool toolbarNeedsRepair = false;
            
            // Check if toolbar controller button is still valid
            if (_tcButton != null && _tcGO != null)
            {
                // Verify the GameObject is still active in scene
                if (!_tcGO.activeInHierarchy)
                {
                    Debug.LogWarning("[StagingBlocker] ToolbarController GameObject became inactive after vessel switch!");
                    _tcGO.SetActive(true);
                }
                
                // Check if component still exists
                if (_tcButton == null || _tcGO.GetComponent(_tcButton.GetType()) != _tcButton)
                {
                    Debug.LogWarning("[StagingBlocker] ToolbarController component is no longer valid after vessel switch!");
                    toolbarNeedsRepair = true;
                }
            }
            
            // Check if AppLauncher button is still valid
            if (_appButton != null)
            {
                // Buttons should remain valid, but log just in case
                Debug.Log("[StagingBlocker] AppLauncher button state valid after vessel switch");
            }
            
            if (toolbarNeedsRepair)
            {
                Debug.LogWarning("[StagingBlocker] Attempting toolbar repair after vessel switch...");
                // Don't recreate, just flag for next retry opportunity
                // The toolbar should repair itself naturally
            }
            
            Debug.Log("[StagingBlocker] Toolbar validation complete after vessel switch");
        }
        catch (Exception e)
        {
            Debug.LogWarning("[StagingBlocker] Error validating toolbar after vessel switch: " + e.Message);
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
            _triggerMode = scen.GetTriggerMode(_currentVesselId);
            _holdToStageSeconds = scen.GetHoldSeconds(_currentVesselId);
            _doubleTapWindowSeconds = scen.GetDoubleTapSeconds(_currentVesselId);
        }
        else
        {
            modifierKey     = KeyCode.BackQuote;
            _stagingBlocked = true;
            _triggerMode = StagingBlockerScenario.StagingTriggerMode.HoldToStage;
            _holdToStageSeconds = 5.00f;
            _doubleTapWindowSeconds = 0.20f;
        }
        Debug.Log("[StagingBlocker] Loaded vessel \"" + v.vesselName
                  + "\": key=" + modifierKey
                  + ", blocked=" + _stagingBlocked
                  + ", mode=" + _triggerMode
                  + ", hold=" + _holdToStageSeconds.ToString("F2", CultureInfo.InvariantCulture)
                  + ", doubleTap=" + _doubleTapWindowSeconds.ToString("F2", CultureInfo.InvariantCulture));
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

        // Staging handling while block is active
        if (_stagingBlocked)
            HandleBlockedStagingInput();

        // Monitor toolbar button health periodically
        _lastToolbarStatusCheck += Time.deltaTime;
        if (_lastToolbarStatusCheck >= TOOLBAR_STATUS_CHECK_INTERVAL)
        {
            _lastToolbarStatusCheck = 0f;
            MonitorToolbarHealth();
        }
    }

    void HandleBlockedStagingInput()
    {
        if (_triggerMode == StagingBlockerScenario.StagingTriggerMode.HoldToStage)
        {
            bool comboHeld = Input.GetKey(modifierKey) && Input.GetKey(KeyCode.Space);
            if (comboHeld)
            {
                if (_holdStartRealtime < 0f)
                {
                    _holdStartRealtime = Time.realtimeSinceStartup;
                    _holdTriggeredThisPress = false;
                }

                if (!_holdTriggeredThisPress
                    && Time.realtimeSinceStartup - _holdStartRealtime >= _holdToStageSeconds)
                {
                    TriggerNextStage();
                    _holdTriggeredThisPress = true;
                }
            }
            else
            {
                _holdStartRealtime = -1f;
                _holdTriggeredThisPress = false;
            }

            return;
        }

        if (_triggerMode == StagingBlockerScenario.StagingTriggerMode.DoubleTap)
        {
            if (!Input.GetKeyDown(KeyCode.Space))
                return;

            float now = Time.realtimeSinceStartup;
            if (now - _lastSpaceTapRealtime <= _doubleTapWindowSeconds)
            {
                TriggerNextStage();
                _lastSpaceTapRealtime = -10f;
            }
            else
            {
                _lastSpaceTapRealtime = now;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Monitors toolbar health and attempts recovery if button becomes unresponsive
    // ─────────────────────────────────────────────────────────────────────────
    void MonitorToolbarHealth()
    {
        try
        {
            // Check if ToolbarController button is still valid
            if (_tcGO != null && _tcButton != null)
            {
                if (!_tcGO.activeInHierarchy)
                {
                    Debug.LogWarning("[StagingBlocker] Toolbar button detected as inactive. Reactivating...");
                    _tcGO.SetActive(true);
                }
                
                // Verify component still exists on GameObject
                var comp = _tcGO.GetComponent(_tcButton.GetType());
                if (comp == null)
                {
                    Debug.LogWarning("[StagingBlocker] Toolbar button component missing from GameObject!");
                    _tcButton = null;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[StagingBlocker] Toolbar health check failed: " + e.Message);
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
        var scenTop = StagingBlockerScenario.Instance;
        bool isCurrentDefault = scenTop != null
            && modifierKey == scenTop.DefaultKey
            && _stagingBlocked == scenTop.DefaultBlocked
            && _triggerMode == scenTop.DefaultMode
            && Mathf.Abs(_holdToStageSeconds - scenTop.DefaultHoldSeconds) < 0.001f
            && Mathf.Abs(_doubleTapWindowSeconds - scenTop.DefaultDoubleTapSeconds) < 0.001f;
        GUI.enabled = !isCurrentDefault;
        if (GUILayout.Button("Set as Default", GUILayout.Width(105)))
        {
            if (scenTop != null)
            {
                scenTop.SetDefaults(modifierKey, _stagingBlocked, _triggerMode, _holdToStageSeconds, _doubleTapWindowSeconds);
                Debug.Log("[StagingBlocker] Defaults updated: key=" + modifierKey
                    + ", blocked=" + _stagingBlocked
                    + ", mode=" + _triggerMode
                    + ", hold=" + _holdToStageSeconds.ToString("F2", CultureInfo.InvariantCulture)
                    + ", doubleTap=" + _doubleTapWindowSeconds.ToString("F2", CultureInfo.InvariantCulture));
            }
        }
        GUI.enabled = true;
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

        // Tab row
        GUILayout.BeginHorizontal();
        bool holdSelected = _triggerMode == StagingBlockerScenario.StagingTriggerMode.HoldToStage;
        bool doubleTapSelected = _triggerMode == StagingBlockerScenario.StagingTriggerMode.DoubleTap;
        if (GUILayout.Toggle(holdSelected, "Hold To Stage", "Button"))
            _triggerMode = StagingBlockerScenario.StagingTriggerMode.HoldToStage;
        if (GUILayout.Toggle(doubleTapSelected, "Double Tap", "Button"))
            _triggerMode = StagingBlockerScenario.StagingTriggerMode.DoubleTap;
        GUILayout.EndHorizontal();

        GUILayout.Space(3);

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
            if (_triggerMode == StagingBlockerScenario.StagingTriggerMode.HoldToStage)
            {
                GUILayout.Label(">>Hold Modifier Key + SPACE to STAGE<<", GUI.skin.label);
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

                GUILayout.BeginHorizontal();
                GUILayout.Label("Hold Time:", GUILayout.Width(100));
                _holdToStageSeconds = GUILayout.HorizontalSlider(_holdToStageSeconds, 0.05f, 10.0f, GUILayout.Width(120));
                GUILayout.Label(_holdToStageSeconds.ToString("F2", CultureInfo.InvariantCulture) + "s", GUILayout.Width(60));
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.Label(">>Double tap SPACE to STAGE<<", GUI.skin.label);
                GUILayout.Space(2);

                GUILayout.BeginHorizontal();
                GUILayout.Label("Tap Window:", GUILayout.Width(100));
                _doubleTapWindowSeconds = GUILayout.HorizontalSlider(_doubleTapWindowSeconds, 0.10f, 1.0f, GUILayout.Width(120));
                GUILayout.Label(_doubleTapWindowSeconds.ToString("F2", CultureInfo.InvariantCulture) + "s", GUILayout.Width(60));
                GUILayout.EndHorizontal();
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
        scen.SetTriggerMode(_currentVesselId, _triggerMode);
        scen.SetHoldSeconds(_currentVesselId, _holdToStageSeconds);
        scen.SetDoubleTapSeconds(_currentVesselId, _doubleTapWindowSeconds);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Stock AppLauncher (toolbar button)
    // ─────────────────────────────────────────────────────────────────────────
    void OnGUIAppLauncherReady()
    {
        AddAppLauncherButton();
    }
    
    IEnumerator RetryAppLauncherButton()
    {
        for (int i = 0; i < 30 && _appButton == null; i++)
        {
            AddAppLauncherButton();
            if (_appButton != null) yield break;
            yield return new WaitForSeconds(1f);
        }
    }

    void AddAppLauncherButton()
    {
        if (_appButton != null) return;
        try
        {
            Type alType = null;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                alType = a.GetType("ApplicationLauncher")
                       ?? a.GetType("KSP.UI.Screens.ApplicationLauncher");
                if (alType != null) break;
            }
            if (alType == null) return;

            var readyProp = alType.GetProperty("Ready", BindingFlags.Public | BindingFlags.Static);
            bool ready = readyProp != null && (bool)readyProp.GetValue(null, null);
            if (!ready) return;

            var instanceProp = alType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            var instance = instanceProp?.GetValue(null, null);
            if (instance == null) return;

            // AppScenes.FLIGHT | AppScenes.MAPVIEW = 0x40 | 0x80 = 192
            Type appScenesType = alType.GetNestedType("AppScenes", BindingFlags.Public);
            object scenes = appScenesType != null
                ? (object)((int)Enum.Parse(appScenesType, "FLIGHT") | (int)Enum.Parse(appScenesType, "MAPVIEW"))
                : (object)192;

            Type ruitType = alType.Assembly.GetType("RUIToggleButton");
            Type onTrueType  = ruitType?.GetNestedType("OnTrue");
            Type onFalseType = ruitType?.GetNestedType("OnFalse");
            Delegate onTrue  = onTrueType  != null
                ? Delegate.CreateDelegate(onTrueType,  this, "OnAppTrue")
                : (Delegate)(UnityEngine.Events.UnityAction)OnAppTrue;
            Delegate onFalse = onFalseType != null
                ? Delegate.CreateDelegate(onFalseType, this, "OnAppFalse")
                : (Delegate)(UnityEngine.Events.UnityAction)OnAppFalse;

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
            if (addMethod != null)
            {
                _appButton = addMethod.Invoke(instance, new object[]
                    { onTrue, onFalse, null, null, null, null, scenes, icon });
                Debug.Log("[StagingBlocker] AppLauncher button added: " + (_appButton != null));
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[StagingBlocker] AppLauncher button failed: " + e.GetType().Name + ": " + e.Message);
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

    // Static wrapper for toolbar callbacks - forwards to current instance
    // This allows the Toolbar Controller to call these without breaking on vessel switches
    private static void StaticOnAppTrue()
    {
        if (_currentInstance != null)
            _currentInstance.OnAppTrue();
    }

    private static void StaticOnAppFalse()
    {
        if (_currentInstance != null)
            _currentInstance.OnAppFalse();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  ToolbarController (optional mod)
    // ─────────────────────────────────────────────────────────────────────────
    void TryCreateToolbarControllerButton()
    {
        try
        {
            // If already created successfully, skip entirely
            if (_tcCreated) return;
            
            // If we've already attempted creation (even if it failed), don't try again
            // This prevents repeated attempts that could cause duplicates
            if (_tcInitAttempted) return;
            
            _tcInitAttempted = true;  // Mark that we've attempted, even if it fails

            Type tcType = null;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                tcType = a.GetType("ToolbarControl_NS.ToolbarControl");
                if (tcType != null) break;
            }
            if (tcType == null)
            {
                Debug.Log("[StagingBlocker] ToolbarController not present — using stock AppLauncher only");
                return;
            }

            // RegisterMod
            var registerMod = tcType.GetMethod("RegisterMod", BindingFlags.Public | BindingFlags.Static);
            if (registerMod != null)
            {
                try
                {
                    var rp = registerMod.GetParameters();
                    string[] strVals = { "StagingBlocker", "StagingBlockerButton", "Staging Blocker", "StagingBlocker/Textures/icon" };
                    int strIdx = 0;
                    var args = new object[rp.Length];
                    for (int i = 0; i < rp.Length; i++)
                    {
                        if (rp[i].ParameterType == typeof(string))
                            args[i] = strIdx < strVals.Length ? strVals[strIdx++] : "";
                        else if (rp[i].ParameterType == typeof(bool))
                            args[i] = rp[i].HasDefaultValue ? rp[i].DefaultValue : true;
                        else
                            args[i] = rp[i].HasDefaultValue ? rp[i].DefaultValue : null;
                    }
                    registerMod.Invoke(null, args);
                }
                catch (Exception ex) { Debug.LogWarning("[StagingBlocker] RegisterMod failed: " + ex.Message); }
            }

            _tcGO = new GameObject("StagingBlockerToolbar");
            DontDestroyOnLoad(_tcGO);
            _tcButton = (Component)_tcGO.AddComponent(tcType);
            _tcCreated = true;  // Mark component created
            
            // Only call AddToAllToolbars() once per session to avoid duplicate entries
            if (!_tcToolbarRegistered)
            {
                Type tcClickType = tcType.GetNestedType("TC_ClickHandler",
                    BindingFlags.Public | BindingFlags.NonPublic);
                if (tcClickType == null)
                {
                    Debug.LogWarning("[StagingBlocker] TC_ClickHandler type not found");
                    return;
                }
                
                // Use static wrapper methods so callbacks survive addon recreation on vessel switches
                Delegate tcOnTrue = null;
                Delegate tcOnFalse = null;
                try
                {
                    tcOnTrue = Delegate.CreateDelegate(tcClickType, null, typeof(StagingBlockerFlight).GetMethod("StaticOnAppTrue", BindingFlags.Static | BindingFlags.NonPublic));
                    tcOnFalse = Delegate.CreateDelegate(tcClickType, null, typeof(StagingBlockerFlight).GetMethod("StaticOnAppFalse", BindingFlags.Static | BindingFlags.NonPublic));
                    Debug.Log("[StagingBlocker] TC delegates created with static forwarding");
                }
                catch (Exception ex)
                {
                    Debug.LogError("[StagingBlocker] Failed to create TC delegates: " + ex.Message);
                    return;
                }

                var addMethod8 = tcType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "AddToAllToolbars" && m.GetParameters().Length == 8);
                if (addMethod8 == null) { Debug.LogWarning("[StagingBlocker] AddToAllToolbars(8) not found"); return; }

                Type appScenesType2 = addMethod8.GetParameters()[2].ParameterType;
                object flightScene = Enum.Parse(appScenesType2, "FLIGHT");

                try
                {
                    addMethod8.Invoke(_tcButton, new object[]
                    {
                        tcOnTrue, tcOnFalse, flightScene,
                        "StagingBlocker", "StagingBlockerButton",
                        "StagingBlocker/Textures/icon",
                        "StagingBlocker/Textures/icon",
                        "Staging Blocker"
                    });
                    _tcToolbarRegistered = true;
                    Debug.Log("[StagingBlocker] ToolbarController button registered (one-time)");
                }
                catch (Exception ex)
                {
                    Debug.LogError("[StagingBlocker] AddToAllToolbars invocation failed: " + ex.Message);
                    return;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[StagingBlocker] ToolbarController init failed: " + e.GetType().Name + ": " + e.Message);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Reset toolbar buttons (removes and recreates them)
    // ─────────────────────────────────────────────────────────────────────────
    void ResetToolbarButtons()
    {
        Debug.Log("[StagingBlocker] Resetting toolbar buttons (this may take a moment)...");
        
        // Remove stock AppLauncher button
        if (_appButton != null)
        {
            try
            {
                Type alType = null;
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    alType = a.GetType("ApplicationLauncher");
                    if (alType != null) break;
                }
                if (alType != null)
                {
                    var inst = alType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)
                                    ?.GetValue(null, null);
                    var rem  = alType.GetMethod("RemoveModApplication", BindingFlags.Public | BindingFlags.Instance);
                    rem?.Invoke(inst, new object[] { _appButton });
                    Debug.Log("[StagingBlocker] AppLauncher button removed for reset");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[StagingBlocker] Failed to remove AppLauncher button: " + e.Message);
            }
            _appButton = null;
        }

        // Destroy ToolbarController button
        if (_tcGO != null)
        {
            try { Destroy(_tcGO); } catch { }
            _tcGO = null;
            _tcButton = null;
            Debug.Log("[StagingBlocker] ToolbarController button removed for reset");
        }
        
        // Reset creation flags to allow re-initialization
        _tcCreated = false;
        _tcInitAttempted = false;

        // Recreate buttons
        Debug.Log("[StagingBlocker] Toolbar buttons reset complete. Recreating...");
        StartCoroutine(RetryAppLauncherButton());
        TryCreateToolbarControllerButton();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Cleanup
    // ─────────────────────────────────────────────────────────────────────────
    void OnDestroy()
    {
        // Always remove the staging lock when leaving flight
        InputLockManager.RemoveControlLock(LOCK_ID);
        Debug.Log("[StagingBlocker] Staging lock removed");

        GameEvents.onVesselChange.Remove(OnVesselChange);
        SaveToScenario();

        GameEvents.onGUIApplicationLauncherReady.Remove(OnGUIAppLauncherReady);

        // Remove stock AppLauncher button
        if (_appButton != null)
        {
            try
            {
                Type alType = null;
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    alType = a.GetType("ApplicationLauncher");
                    if (alType != null) break;
                }
                if (alType != null)
                {
                    var inst = alType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)
                                    ?.GetValue(null, null);
                    var rem  = alType.GetMethod("RemoveModApplication", BindingFlags.Public | BindingFlags.Instance);
                    rem?.Invoke(inst, new object[] { _appButton });
                }
            }
            catch { }
            _appButton = null;
        }

        // Destroy ToolbarController button only when truly leaving flight
        if (!HighLogic.LoadedSceneIsFlight && _tcGO != null)
        {
            try { Destroy(_tcGO); } catch { }
            _tcGO = null;
            _tcButton = null;
            _tcCreated = false;
            _tcToolbarRegistered = false; // Reset so button can be re-registered if returning to flight
            _currentInstance = null;
        }
    }
}
