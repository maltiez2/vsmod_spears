using AnimationManagerLib.API;
using MaltiezFSM.Framework.Simplified.Systems;
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using static OpenTK.Graphics.OpenGL.GL;

namespace Spears;

public class SpearFsm : BaseControls
{
    public SpearFsm(ICoreAPI api, CollectibleObject collectible, SpearStats stats) : base(api, collectible)
    {
        if (api is ICoreClientAPI clientApi)
        {
            AnimationSystem = new(clientApi, debugName: "spears-animations");
            AttacksSystem = new(api, AnimationSystem, GetAttacks(stats, clientApi), debugName: "spears-attacks");

            AnimationSystem.RegisterAnimations(GetAnimations());
        }

        Stats = stats;
    }

    public virtual void OnRender(ItemSlot slot, IClientPlayer player)
    {
        AnimationSystem?.Track(player);
    }

    protected readonly SpearAnimationSystem? AnimationSystem;
    protected readonly SpearAttacksSystem? AttacksSystem;
    protected readonly SpearStats Stats;

    protected override bool OnStartAttack(ItemSlot slot, IPlayer player, StanceType stanceType, bool blocking)
    {
        Console.WriteLine($"OnStartAttack: {stanceType} (blocking: {blocking})");
        AttacksSystem?.Start(GetAttackAnimationType(stanceType, blocking), player, slot, terrainCollisionCallback: () => OnTerrainHit(slot, player, blocking));
        return true;
    }
    protected override void OnCancelAttack(ItemSlot slot, IPlayer player, StanceType stanceType, bool blocking)
    {
        AttacksSystem?.Stop(GetAttackAnimationType(stanceType, blocking), player, _easeOutTime);
        Console.WriteLine($"OnCancelAttack: {stanceType}");
    }
    protected override bool OnStartBlock(ItemSlot slot, IPlayer player, StanceType stanceType, bool attacking)
    {
        Console.WriteLine($"OnStartBlock: {stanceType} (attacking: {attacking})");
        AnimationSystem?.Play(player, GetBlockAnimationType(stanceType));
        return true;
    }
    protected override void OnCancelBlock(ItemSlot slot, IPlayer player, StanceType stanceType, bool attacking)
    {
        Console.WriteLine($"OnCancelBlock: {stanceType}");
        AnimationSystem?.EaseOut(player, GetBlockAnimationType(stanceType), _easeOutTime);
    }
    protected override void OnStanceChange(ItemSlot slot, IPlayer player, StanceType newStance, bool blocking)
    {
        Console.WriteLine($"OnStanceChange: {newStance} (blocking: {blocking})");
        AnimationSystem?.Play(player, GetStanceAnimationType(newStance));
    }
    protected override void OnDeselected(ItemSlot slot, IPlayer player)
    {
        Console.WriteLine($"OnDeselected");
        AnimationSystem?.EaseOut(player, _easeOutTime);
    }

    protected virtual bool OnTerrainHit(ItemSlot slot, IPlayer player, bool blocking)
    {
        CancelAttack(slot, player, blocking);
        return false;
    }


    //private AdvancedParticleProperties _headHitParticles;
    //private AdvancedParticleProperties _shaftHitParticles;
    private readonly TimeSpan _easeOutTime = TimeSpan.FromSeconds(0.6);

    private static SpearAnimationSystem.AnimationType GetAttackAnimationType(StanceType stance, bool blocking)
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
    private static SpearAnimationSystem.AnimationType GetBlockAnimationType(StanceType stance)
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
    private static SpearAnimationSystem.AnimationType GetStanceAnimationType(StanceType stance)
    {
        return stance switch
        {
            StanceType.OneHandedUpper => SpearAnimationSystem.AnimationType.High1hStance,
            StanceType.OneHandedLower => SpearAnimationSystem.AnimationType.Low1hStance,
            StanceType.TwoHandedUpper => SpearAnimationSystem.AnimationType.High2hStance,
            StanceType.TwoHandedLower => SpearAnimationSystem.AnimationType.Low2hStance,
            _ => throw new NotImplementedException()
        };
    }

    private static Dictionary<SpearAnimationSystem.AnimationType, MeleeAttack> GetAttacks(SpearStats stats, ICoreClientAPI api)
    {
        Dictionary<SpearAnimationSystem.AnimationType, MeleeAttack> result = new();

        SpearAnimationSystem.AnimationType[] animationTypes = Enum.GetValues<SpearAnimationSystem.AnimationType>();
        foreach ((SpearAnimationSystem.AnimationType type, MeleeAttack? attack) in animationTypes.Select(type => (type, GetAttack(type, stats, api))))
        {
            if (attack != null) result.Add(type, attack);
        }

        return result;
    }
    private static MeleeAttack? GetAttack(SpearAnimationSystem.AnimationType attackType, SpearStats stats, ICoreClientAPI api)
    {
        HitWindow hitWindow;

        switch (attackType)
        {
            case SpearAnimationSystem.AnimationType.Low1hAttack:
            case SpearAnimationSystem.AnimationType.High1hAttack:
                hitWindow = new(TimeSpan.FromMilliseconds(stats.OneHandedAttackWindowMs[0]), TimeSpan.FromMilliseconds(stats.OneHandedAttackWindowMs[1]));
                break;
            case SpearAnimationSystem.AnimationType.Low2hAttack:
            case SpearAnimationSystem.AnimationType.High2hAttack:
                hitWindow = new(TimeSpan.FromMilliseconds(stats.TwoHandedAttackWindowMs[0]), TimeSpan.FromMilliseconds(stats.TwoHandedAttackWindowMs[1]));
                break;
            case SpearAnimationSystem.AnimationType.Low1hBlockAttack:
            case SpearAnimationSystem.AnimationType.High1hBlockAttack:
            case SpearAnimationSystem.AnimationType.Low2hBlockAttack:
            case SpearAnimationSystem.AnimationType.High2hBlockAttack:
                hitWindow = new(TimeSpan.FromMilliseconds(stats.PushAttackWindowMs[0]), TimeSpan.FromMilliseconds(stats.PushAttackWindowMs[1]));
                break;
            default: return null;
        }

        List<MeleeAttackDamageType> damageTypes = new();
        MeleeAttackDamageType? headDamage = GetAttackDamageType(attackType, stats);
        MeleeAttackDamageType? shaftDamage = GetShaftDamageType(attackType, stats);
        if (headDamage != null) damageTypes.Add(headDamage);
        if (shaftDamage != null) damageTypes.Add(shaftDamage);

        return new MeleeAttack(api, hitWindow, damageTypes, stats.MaxReach);
    }
    private static MeleeAttackDamageType? GetAttackDamageType(SpearAnimationSystem.AnimationType attackType, SpearStats stats)
    {
        switch (attackType)
        {
            case SpearAnimationSystem.AnimationType.High1hAttack:
            case SpearAnimationSystem.AnimationType.Low1hAttack:
                return new MeleeAttackDamageType(
                        damage: stats.OneHandedDamage,
                        damageType: EnumDamageType.PiercingAttack,
                        collider: new(stats.SpearHeadCollider),
                        tier: stats.OneHandedTier,
                        hitSound: new(stats.HeadHitEntitySound),
                        terrainSound: new(stats.HeadHitTerrainSound)
                    );
            case SpearAnimationSystem.AnimationType.High2hAttack:
            case SpearAnimationSystem.AnimationType.Low2hAttack:
                return new MeleeAttackDamageType(
                        damage: stats.TwoHandedDamage,
                        damageType: EnumDamageType.PiercingAttack,
                        collider: new(stats.SpearHeadCollider),
                        tier: stats.TwoHandedTier,
                        hitSound: new(stats.HeadHitEntitySound),
                        terrainSound: new(stats.HeadHitTerrainSound)
                    );
            case SpearAnimationSystem.AnimationType.Low1hBlockAttack:
            case SpearAnimationSystem.AnimationType.High1hBlockAttack:
            case SpearAnimationSystem.AnimationType.Low2hBlockAttack:
            case SpearAnimationSystem.AnimationType.High2hBlockAttack:
                return new MeleeAttackDamageType(
                        damage: stats.ShaftDamage,
                        damageType: EnumDamageType.BluntAttack,
                        collider: new(stats.ShaftCollider),
                        tier: 0,
                        hitSound: new(stats.ShaftHitEntitySound),
                        terrainSound: new(stats.ShaftHitTerrainSound),
                        knockback: stats.PushKnockback
                    );
            default: return null;
        }
    }
    private static MeleeAttackDamageType? GetShaftDamageType(SpearAnimationSystem.AnimationType attackType, SpearStats stats)
    {
        switch (attackType)
        {
            case SpearAnimationSystem.AnimationType.High2hAttack:
            case SpearAnimationSystem.AnimationType.Low2hAttack:
            case SpearAnimationSystem.AnimationType.High1hAttack:
            case SpearAnimationSystem.AnimationType.Low1hAttack:
                return new MeleeAttackDamageType(
                        damage: stats.ShaftDamage,
                        damageType: EnumDamageType.BluntAttack,
                        collider: new(stats.ShaftCollider),
                        tier: 0,
                        hitSound: new(stats.ShaftHitEntitySound),
                        terrainSound: new(stats.ShaftHitTerrainSound),
                        knockback: stats.ShaftHitKnockback
                    );
            default: return null;
        }
    }

    private static Dictionary<SpearAnimationSystem.AnimationType, List<SpearAnimationSystem.AnimationParameters>> GetAnimations()
    {
        Dictionary<SpearAnimationSystem.AnimationType, List<SpearAnimationSystem.AnimationParameters>> result = new();

        SpearAnimationSystem.AnimationType[] animationTypes = Enum.GetValues<SpearAnimationSystem.AnimationType>();
        foreach ((SpearAnimationSystem.AnimationType type, SpearAnimationSystem.AnimationParameters parameters) in animationTypes.Select(type => (type, GetAnimation(type))))
        {
            result.Add(type, new() { parameters });
        }

        return result;
    }
    private static SpearAnimationSystem.AnimationParameters GetAnimation(SpearAnimationSystem.AnimationType animationType)
    {
        return animationType switch
        {
            SpearAnimationSystem.AnimationType.Low1hAttack => new(
                "low-1h-attack",
                RunParameters.EaseIn(TimeSpan.FromMilliseconds(500), 10, ProgressModifierType.Sin),
                RunParameters.EaseIn(TimeSpan.FromMilliseconds(500), 20, ProgressModifierType.Quadratic)
                ),
            SpearAnimationSystem.AnimationType.High1hAttack => throw new NotImplementedException(),
            SpearAnimationSystem.AnimationType.Low2hAttack => throw new NotImplementedException(),
            SpearAnimationSystem.AnimationType.High2hAttack => throw new NotImplementedException(),
            SpearAnimationSystem.AnimationType.Low1hBlockAttack => throw new NotImplementedException(),
            SpearAnimationSystem.AnimationType.High1hBlockAttack => throw new NotImplementedException(),
            SpearAnimationSystem.AnimationType.Low2hBlockAttack => throw new NotImplementedException(),
            SpearAnimationSystem.AnimationType.High2hBlockAttack => throw new NotImplementedException(),
            SpearAnimationSystem.AnimationType.Low1hBlock => throw new NotImplementedException(),
            SpearAnimationSystem.AnimationType.High1hBlock => throw new NotImplementedException(),
            SpearAnimationSystem.AnimationType.Low2hBlock => throw new NotImplementedException(),
            SpearAnimationSystem.AnimationType.High2hBlock => throw new NotImplementedException(),
            SpearAnimationSystem.AnimationType.Low1hStance => throw new NotImplementedException(),
            SpearAnimationSystem.AnimationType.High1hStance => throw new NotImplementedException(),
            SpearAnimationSystem.AnimationType.Low2hStance => throw new NotImplementedException(),
            SpearAnimationSystem.AnimationType.High2hStance => throw new NotImplementedException(),
            _ => throw new NotImplementedException()
        };
    }
}

public sealed class SpearStats
{
    public float MaxReach { get; set; }
    public float OneHandedDamage { get; set; }
    public float TwoHandedDamage { get; set; }
    public float ShaftDamage { get; set; }
    public float PushKnockback { get; set; }
    public float ShaftHitKnockback { get; set; }
    public float TwoHandedKnockback { get; set; }
    public float OneHandedKnockback { get; set; }
    public int OneHandedTier { get; set; }
    public int TwoHandedTier { get; set; }
    public float[] SpearHeadCollider { get; set; } = new float[6];
    public float[] ShaftCollider { get; set; } = new float[6];
    public string? HeadHitTerrainSound { get; set; } = null;
    public string? HeadHitEntitySound { get; set; } = null;
    public string? ShaftHitTerrainSound { get; set; } = null;
    public string? ShaftHitEntitySound { get; set; } = null;
    public float[] OneHandedAttackWindowMs { get; set; } = new float[2];
    public float[] TwoHandedAttackWindowMs { get; set; } = new float[2];
    public float[] PushAttackWindowMs { get; set; } = new float[2];
}
