using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using HarmonyLib;

namespace StonePortal
{
  // This patch was written by blaxxun.  Source: https://discord.com/channels/1112768725712642209/1116483988702384138/1117488143231353074
  // This patch finds the four places in Vanilla's portal handling code (which checks a prefab to see if it == the vanilla portal prefab)
  // and transpiles in an if statement to also check the HashSet defined here.  It's an elegant solution that works even if multiple
  // mods duplicate this patch, since it should just add an extra OR for each mod to check its own HashSet... which is exactly what we want.
  [HarmonyPatch]
  public static class AddPortal
  {
    public static HashSet<int> hashes = new();

    private static IEnumerable<MethodInfo> TargetMethods() => new[]
    {
      AccessTools.DeclaredMethod(typeof(ZDOMan), nameof(ZDOMan.Load)),
      AccessTools.DeclaredMethod(typeof(ZDOMan), nameof(ZDOMan.CreateNewZDO), new[] { typeof(ZDOID), typeof(Vector3), typeof(int) }),
      AccessTools.DeclaredMethod(typeof(ZDOMan), nameof(ZDOMan.HandleDestroyedZDO)),
      AccessTools.DeclaredMethod(typeof(ZDOMan), nameof(ZDOMan.RPC_ZDOData))
    };

    private static bool HashIsPortal(int hash) => hashes.Contains(hash);

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructionsEnumerable, ILGenerator ilg)
    {
      List<CodeInstruction> instructions = instructionsEnumerable.ToList();
      MethodInfo prefabMethod = AccessTools.DeclaredPropertyGetter(typeof(Game), nameof(Game.PortalPrefabHash));
      int index = instructions.FindIndex(c => c.Calls(prefabMethod)) - 1;
      Label failLabel = ilg.DefineLabel();
      Label matchLabel = ilg.DefineLabel();
      instructions[index + 3].labels.Add(matchLabel);

      instructions.InsertRange(index, new[]
      {
        new CodeInstruction(OpCodes.Dup),
        new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(AddPortal), nameof(HashIsPortal))),
        new CodeInstruction(OpCodes.Brfalse, failLabel),
        new CodeInstruction(OpCodes.Pop),
        new CodeInstruction(OpCodes.Br, matchLabel),
        new CodeInstruction(OpCodes.Nop) { labels = { failLabel } }
      });

      return instructions;
    }
  }
}