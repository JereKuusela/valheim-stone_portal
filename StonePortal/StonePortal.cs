using System.Collections.Generic;
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
public class Plugin : BaseUnityPlugin {
  const string GUID = "stone_portal";
  const string NAME = "Stone Portal";
  const string VERSION = "1.0";
  ConfigSync configSync = new ConfigSync(GUID) { DisplayName = NAME, CurrentVersion = VERSION, IsLocked = true };

#nullable disable
  public static ConfigEntry<bool> configEnabled;
  public static ConfigEntry<string> configRequirements;
  public static ManualLogSource Log;
#nullable enable
  public void Awake() {
    Log = Logger;
    configEnabled = config("General", "Enabled", true, "Recipe enabled.");
    configEnabled.SettingChanged += (s, e) => Fix(ZNetScene.instance);
    configRequirements = config("General", "Recipe", "Wood", "Recipe");
    configRequirements.SettingChanged += (s, e) => Fix(ZNetScene.instance);
    new Harmony(GUID).PatchAll();
    try {
      SetupWatcher();
    } catch {
      //
    }
  }
  static void FixPortal(TeleportWorld tp) {
    if (!tp) return;
    Log.LogInfo("Fixing Stone Portal object.");
    if (!tp.m_proximityRoot)
      tp.m_proximityRoot = tp.transform;
    if (!tp.m_target_found) {
      var tr = tp.transform.Find("_target_found");
      tr.gameObject.SetActive(true);
      var fade = tr.gameObject.AddComponent<EffectFade>();
      fade.m_fadeDuration = 1f;
      tp.m_target_found = fade;
    }
  }
  static void FixRecipe(ZNetScene zs, Piece piece) {
    if (!piece) return;
    Log.LogInfo("Fixing Stone Portal recipe.");
    piece.m_enabled = configEnabled.Value;
    piece.m_category = Piece.PieceCategory.Misc;
    piece.m_description = "$piece_portal_description";
    piece.m_resources = configRequirements.Value.Split(',').Select(s => s.Split(':')).Select(s => {
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
  static void Fix(ZNetScene zs) {
    if (!zs) return;
    if (!zs.m_namedPrefabs.TryGetValue("portal".GetStableHashCode(), out var portal)) return;
    FixPortal(portal.GetComponent<TeleportWorld>());
    FixRecipe(zs, portal.GetComponent<Piece>());
    if (!zs.m_namedPrefabs.TryGetValue("Hammer".GetStableHashCode(), out var hammer)) return;
    if (hammer.GetComponent<ItemDrop>() is { } item) {
      var pieces = item.m_itemData.m_shared.m_buildPieces.m_pieces;
      if (!pieces.Contains(portal))
        pieces.Add(portal);
    }
  }

  [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Awake)), HarmonyPostfix]
  static void StonePortalRecipe(ZNetScene __instance) => Fix(__instance);

  [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.GetAllZDOsWithPrefabIterative)), HarmonyPrefix]
  static void FixConnection(ZDOMan __instance, string prefab, List<ZDO> zdos, int index) {
    if (prefab == Game.instance.m_portalPrefab.name) {
      __instance.GetAllZDOsWithPrefabIterative("portal", zdos, ref index);
    }
  }

  ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description, bool synchronizedSetting = true) {
    ConfigEntry<T> configEntry = Config.Bind(group, name, value, description);
    SyncedConfigEntry<T> syncedConfigEntry = configSync.AddConfigEntry(configEntry);
    syncedConfigEntry.SynchronizedConfig = synchronizedSetting;
    return configEntry;
  }

  ConfigEntry<T> config<T>(string group, string name, T value, string description, bool synchronizedSetting = true) => config(group, name, value, new ConfigDescription(description), synchronizedSetting);

  private void OnDestroy() {
    Config.Save();
  }

  private void SetupWatcher() {
    FileSystemWatcher watcher = new(Path.GetDirectoryName(Config.ConfigFilePath), Path.GetFileName(Config.ConfigFilePath));
    watcher.Changed += ReadConfigValues;
    watcher.Created += ReadConfigValues;
    watcher.Renamed += ReadConfigValues;
    watcher.IncludeSubdirectories = true;
    watcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
    watcher.EnableRaisingEvents = true;
  }

  private void ReadConfigValues(object sender, FileSystemEventArgs e) {
    if (!File.Exists(Config.ConfigFilePath)) return;
    try {
      Log.LogDebug("ReadConfigValues called");
      Config.Reload();
    } catch {
      Log.LogError($"There was an issue loading your {Config.ConfigFilePath}");
      Log.LogError("Please check your config entries for spelling and format!");
    }
  }
}
