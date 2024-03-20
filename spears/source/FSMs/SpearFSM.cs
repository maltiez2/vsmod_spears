using System;
using Vintagestory.API.Common;

namespace Spears;

public class SpearFsm : BaseControls
{
    public SpearFsm(ICoreAPI api, CollectibleObject collectible) : base(api, collectible)
    {

    }

    protected override bool OnStartAttack(ItemSlot slot, IPlayer player, StanceType stanceType, bool blocking)
    {
        Console.WriteLine($"OnStartAttack: {stanceType} (blocking: {blocking})");
        return true;
    }
    protected override void OnCancelAttack(ItemSlot slot, IPlayer player, StanceType stanceType)
    {
        Console.WriteLine($"OnCancelAttack: {stanceType}");
    }
    protected override bool OnStartBlock(ItemSlot slot, IPlayer player, StanceType stanceType, bool attacking)
    {
        Console.WriteLine($"OnStartBlock: {stanceType} (attacking: {attacking})");
        return true;
    }
    protected override void OnCancelBlock(ItemSlot slot, IPlayer player, StanceType stanceType)
    {
        Console.WriteLine($"OnCancelBlock: {stanceType}");
    }
    protected override void OnStanceChange(ItemSlot slot, IPlayer player, StanceType newStance, bool blocking)
    {
        Console.WriteLine($"OnStanceChange: {newStance} (blocking: {blocking})");
    }
    protected override void OnDeselected(ItemSlot slot, IPlayer player)
    {
        Console.WriteLine($"OnDeselected");
    }
}
