using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Spears;

public class SpearsModSystem : ModSystem
{
    public override void Start(ICoreAPI api)
    {
        api.RegisterItemClass("Spears:SpearItem", typeof(SpearItem));
        api.RegisterItemClass("Spears:PikeItem", typeof(PikeItem));
    }
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

    public override void OnHeldAttackStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, ref EnumHandHandling handling)
    {
        handling = EnumHandHandling.PreventDefault;
    }

    private PikeFsm? _fsm;
}