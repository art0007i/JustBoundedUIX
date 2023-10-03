using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Reflection;
using System.Text;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.UIX;
using HarmonyLib;
using ResoniteModLoader;
using System.Linq;
using System.Threading.Tasks;

namespace JustBoundedUIX
{
    public class JustBoundedUIX : ResoniteMod
    {
        public const float GizmoOffset = .02f;
        public override string Author => "art0007i";
        public override string Link => "https://github.com/art0007i/JustBoundedUIX";
        public override string Name => "JustBoundedUIX";
        public override string Version => "2.0.1";

        public override void OnEngineInit()
        {
            Harmony harmony = new Harmony("me.art0007i.JustBoundedUIX");
            harmony.PatchAll();
        }

        public static RectTransform GetRectTransform(Slot target)
        {
            if (target == null) return null;
            if (target.GetComponent<RectTransform>() is RectTransform rt && rt.Canvas != null
                && rt.Slot != rt.Canvas.Slot) return rt;
            else return null;
        }
        public static BoundingBox GetBoundingBox(RectTransform rt)
        {
            var area = rt.ComputeGlobalComputeRect();
            var bounds = BoundingBox.Empty();
            bounds.Encapsulate(rt.Canvas.Slot.LocalPointToGlobal(area.ExtentMin / rt.Canvas.UnitScale));
            bounds.Encapsulate(rt.Canvas.Slot.LocalPointToGlobal(area.ExtentMax / rt.Canvas.UnitScale));
            return bounds;
        }

        [HarmonyPatch]
        internal static class SceneInspectorPatches
        {
            static IEnumerable<MethodBase> TargetMethods()
            {
                var func = AccessTools.Method(typeof(SceneInspector), "OnInsertParentPressed");
                yield return func;
                func = AccessTools.Method(typeof(SceneInspector), "OnAddChildPressed");
                yield return func;
            }

            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codes)
            {
                var lookFor = typeof(Slot).GetMethod(nameof(Slot.AddSlot));
                foreach (var code in codes)
                {
                    yield return code;
                    if (code.Calls(lookFor))
                    {
                        yield return new CodeInstruction(OpCodes.Ldarg_0);
                        yield return new CodeInstruction(OpCodes.Call, typeof(SceneInspectorPatches).GetMethod(nameof(ProxyFunc)));
                    }
                }
            }

            public static Slot ProxyFunc(Slot sl, SceneInspector i)
            {
                if (i.ComponentView.Target is Slot target && target.GetComponent<RectTransform>() is RectTransform rt && rt.Canvas != null)
                {
                    sl.AttachComponent<RectTransform>();
                }
                return sl;
            }
        }

        [HarmonyPatch(typeof(SlotGizmo))]
        internal static class SlotGizmoPatches
        {

            private static readonly MethodInfo boundUIXMethod = typeof(SlotGizmoPatches).GetMethod(nameof(BoundUIX), AccessTools.allDeclared);

            private static readonly MethodInfo computeBoundingBoxMethod = typeof(BoundsHelper).GetMethod("ComputeBoundingBox", AccessTools.allDeclared);

            private static BoundingBox BoundUIX(BoundingBox bounds, Slot target, Slot space)
            {
                if (target.GetComponent<RectTransform>() is RectTransform rt && rt.Canvas != null
                    && rt.Slot != rt.Canvas.Slot)
                {
                    var area = rt.ComputeGlobalComputeRect();
                    bounds.Encapsulate(space.GlobalPointToLocal(rt.Canvas.Slot.LocalPointToGlobal(area.ExtentMin / rt.Canvas.UnitScale)));
                    bounds.Encapsulate(space.GlobalPointToLocal(rt.Canvas.Slot.LocalPointToGlobal(area.ExtentMax / rt.Canvas.UnitScale)));
                }
                return bounds;
            }

            [HarmonyTranspiler]
            [HarmonyPatch("OnCommonUpdate")]
            private static IEnumerable<CodeInstruction> OnCommonUpdateTranspiler(IEnumerable<CodeInstruction> codes)
            {
                foreach (var code in codes)
                {
                    yield return code;
                    if (code.Calls(computeBoundingBoxMethod))
                    {
                        yield return new CodeInstruction(OpCodes.Ldloc_0);
                        yield return new CodeInstruction(OpCodes.Ldloc_3);
                        yield return new CodeInstruction(OpCodes.Call, boundUIXMethod);
                    }
                }
            }
        }

        [HarmonyPatch(typeof(RectTransform), "BuildInspectorUI")]
        internal class RectTransformPatch
        {
            public static void Postfix(RectTransform __instance, UIBuilder ui)
            {
                ui.Button("Visualize Preferred Area").LocalPressed += (IButton b, ButtonEventData e) =>
                {
                    var rt = __instance;
                    if (rt == null || rt.Canvas == null) return;
                    b.Enabled = false;
                    __instance.StartTask(async () =>
                    {
                        while (!b.IsRemoved)
                        {
                            var hori = rt.GetHorizontalMetrics().preferred;
                            var vert = rt.GetVerticalMetrics().preferred;

                            var area = rt.ComputeGlobalComputeRect();
                            var pos = rt.Canvas.Slot.LocalPointToGlobal(new float3(area.Center / rt.Canvas.UnitScale));
                            var size = rt.Canvas.Slot.LocalScaleToGlobal(new float3(hori, vert) / rt.Canvas.UnitScale);

                            rt.World.Debug.Box(pos, size, colorX.Blue.SetA(0.25f), rt.Canvas.Slot.GlobalRotation);
                            await default(NextUpdate);
                        }
                    });
                };
            }
        }
    }
}