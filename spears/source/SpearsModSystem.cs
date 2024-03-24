using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;

namespace Spears;

public class SpearsModSystem : ModSystem
{
    public override void Start(ICoreAPI api)
    {
        api.RegisterItemClass("Spears:SpearItem", typeof(SpearItem));
        api.RegisterItemClass("Spears:PikeItem", typeof(PikeItem));
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
        if (slot.Itemstack?.Item is not PikeItem pike) return true;

        IClientPlayer player = _clientApi.World.Player;
        bool handled = pike.OnMouseWheel(slot, player, args.deltaPrecise);
        
        return !handled;
    }
}

public class SpearItem : Item
{
    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        //SpearStats stats = Attributes["spearStats"].AsObject<SpearStats>();
        //_fsm = new(api, this, stats);
    }

    public override void OnHeldRenderOpaque(ItemSlot inSlot, IClientPlayer byPlayer)
    {
        base.OnHeldRenderOpaque(inSlot, byPlayer);

        //_fsm?.OnRender(inSlot, byPlayer);
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