using AnimationManagerLib.API;
using MaltiezFSM;
using MaltiezFSM.Framework.Simplified.Systems;
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Spears;

public class SpearFsm : BaseControls
{
    public SpearFsm(ICoreAPI api, CollectibleObject collectible, SpearStats stats) : base(api, collectible)
    {
        Console.WriteLine("Init SpearFsm");

        if (api is ICoreClientAPI clientApi)
        {
            AnimationSystem = new(clientApi, debugName: "spears-animations");
            _attacksForDebugRendering = GetAttacks(stats, clientApi);
            AttacksSystem = new(api, AnimationSystem, _attacksForDebugRendering, debugName: "spears-attacks");

            AnimationSystem.RegisterAnimations(GetAnimations());

            MaltiezFSM.Systems.IParticleEffectsManager? effectsManager = api.ModLoader.GetModSystem<FiniteStateMachineSystem>().ParticleEffects;
            AdvancedParticleProperties? entityHit = effectsManager?.Get("entity-hit-blood", "maltiezspears");
            AdvancedParticleProperties? terrainHit = effectsManager?.Get("terrain-hit-sparks", "maltiezspears");

            if (terrainHit != null) _terrainHitEffects.Add("*stone*", terrainHit);
            if (entityHit != null) _entityHitEffects.Add("*", entityHit);
        }
        else
        {
            AttacksSystem = new(api, AnimationSystem, GetAttacks(stats, api), debugName: "spears-attacks");
        }

        Stats = stats;
    }

    private Dictionary<SpearAnimationSystem.AnimationType, MeleeAttack>? _attacksForDebugRendering;
    private SpearAnimationSystem.AnimationType? _lastAttack;
    public virtual void OnRender(ItemSlot slot, IClientPlayer player)
    {
        AnimationSystem?.Track(player);

        if (_attacksForDebugRendering != null && _lastAttack != null)
        {
            _attacksForDebugRendering[_lastAttack.Value].RenderDebugColliders(player, slot);
        }
    }

    protected readonly SpearAnimationSystem? AnimationSystem;
    protected readonly SpearAttacksSystem? AttacksSystem;
    protected readonly SpearStats Stats;

    protected override bool OnStartAttack(ItemSlot slot, IPlayer player, StanceType stanceType, bool blocking)
    {
        Console.WriteLine($"OnStartAttack: {stanceType} (blocking: {blocking})");
        _lastAttack = GetAttackAnimationType(stanceType, blocking);
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

    private readonly TimeSpan _easeOutTime = TimeSpan.FromSeconds(0.6);
    private readonly Dictionary<string, AdvancedParticleProperties> _terrainHitEffects = new();
    private readonly Dictionary<string, AdvancedParticleProperties> _entityHitEffects = new();

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

    private Dictionary<SpearAnimationSystem.AnimationType, MeleeAttack> GetAttacks(SpearStats stats, ICoreAPI api)
    {
        Dictionary<SpearAnimationSystem.AnimationType, MeleeAttack> result = new();

        SpearAnimationSystem.AnimationType[] animationTypes = Enum.GetValues<SpearAnimationSystem.AnimationType>();
        foreach ((SpearAnimationSystem.AnimationType type, MeleeAttack? attack) in animationTypes.Select(type => (type, GetAttack(type, stats, api))))
        {
            if (attack != null) result.Add(type, attack);
        }

        return result;
    }
    private MeleeAttack? GetAttack(SpearAnimationSystem.AnimationType attackType, SpearStats stats, ICoreAPI api)
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

        return new MeleeAttack(api as ICoreClientAPI, hitWindow, damageTypes, stats.MaxReach);
    }
    private MeleeAttackDamageType? GetAttackDamageType(SpearAnimationSystem.AnimationType attackType, SpearStats stats)
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
                        hitSound: stats.HeadHitEntitySound != null ? new(stats.HeadHitEntitySound) : null,
                        terrainSound: stats.HeadHitTerrainSound != null ? new(stats.HeadHitTerrainSound) : null

                    )
                {
                    TerrainCollisionParticles = _terrainHitEffects,
                    EntityCollisionParticles = _entityHitEffects
                };
            case SpearAnimationSystem.AnimationType.High2hAttack:
            case SpearAnimationSystem.AnimationType.Low2hAttack:
                return new MeleeAttackDamageType(
                        damage: stats.TwoHandedDamage,
                        damageType: EnumDamageType.PiercingAttack,
                        collider: new(stats.SpearHeadCollider),
                        tier: stats.TwoHandedTier,
                        hitSound: stats.HeadHitEntitySound != null ? new(stats.HeadHitEntitySound) : null,
                        terrainSound: stats.HeadHitTerrainSound != null ? new(stats.HeadHitTerrainSound) : null,
                        knockback: stats.TwoHandedKnockback
                    )
                {
                    TerrainCollisionParticles = _terrainHitEffects,
                    EntityCollisionParticles = _entityHitEffects
                };
            case SpearAnimationSystem.AnimationType.Low1hBlockAttack:
            case SpearAnimationSystem.AnimationType.High1hBlockAttack:
            case SpearAnimationSystem.AnimationType.Low2hBlockAttack:
            case SpearAnimationSystem.AnimationType.High2hBlockAttack:
                return new MeleeAttackDamageType(
                        damage: stats.ShaftDamage,
                        damageType: EnumDamageType.BluntAttack,
                        collider: new(stats.ShaftCollider),
                        tier: 0,
                        hitSound: stats.ShaftHitEntitySound != null ? new(stats.ShaftHitEntitySound) : null,
                        terrainSound: stats.ShaftHitTerrainSound != null ? new(stats.ShaftHitTerrainSound) : null,
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
                        hitSound: stats.ShaftHitEntitySound != null ? new(stats.ShaftHitEntitySound) : null,
                        terrainSound: stats.ShaftHitTerrainSound != null ? new(stats.ShaftHitTerrainSound) : null,
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
        SpearAnimationSystem.AnimationParameters empty = new(
            "spearsempty",
            RunParameters.EaseOut(TimeSpan.FromMilliseconds(1000))
            );


        return animationType switch
        {
            SpearAnimationSystem.AnimationType.Low1hAttack => new(
                "spear-high-2h-attack",
                RunParameters.EaseIn(TimeSpan.FromMilliseconds(200), 10, ProgressModifierType.Bounce),
                RunParameters.EaseIn(TimeSpan.FromMilliseconds(300), 15, ProgressModifierType.Cubic),
                RunParameters.EaseOut(TimeSpan.FromMilliseconds(1000), ProgressModifierType.Bounce)
                ),
            SpearAnimationSystem.AnimationType.High1hAttack => empty,
            SpearAnimationSystem.AnimationType.Low2hAttack => empty,
            SpearAnimationSystem.AnimationType.High2hAttack => empty,
            SpearAnimationSystem.AnimationType.Low1hBlockAttack => empty,
            SpearAnimationSystem.AnimationType.High1hBlockAttack => empty,
            SpearAnimationSystem.AnimationType.Low2hBlockAttack => empty,
            SpearAnimationSystem.AnimationType.High2hBlockAttack => empty,
            SpearAnimationSystem.AnimationType.Low1hBlock => empty,
            SpearAnimationSystem.AnimationType.High1hBlock => empty,
            SpearAnimationSystem.AnimationType.Low2hBlock => empty,
            SpearAnimationSystem.AnimationType.High2hBlock => empty,
            SpearAnimationSystem.AnimationType.Low1hStance => empty,
            SpearAnimationSystem.AnimationType.High1hStance => empty,
            SpearAnimationSystem.AnimationType.Low2hStance => empty,
            SpearAnimationSystem.AnimationType.High2hStance => empty,
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
