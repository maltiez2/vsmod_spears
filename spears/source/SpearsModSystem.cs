using HarmonyLib;
using Javelins;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;

namespace Spears;

public class SpearsModSystem : ModSystem
{
    public override void Start(ICoreAPI api)
    {
        api.RegisterItemClass("Spears:JavelinItem", typeof(JavelinItem));
        api.RegisterItemClass("Spears:SpearItem", typeof(SpearItem));
        api.RegisterItemClass("Spears:PikeItem", typeof(PikeItem));

        new Harmony("maltiezspears").PatchAll();
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        _clientApi = api;

        new Harmony("maltiezspears").Patch(
            typeof(HudHotbar).GetMethod("OnMouseWheel", AccessTools.all),
            prefix: new HarmonyMethod(AccessTools.Method(typeof(SpearsModSystem), nameof(OnMouseWheel)))
            );
    }

    public override void Dispose()
    {
        new Harmony("maltiezspears").Unpatch(typeof(HudHotbar).GetMethod("OnMouseWheel", AccessTools.all), HarmonyPatchType.Prefix, "maltiezspears");
    }

    private static ICoreClientAPI? _clientApi;
    private static int _prevValue = int.MaxValue;
    private static bool OnMouseWheel(MouseWheelEventArgs args)
    {
        if (_clientApi == null) return true;
        if (args.delta == 0 || args.value == _prevValue) return true;
        _prevValue = args.value;

        ItemSlot slot = _clientApi.World.Player.InventoryManager.ActiveHotbarSlot;

        if (slot.Itemstack?.Item is PikeItem pike)
        {
            IClientPlayer player = _clientApi.World.Player;
            bool handled = pike.OnMouseWheel(slot, player, args.deltaPrecise);

            return !handled;
        }

        if (slot.Itemstack?.Item is SpearItem spear)
        {
            IClientPlayer player = _clientApi.World.Player;
            bool handled = spear.OnMouseWheel(slot, player, args.deltaPrecise);

            return !handled;
        }

        return true;
    }
}

public class JavelinItem : Item
{
    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        JavelinStats stats = Attributes["javelinStats"].AsObject<JavelinStats>();
        _fsm = new(api, this, stats);
    }

    public override void OnHeldRenderOpaque(ItemSlot inSlot, IClientPlayer byPlayer)
    {
        base.OnHeldRenderOpaque(inSlot, byPlayer);

        _fsm?.OnRender(inSlot, byPlayer);
    }

    public override void OnHeldAttackStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, ref EnumHandHandling handling)
    {
        handling = EnumHandHandling.PreventDefault;
    }

    private JavelinFsm? _fsm;
}

public class SpearItem : Item
{
    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        SpearStats stats = Attributes["spearStats"].AsObject<SpearStats>();
        _fsm = new(api, this, stats);
    }

    public override void OnHeldRenderOpaque(ItemSlot inSlot, IClientPlayer byPlayer)
    {
        base.OnHeldRenderOpaque(inSlot, byPlayer);

        _fsm?.OnRender(inSlot, byPlayer);
    }

    public bool OnMouseWheel(ItemSlot slot, IClientPlayer byPlayer, float delta)
    {
        if (!byPlayer.Entity.Controls.RightMouseDown) return false;

        return _fsm?.ChangeGrip(slot, byPlayer, delta) ?? false;
    }

    public override void OnHeldAttackStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, ref EnumHandHandling handling)
    {
        handling = EnumHandHandling.PreventDefault;
    }

    private SpearFsm? _fsm;
}

public class PikeItem : Item
{
    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        PikeStats stats = Attributes["pikeStats"].AsObject<PikeStats>();
        _fsm = new(api, this, stats);
    }

    public override void OnHeldRenderOpaque(ItemSlot inSlot, IClientPlayer byPlayer)
    {
        base.OnHeldRenderOpaque(inSlot, byPlayer);

        _fsm?.OnRender(inSlot, byPlayer);
    }

    public bool OnMouseWheel(ItemSlot slot, IClientPlayer byPlayer, float delta)
    {
        if (!byPlayer.Entity.Controls.RightMouseDown) return false;

        return _fsm?.ChangeGrip(slot, byPlayer, delta) ?? false;
    }

    public override void OnHeldAttackStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, ref EnumHandHandling handling)
    {
        handling = EnumHandHandling.PreventDefault;
    }

    private PikeFsm? _fsm;
    private ICoreClientAPI? _clientApi;
}

public static class HarmonyPatches
{
    // Make a transpiler patch for this. Replace fpModeHandShader.Uniform("depthOffset", -0.3f); with fpModeHandShader.Uniform("depthOffset", GetOffset(modSys)).
    // The variable to pass into the method is the ModSystemFpHands modSys field.
    [HarmonyPatch(typeof(EntityPlayerShapeRenderer), "DoRender3DOpaque")]
    public class EntityShapeRendererPatch
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new(instructions);
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode == OpCodes.Ldc_R4 && (float)codes[i].operand == -0.3f)
                {
                    codes[i].opcode = OpCodes.Call;
                    codes[i].operand = typeof(HarmonyPatches).GetMethod("GetOffset");

                    codes.Insert(i, new CodeInstruction(OpCodes.Ldarg_0));
                    codes.Insert(i + 1, new CodeInstruction(OpCodes.Ldfld, typeof(EntityPlayerShapeRenderer).GetField("modSys", BindingFlags.NonPublic | BindingFlags.Instance)));

                    break;
                }
            }

            return codes;
        }
    }

    public static float FpHandsOffset { get; set; } = DefaultFpHandsOffset;
    public const float DefaultFpHandsOffset = -0.3f;

    public static float GetOffset(ModSystemFpHands modSys) => FpHandsOffset;

    // Do the same thing for getReadyShader.
    [HarmonyPatch(typeof(EntityPlayerShapeRenderer), "getReadyShader")]
    public class EntityShapeRendererShaderPatch
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new(instructions);
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode == OpCodes.Ldc_R4 && (float)codes[i].operand == -0.3f)
                {
                    codes[i].opcode = OpCodes.Call;
                    codes[i].operand = typeof(HarmonyPatches).GetMethod("GetOffset");

                    codes.Insert(i, new CodeInstruction(OpCodes.Ldarg_0));
                    codes.Insert(i + 1, new CodeInstruction(OpCodes.Ldfld, typeof(EntityPlayerShapeRenderer).GetField("modSys", BindingFlags.NonPublic | BindingFlags.Instance)));

                    break;
                }
            }

            return codes;
        }
    }
}