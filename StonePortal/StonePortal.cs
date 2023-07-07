using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using ServerSync;

namespace StonePortal;
[HarmonyPatch]
[BepInPlugin(GUID, NAME, VERSION)]
public class Plugin : BaseUnityPlugin
{
  const string GUID = "stone_portal";
  const string NAME = "Stone Portal";
  const string VERSION = "1.4";
  const string PREFAB = "portal";
  readonly ConfigSync configSync = new(GUID) { DisplayName = NAME, CurrentVersion = VERSION, IsLocked = true };

#nullable disable
  public static ConfigEntry<bool> configEnabled;
  public static ConfigEntry<bool> configIgnoreRestrictions;
  public static ConfigEntry<string> configRequirements;
  public static ConfigEntry<string> configCraftingStation;
  public static ManualLogSource Log;
#nullable enable
  public void Awake()
  {
    Log = Logger;
    configEnabled = config("General", "Enabled", true, "Recipe enabled.");
    configEnabled.SettingChanged += (s, e) => Fix(ZNetScene.instance);
    configCraftingStation = config("General", "Crafting station", "piece_workbench", "Required crafting station.");
    configCraftingStation.SettingChanged += (s, e) => Fix(ZNetScene.instance);
    configIgnoreRestrictions = config("General", "No restrictions", false, "If enabled, all items can be teleported.");
    configIgnoreRestrictions.SettingChanged += (s, e) => Fix(ZNetScene.instance);
    configRequirements = config("General", "Recipe", "GreydwarfEye:20,SurtlingCore:10,Obsidian:100,Thunderstone:10", "Recipe (id:amount,id:amount,...)");
    configRequirements.SettingChanged += (s, e) => Fix(ZNetScene.instance);
    new Harmony(GUID).PatchAll();

    // Activate the custom portal patch by giving it the prefabs for each portal we're adding.
    // The patch updates several places in vanilla code which use hardcoded comparisons against the regular prefab,
    // patching them to also check for our Stone Portal prefab.
    AddPortal.hashes.Add(PREFAB.GetStableHashCode());

    try
    {
      SetupWatcher();
    }
    catch
    {
      //
    }
  }
  static void FixPortal(TeleportWorld tp)
  {
    if (!tp) return;
    Log.LogInfo("Fixing Stone Portal object.");
    if (!tp.m_proximityRoot)
      tp.m_proximityRoot = tp.transform;
    if (!tp.m_target_found)
    {
      var tr = tp.transform.Find("_target_found");
      tr.gameObject.SetActive(true);
      var fade = tr.gameObject.AddComponent<EffectFade>();
      fade.m_fadeDuration = 1f;
      tp.m_target_found = fade;
    }
  }
  static void FixRecipe(ZNetScene zs, Piece piece)
  {
    if (!piece) return;
    Log.LogInfo("Fixing Stone Portal recipe.");
    piece.m_enabled = configEnabled.Value;
    piece.m_category = Piece.PieceCategory.Misc;
    piece.m_craftingStation = null;
    if (zs.m_namedPrefabs.TryGetValue(configCraftingStation.Value.GetStableHashCode(), out var view))
    {
      if (view.TryGetComponent<CraftingStation>(out var craftingStation))
        piece.m_craftingStation = craftingStation;
    }
    piece.m_description = "$piece_portal_description";
    piece.m_resources = configRequirements.Value.Split(',').Select(s => s.Split(':')).Select(s =>
    {
      Piece.Requirement req = new();
      var id = s[0];
      if (!zs.m_namedPrefabs.TryGetValue(id.GetStableHashCode(), out var item)) return req;
      req.m_resItem = item.GetComponent<ItemDrop>();
      req.m_amount = 1;
      if (s.Length > 1)
        int.TryParse(s[1], out req.m_amount);
      return req;
    }).Where(req => req.m_resItem).ToArray();
  }
  static void Fix(ZNetScene zs)
  {
    if (!zs) return;
    if (!zs.m_namedPrefabs.TryGetValue(PREFAB.GetStableHashCode(), out var portal)) return;
    FixPortal(portal.GetComponent<TeleportWorld>());
    FixRecipe(zs, portal.GetComponent<Piece>());
    if (!zs.m_namedPrefabs.TryGetValue("Hammer".GetStableHashCode(), out var hammer)) return;
    if (hammer.GetComponent<ItemDrop>() is { } item)
    {
      var pieces = item.m_itemData.m_shared.m_buildPieces.m_pieces;
      if (!pieces.Contains(portal))
        pieces.Add(portal);
    }
  }

  [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Awake)), HarmonyPostfix]
  static void StonePortalRecipe(ZNetScene __instance) => Fix(__instance);

  [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.UpdatePortal))]
  public class TeleportWorldUpdatePortal
  {
    static void Prefix(TeleportWorld __instance)
    {
      if (!configIgnoreRestrictions.Value) return;
      if (Utils.GetPrefabName(__instance.gameObject) != PREFAB) return;
      ForceTeleportable.Force = true;
    }
    static void Postfix()
    {
      ForceTeleportable.Force = false;
    }
  }
  [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.Teleport))]
  public class TeleportWorldTeleport
  {
    static void Prefix(TeleportWorld __instance)
    {
      if (!configIgnoreRestrictions.Value) return;
      if (Utils.GetPrefabName(__instance.gameObject) != PREFAB) return;
      ForceTeleportable.Force = true;
    }
    static void Postfix()
    {
      ForceTeleportable.Force = false;
    }
  }

  [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.IsTeleportable))]
  public class ForceTeleportable
  {
    public static bool Force = false;
    static bool Postfix(bool result) => result || Force;
  }
  ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description, bool synchronizedSetting = true)
  {
    ConfigEntry<T> configEntry = Config.Bind(group, name, value, description);
    SyncedConfigEntry<T> syncedConfigEntry = configSync.AddConfigEntry(configEntry);
    syncedConfigEntry.SynchronizedConfig = synchronizedSetting;
    return configEntry;
  }

  ConfigEntry<T> config<T>(string group, string name, T value, string description, bool synchronizedSetting = true) => config(group, name, value, new ConfigDescription(description), synchronizedSetting);

  private void OnDestroy()
  {
    Config.Save();
  }

  private void SetupWatcher()
  {
    FileSystemWatcher watcher = new(Path.GetDirectoryName(Config.ConfigFilePath), Path.GetFileName(Config.ConfigFilePath));
    watcher.Changed += ReadConfigValues;
    watcher.Created += ReadConfigValues;
    watcher.Renamed += ReadConfigValues;
    watcher.IncludeSubdirectories = true;
    watcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
    watcher.EnableRaisingEvents = true;
  }

  private void ReadConfigValues(object sender, FileSystemEventArgs e)
  {
    if (!File.Exists(Config.ConfigFilePath)) return;
    try
    {
      Log.LogDebug("ReadConfigValues called");
      Config.Reload();
    }
    catch
    {
      Log.LogError($"There was an issue loading your {Config.ConfigFilePath}");
      Log.LogError("Please check your config entries for spelling and format!");
    }
  }
}
