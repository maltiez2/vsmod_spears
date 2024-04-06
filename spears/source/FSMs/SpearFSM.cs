﻿using AnimationManagerLib.API;
using MaltiezFSM;
using MaltiezFSM.Framework.Simplified.Systems;
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Spears;

public sealed class SpearFsm : SpearControls
{
    public static readonly TimeSpan AimToHoldDelay = TimeSpan.FromSeconds(0.5);

    public SpearFsm(ICoreAPI api, CollectibleObject collectible, SpearStats stats) : base(api, collectible, AimToHoldDelay)
    {
        if (api is ICoreClientAPI clientApi)
        {
            _animationSystem = new(clientApi, debugName: "pike-animations");
            _attacksForDebugRendering = GetAttacks(stats, clientApi);
            _attacksSystem = new(api, _animationSystem, _attacksForDebugRendering, debugName: "pike-attacks");

            _animationSystem.RegisterAnimations(GetAnimations(stats));

            MaltiezFSM.Systems.IParticleEffectsManager? effectsManager = api.ModLoader.GetModSystem<FiniteStateMachineSystem>().ParticleEffects;
            AdvancedParticleProperties? entityHitSuccess = effectsManager?.Get("entity-hit-success", "maltiezspears");
            AdvancedParticleProperties? entityHitFail = effectsManager?.Get("entity-hit-fail", "maltiezspears");
            AdvancedParticleProperties? terrainHitFail = effectsManager?.Get("terrain-hit-fail", "maltiezspears");

            if (terrainHitFail != null) _headCollisionEffects.TerrainCollisionParticles.Add("*", (terrainHitFail, 0));
            if (terrainHitFail != null) _shaftCollisionEffects.TerrainCollisionParticles.Add("*", (terrainHitFail, 0));
            if (entityHitSuccess != null) _headCollisionEffects.EntityCollisionParticles.Add("*", (entityHitSuccess, 0));
            if (entityHitFail != null) _shaftCollisionEffects.EntityCollisionParticles.Add("*", (entityHitFail, 0));

            if (stats.ShaftHitTerrainSound != null) _shaftCollisionEffects.TerrainCollisionSounds.Add("*", new(stats.ShaftHitTerrainSound));
            if (stats.HeadHitTerrainSound != null) _headCollisionEffects.TerrainCollisionSounds.Add("*", new(stats.HeadHitTerrainSound));
            if (stats.ShaftHitEntitySound != null) _shaftCollisionEffects.EntityCollisionSounds.Add("*", new(stats.ShaftHitEntitySound));
        }
        else
        {
            _attacksSystem = new(api, _animationSystem, GetAttacks(stats, api), debugName: "pike-attacks");
        }

        _stats = stats;
    }

    public bool ChangeGrip(ItemSlot slot, IPlayer player, float delta)
    {
        StanceType stance = GetStance(slot);
        bool oneHanded = stance == StanceType.OneHandedLower || stance == StanceType.OneHandedUpper;
        _grip = GameMath.Clamp(_grip + delta * _gripFactor, oneHanded ? _stats.GripMinLength1h : _stats.GripMinLength2h, oneHanded ? _stats.GripMaxLength1h : _stats.GripMaxLength2h);
        _animationSystem?.SetGrip(player, _grip);
        return true;
    }

    #region Debug render
    private Dictionary<SpearAnimationSystem.AnimationType, MeleeAttack>? _attacksForDebugRendering;
    private SpearAnimationSystem.AnimationType? _lastAttack;
    public void OnRender(ItemSlot slot, IClientPlayer player)
    {
        _animationSystem?.Track(player);

        if (_attacksForDebugRendering != null && _lastAttack != null)
        {
            _attacksForDebugRendering[_lastAttack.Value].RenderDebugColliders(player, slot);
        }
    }
    #endregion

    private readonly SpearAnimationSystem? _animationSystem;
    private readonly SpearAttacksSystem? _attacksSystem;
    private readonly SpearStats _stats;
    private readonly TimeSpan _easeOutTime = TimeSpan.FromSeconds(0.6);
    private readonly CollisionEffects _headCollisionEffects = new();
    private readonly CollisionEffects _shaftCollisionEffects = new();
    private const float _gripFactor = 0.1f;
    private float _grip = 0;

    private bool OnEntityHit(ItemSlot slot, IPlayer player)
    {
        slot.Itemstack.Item.DamageItem(player.Entity.World, player.Entity, slot, _stats.DurabilitySpentOnEntityHit);
        return false;
    }
    private bool OnTerrainHit(ItemSlot slot, IPlayer player)
    {
        CancelAttack(slot, player, Blocking(slot));
        slot.Itemstack.Item.DamageItem(player.Entity.World, player.Entity, slot, _stats.DurabilitySpentOnTerrainHit);
        return true;
    }

    #region Attacks
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
        List<MeleeAttackDamageType> damageTypes = new();
        MeleeAttackDamageType? headDamage = GetHeadDamageType(attackType, stats);
        MeleeAttackDamageType? shaftDamage = GetShaftDamageType(attackType, stats);
        if (headDamage != null) damageTypes.Add(headDamage);
        if (shaftDamage != null) damageTypes.Add(shaftDamage);

        return new MeleeAttack(api as ICoreClientAPI, GetAttackDuration(attackType, stats), damageTypes, stats.MaxReach);
    }
    private static TimeSpan GetAttackDuration(SpearAnimationSystem.AnimationType attackType, SpearStats stats)
    {
        switch (attackType)
        {
            case SpearAnimationSystem.AnimationType.Low1hAttack:
            case SpearAnimationSystem.AnimationType.High1hAttack:
                return TimeSpan.FromMilliseconds(stats.AttackDuration1hMs);

            case SpearAnimationSystem.AnimationType.Low2hAttack:
            case SpearAnimationSystem.AnimationType.High2hAttack:
                return TimeSpan.FromMilliseconds(stats.AttackDuration2hMs);

            case SpearAnimationSystem.AnimationType.Low1hBlockAttack:
            case SpearAnimationSystem.AnimationType.High1hBlockAttack:
            case SpearAnimationSystem.AnimationType.Low2hBlockAttack:
            case SpearAnimationSystem.AnimationType.High2hBlockAttack:
                return TimeSpan.FromMilliseconds(stats.AttackDurationBlockMs);

            default:
                return TimeSpan.Zero;
        }
    }
    private MeleeAttackDamageType? GetHeadDamageType(SpearAnimationSystem.AnimationType attackType, SpearStats stats)
    {
        switch (attackType)
        {
            case SpearAnimationSystem.AnimationType.High2hAttack:
            case SpearAnimationSystem.AnimationType.Low2hAttack:
                return new MeleeAttackDamageType(
                        damage: stats.Damage2hAttack,
                        damageType: EnumDamageType.PiercingAttack,
                        collider: new(stats.HeadCollider),
                        tier: stats.HeadDamageTier,
                        hitWindow: new(stats.AttackWindow2h[0] / stats.AttackDuration2hMs, stats.AttackWindow2h[1] / stats.AttackDuration2hMs),
                        knockback: stats.KnockbackAttack2h,
                        stagger: stats.StaggerAttack2h,
                        effects: _headCollisionEffects
                    );
            case SpearAnimationSystem.AnimationType.Low1hAttack:
            case SpearAnimationSystem.AnimationType.High1hAttack:
                return new MeleeAttackDamageType(
                        damage: stats.Damage1hAttack,
                        damageType: EnumDamageType.PiercingAttack,
                        collider: new(stats.HeadCollider),
                        tier: stats.HeadDamageTier,
                        hitWindow: new(stats.AttackWindow1h[0] / stats.AttackDuration1hMs, stats.AttackWindow1h[1] / stats.AttackDuration1hMs),
                        knockback: stats.KnockbackAttack1h,
                        stagger: stats.StaggerAttack1h,
                        effects: _headCollisionEffects
                    );
            default: return null;
        }
    }
    private MeleeAttackDamageType? GetShaftDamageType(SpearAnimationSystem.AnimationType attackType, SpearStats stats)
    {
        switch (attackType)
        {
            case SpearAnimationSystem.AnimationType.High2hAttack:
            case SpearAnimationSystem.AnimationType.Low2hAttack:
                return new MeleeAttackDamageType(
                        damage: stats.DamageShaft,
                        damageType: EnumDamageType.BluntAttack,
                        collider: new(stats.ShaftCollider),
                        tier: stats.ShaftDamageTier,
                        hitWindow: new(stats.ShaftCollisionWindow2h[0] / stats.AttackDuration2hMs, stats.ShaftCollisionWindow2h[1] / stats.AttackDuration2hMs),
                        knockback: stats.KnockbackAttack2h,
                        stagger: stats.StaggerAttack2h,
                        effects: _shaftCollisionEffects
                    );
            case SpearAnimationSystem.AnimationType.Low1hAttack:
            case SpearAnimationSystem.AnimationType.High1hAttack:
                return new MeleeAttackDamageType(
                        damage: stats.DamageShaft,
                        damageType: EnumDamageType.BluntAttack,
                        collider: new(stats.ShaftCollider),
                        tier: stats.ShaftDamageTier,
                        hitWindow: new(stats.ShaftCollisionWindow1h[0] / stats.AttackDuration1hMs, stats.ShaftCollisionWindow1h[1] / stats.AttackDuration1hMs),
                        knockback: stats.KnockbackAttack1h,
                        stagger: stats.StaggerAttack1h,
                        effects: _shaftCollisionEffects
                    );
            case SpearAnimationSystem.AnimationType.Low1hBlockAttack:
            case SpearAnimationSystem.AnimationType.High1hBlockAttack:
            case SpearAnimationSystem.AnimationType.Low2hBlockAttack:
            case SpearAnimationSystem.AnimationType.High2hBlockAttack:
                return new MeleeAttackDamageType(
                        damage: stats.DamageBlockAttack,
                        damageType: EnumDamageType.BluntAttack,
                        collider: new(stats.ShaftCollider),
                        tier: stats.ShaftDamageTier,
                        hitWindow: new(stats.AttackWindowBlock[0] / stats.AttackDurationBlockMs, stats.AttackWindow1h[1] / stats.AttackDurationBlockMs),
                        knockback: stats.KnockbackAttackBlock,
                        stagger: stats.StaggerAttackBlock,
                        effects: _headCollisionEffects
                    );
            default: return null;
        }
    }
    #endregion

    private static Dictionary<SpearAnimationSystem.AnimationType, List<SpearAnimationSystem.AnimationParameters>> GetAnimations(SpearStats stats)
    {
        Dictionary<SpearAnimationSystem.AnimationType, List<SpearAnimationSystem.AnimationParameters>> result = new();

        SpearAnimationSystem.AnimationType[] animationTypes = Enum.GetValues<SpearAnimationSystem.AnimationType>();
        foreach ((SpearAnimationSystem.AnimationType type, SpearAnimationSystem.AnimationParameters? parameters) in animationTypes.Select(type => (type, GetAnimation(type, stats))))
        {
            if (parameters != null) result.Add(type, new() { parameters.Value });
        }

        return result;
    }
    private static SpearAnimationSystem.AnimationParameters? GetAnimation(SpearAnimationSystem.AnimationType animationType, SpearStats stats)
    {
        SpearAnimationSystem.AnimationParameters empty = new(
            "spearsempty",
            RunParameters.EaseOut(TimeSpan.FromMilliseconds(1000))
            );


        return animationType switch
        {
            SpearAnimationSystem.AnimationType.High2hAttack => new(
                "pike-high-2h",
                RunParameters.EaseIn(TimeSpan.FromMilliseconds(400), 1, ProgressModifierType.Bounce),
                RunParameters.EaseIn(TimeSpan.FromMilliseconds(300), 2, ProgressModifierType.Cubic),
                RunParameters.EaseIn(TimeSpan.FromMilliseconds(600), 3, ProgressModifierType.Bounce)
                ),
            SpearAnimationSystem.AnimationType.Low2hAttack => new(
                "pike-low-2h",
                RunParameters.EaseIn(TimeSpan.FromMilliseconds(400), 1, ProgressModifierType.Bounce),
                RunParameters.EaseIn(TimeSpan.FromMilliseconds(300), 3, ProgressModifierType.Cubic),
                RunParameters.EaseIn(TimeSpan.FromMilliseconds(600), 4, ProgressModifierType.Bounce)
                ),
            SpearAnimationSystem.AnimationType.Idle1h => new(
                "pike-stances",
                RunParameters.EaseIn(TimeSpan.FromMilliseconds(1000), 0, ProgressModifierType.Bounce)
                ),
            SpearAnimationSystem.AnimationType.High2hStance => new(
                "pike-stances",
                RunParameters.EaseIn(TimeSpan.FromMilliseconds(1000), 2, ProgressModifierType.Bounce)
                ),
            SpearAnimationSystem.AnimationType.Low2hStance => new(
                "pike-stances",
                RunParameters.EaseIn(TimeSpan.FromMilliseconds(1000), 1, ProgressModifierType.Bounce)
                ),
            SpearAnimationSystem.AnimationType.Low1hAttack => empty,
            SpearAnimationSystem.AnimationType.High1hAttack => empty,
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
            SpearAnimationSystem.AnimationType.Idle2h => empty,
            SpearAnimationSystem.AnimationType.Aim => empty,
            SpearAnimationSystem.AnimationType.Throw => empty,
            _ => empty,
        };
    }
}

public sealed class SpearStats
{
    public float MaxReach { get; set; }

    #region Damage
    public float DamageShaft { get; set; }
    public float Damage1hAttack { get; set; }
    public float Damage2hAttack { get; set; }
    public float DamageBlockAttack { get; set; }
    public int ShaftDamageTier { get; set; }
    public int HeadDamageTier { get; set; }
    #endregion

    #region Knockback
    public float KnockbackAttack1h { get; set; }
    public float KnockbackAttack2h { get; set; }
    public float KnockbackAttackBlock { get; set; }
    public float StaggerAttack1h { get; set; }
    public float StaggerAttack2h { get; set; }
    public float StaggerAttackBlock { get; set; }
    #endregion

    #region Colliders
    public float[] HeadCollider { get; set; } = new float[6];
    public float[] ShaftCollider { get; set; } = new float[6];
    public float[] BlockCollider { get; set; } = new float[6];
    #endregion

    #region Timing
    public float AttackDuration1hMs { get; set; }
    public float AttackDuration2hMs { get; set; }
    public float AttackDurationBlockMs { get; set; }

    public float[] AttackWindow1h { get; set; } = new float[2];
    public float[] AttackWindow2h { get; set; } = new float[2];
    public float[] AttackWindowBlock { get; set; } = new float[2];

    public float[] ShaftCollisionWindow1h { get; set; } = new float[2];
    public float[] ShaftCollisionWindow2h { get; set; } = new float[2];
    #endregion

    #region Grip
    public float GripMaxLength1h { get; set; } = 1;
    public float GripMinLength1h { get; set; } = 0;
    public float GripMaxLength2h { get; set; } = 1;
    public float GripMinLength2h { get; set; } = 0;
    #endregion

    #region Sounds
    public string? HeadHitTerrainSound { get; set; } = null;
    public string? HeadHitEntitySound { get; set; } = null;
    public string? ShaftHitTerrainSound { get; set; } = null;
    public string? ShaftHitEntitySound { get; set; } = null;
    #endregion

    #region Durability
    public int DurabilitySpentOnEntityHit { get; set; } = 1;
    public int DurabilitySpentOnTerrainHit { get; set; } = 1;
    #endregion
}
