using MaltiezFSM.Framework.Simplified.Systems;
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Spears;

public class SpearFsm : BaseControls
{
    public SpearFsm(ICoreAPI api, CollectibleObject collectible) : base(api, collectible)
    {
        if (api is ICoreClientAPI clientApi) AnimationSystem = new(clientApi);
    }

    public virtual void OnRender(ItemSlot slot, IClientPlayer player)
    {
        AnimationSystem?.Track(player);
    }

    protected readonly SpearAnimationSystem? AnimationSystem;
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


    private SpearAnimationSystem.AnimationType GetAttackAnimationType(StanceType stance, bool blocking)
    {
        return stance switch
        {
            StanceType.OneHandedUpper => blocking ? SpearAnimationSystem.AnimationType.High1hBlockAttack : SpearAnimationSystem.AnimationType.High1hAttack,
            StanceType.OneHandedLower => blocking ? SpearAnimationSystem.AnimationType.Low1hBlockAttack : SpearAnimationSystem.AnimationType.Low1hAttack,
            StanceType.TwoHandedUpper => blocking ? SpearAnimationSystem.AnimationType.High2hBlockAttack : SpearAnimationSystem.AnimationType.High2hAttack,
            StanceType.TwoHandedLower => blocking ? SpearAnimationSystem.AnimationType.Low2hBlockAttack : SpearAnimationSystem.AnimationType.Low2hAttack,
            _ => throw new NotImplementedException()
        };
    }
    private SpearAnimationSystem.AnimationType GetBlockAnimationType(StanceType stance)
    {
        return stance switch
        {
            StanceType.OneHandedUpper => SpearAnimationSystem.AnimationType.High1hBlock,
            StanceType.OneHandedLower => SpearAnimationSystem.AnimationType.Low1hBlock,
            StanceType.TwoHandedUpper => SpearAnimationSystem.AnimationType.High2hBlock,
            StanceType.TwoHandedLower => SpearAnimationSystem.AnimationType.Low2hBlock,
            _ => throw new NotImplementedException()
        };
    }
}

