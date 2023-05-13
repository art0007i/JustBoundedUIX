using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Reflection;
using System.Text;
using BaseX;
using FrooxEngine;
using FrooxEngine.UIX;
using HarmonyLib;
using NeosModLoader;
using System.Linq;
using System.Threading.Tasks;

namespace JustBoundedUIX
{
    public class JustBoundedUIX : NeosMod
    {
        public const float GizmoOffset = .02f;
        public override string Author => "art0007i";
        public override string Link => "https://github.com/art0007i/JustBoundedUIX";
        public override string Name => "JustBoundedUIX";
        public override string Version => "1.0.0";

        public override void OnEngineInit()
        {
            Harmony harmony = new Harmony($"me.art0007i.JustBoundedUIX");
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
                func = AccessTools.Method(typeof(SlotPositioning), nameof(SlotPositioning.CreatePivotAtCenter),
                    new Type[] {
                        typeof(Slot),
                        typeof(BoundingBox).MakeByRefType(),
                        typeof(bool)
                });
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

            private static readonly MethodInfo getGlobalPositionMethod = typeof(Slot).GetProperty(nameof(Slot.GlobalPosition), AccessTools.allDeclared).GetMethod;

            private static readonly Type RotationGizmoType = typeof(RotationGizmo);
            private static readonly MethodInfo uixBoundCenterMethod = typeof(SlotGizmoPatches).GetMethod(nameof(UIXBoundCenter), AccessTools.allDeclared);

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
            private static IEnumerable<CodeInstruction> OnCommonUpdateTranspiler(IEnumerable<CodeInstruction> codeInstructions)
            {
                var instructions = codeInstructions.ToList();

                var globalPositionIndex = instructions.FindIndex(instruction => instruction.Calls(getGlobalPositionMethod));

                if (globalPositionIndex < 0)
                    return instructions;

                instructions[globalPositionIndex] = new CodeInstruction(OpCodes.Call, uixBoundCenterMethod);

                var computeIndex = instructions.FindIndex(globalPositionIndex, instruction => instruction.Calls(computeBoundingBoxMethod));

                if (computeIndex < 0)
                    return instructions;

                instructions.Insert(computeIndex + 1, instructions[computeIndex - 5]);
                instructions.Insert(computeIndex + 2, instructions[computeIndex - 3]);
                instructions.Insert(computeIndex + 3, new CodeInstruction(OpCodes.Call, boundUIXMethod));

                return instructions;
            }

            [HarmonyPostfix]
            [HarmonyPatch("RegenerateButtons")]
            private static void RegenerateButtonsPostfix(SlotGizmo __instance, SyncRef<Slot> ____buttonsSlot)
            {
                var moveableRect = JustBoundedUIX.GetRectTransform(__instance.TargetSlot) == null;

                if (____buttonsSlot.Target.GetComponentInChildren<SlotGizmoButton>(button => ((SyncRef<Worker>)button.TryGetField("_worker")).Target?.GetType() == RotationGizmoType) is SlotGizmoButton sgb)
                    sgb.Slot.ActiveSelf = !moveableRect;
            }
            private static float3 UIXBoundCenter(Slot target)
            {
                var rt = JustBoundedUIX.GetRectTransform(target);
                if (rt == null)
                    return target.GlobalPosition;

                var bounds = JustBoundedUIX.GetBoundingBox(rt);

                return bounds.Center - JustBoundedUIX.GizmoOffset * rt.Canvas.Slot.Forward;
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

                            rt.World.Debug.Box(pos, size, color.Blue.SetA(0.25f), rt.Canvas.Slot.GlobalRotation);
                            await default(NextUpdate);
                        }
                    });
                };
            }
        }
    }
}