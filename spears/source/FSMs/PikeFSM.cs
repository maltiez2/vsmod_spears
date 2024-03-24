using AnimationManagerLib.API;
using MaltiezFSM;
using MaltiezFSM.Framework.Simplified.Systems;
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Spears;

public class PikeFsm : PikeControls
{
    public PikeFsm(ICoreAPI api, CollectibleObject collectible, PikeStats stats) : base(api, collectible)
    {
        if (api is ICoreClientAPI clientApi)
        {
            AnimationSystem = new(clientApi, debugName: "pike-animations");
            _attacksForDebugRendering = GetAttacks(stats, clientApi);
            AttacksSystem = new(api, AnimationSystem, _attacksForDebugRendering, debugName: "pike-attacks");

            AnimationSystem.RegisterAnimations(GetAnimations());

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
            AttacksSystem = new(api, AnimationSystem, GetAttacks(stats, api), debugName: "spears-attacks");
        }

        Stats = stats;
    }

    public bool ChangeGrip(ItemSlot slot, IPlayer player, float delta)
    {
        if (GetStance(slot) == StanceType.Shoulder)
        {
            AnimationSystem?.ResetGrip(player);
            Grip = 0;
            return false;
        }
        Grip = GameMath.Clamp(Grip + delta * GripFactor, 0, 1);
        AnimationSystem?.SetGrip(player, Grip);
        return true;
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

    protected const float GripFactor = 0.1f;
    protected float Grip = 0;
    protected readonly SpearAnimationSystem? AnimationSystem;
    protected readonly SpearAttacksSystem? AttacksSystem;
    protected readonly PikeStats Stats;

    protected override bool OnStartAttack(ItemSlot slot, IPlayer player, StanceType stanceType)
    {
        _lastAttack = GetAttackAnimationType(stanceType);
        AttacksSystem?.Start(
            GetAttackAnimationType(stanceType),
            player,
            slot,
            attackFinishedCallback: () => OnAttackFinished(slot, player, stanceType),
            terrainCollisionCallback: () => OnTerrainHit(slot, player),
            entityCollisionCallback: () => OnEntityHit(slot, player));
        return true;
    }
    protected virtual void OnAttackFinished(ItemSlot slot, IPlayer player, StanceType stanceType)
    {
        CancelAttack(slot, player);
    }
    protected override void OnCancelAttack(ItemSlot slot, IPlayer player, StanceType stanceType)
    {
        AnimationSystem?.Play(player, GetStanceAnimationType(stanceType));
    }
    protected override void OnStanceChange(ItemSlot slot, IPlayer player, StanceType newStance)
    {
        if (newStance == StanceType.Shoulder)
        {
            AnimationSystem?.ResetGrip(player, TimeSpan.FromSeconds(0.5));
            Grip = 0;
        }
        AnimationSystem?.Play(player, GetStanceAnimationType(newStance));
    }
    protected override void OnDeselected(ItemSlot slot, IPlayer player)
    {
        AnimationSystem?.EaseOut(player, _easeOutTime);
        AnimationSystem?.ResetGrip(player);
        Grip = 0;
    }
    protected override void OnSelected(ItemSlot slot, IPlayer player)
    {
        AnimationSystem?.Play(player, GetStanceAnimationType(StanceType.Shoulder));
    }

    protected virtual bool OnEntityHit(ItemSlot slot, IPlayer player)
    {
        slot.Itemstack.Item.DamageItem(player.Entity.World, player.Entity, slot, 1);
        return false;
    }
    protected virtual bool OnTerrainHit(ItemSlot slot, IPlayer player)
    {
        CancelAttack(slot, player);
        slot.Itemstack.Item.DamageItem(player.Entity.World, player.Entity, slot, 1);
        return true;
    }

    private readonly TimeSpan _easeOutTime = TimeSpan.FromSeconds(0.6);
    private readonly CollisionEffects _headCollisionEffects = new();
    private readonly CollisionEffects _shaftCollisionEffects = new();

    private static SpearAnimationSystem.AnimationType GetAttackAnimationType(StanceType stance)
    {
        return stance switch
        {
            StanceType.Upper => SpearAnimationSystem.AnimationType.High2hAttack,
            StanceType.Lower => SpearAnimationSystem.AnimationType.Low2hAttack,
            _ => throw new NotImplementedException()
        };
    }
    private static SpearAnimationSystem.AnimationType GetStanceAnimationType(StanceType stance)
    {
        return stance switch
        {
            StanceType.Upper => SpearAnimationSystem.AnimationType.High2hStance,
            StanceType.Lower => SpearAnimationSystem.AnimationType.Low2hStance,
            StanceType.Shoulder => SpearAnimationSystem.AnimationType.Idle1h,
            _ => throw new NotImplementedException()
        };
    }

    private Dictionary<SpearAnimationSystem.AnimationType, MeleeAttack> GetAttacks(PikeStats stats, ICoreAPI api)
    {
        Dictionary<SpearAnimationSystem.AnimationType, MeleeAttack> result = new();

        SpearAnimationSystem.AnimationType[] animationTypes = Enum.GetValues<SpearAnimationSystem.AnimationType>();
        foreach ((SpearAnimationSystem.AnimationType type, MeleeAttack? attack) in animationTypes.Select(type => (type, GetAttack(type, stats, api))))
        {
            if (attack != null) result.Add(type, attack);
        }

        return result;
    }
    private MeleeAttack? GetAttack(SpearAnimationSystem.AnimationType attackType, PikeStats stats, ICoreAPI api)
    {
        List<MeleeAttackDamageType> damageTypes = new();
        MeleeAttackDamageType? headDamage = GetAttackDamageType(attackType, stats);
        MeleeAttackDamageType? shaftDamage = GetShaftDamageType(attackType, stats);
        if (headDamage != null) damageTypes.Add(headDamage);
        if (shaftDamage != null) damageTypes.Add(shaftDamage);

        return new MeleeAttack(api as ICoreClientAPI, TimeSpan.FromMilliseconds(stats.AttackDurationMs), damageTypes, stats.MaxReach);
    }
    private MeleeAttackDamageType? GetAttackDamageType(SpearAnimationSystem.AnimationType attackType, PikeStats stats)
    {
        switch (attackType)
        {
            case SpearAnimationSystem.AnimationType.High2hAttack:
            case SpearAnimationSystem.AnimationType.Low2hAttack:
                return new MeleeAttackDamageType(
                        damage: stats.Damage,
                        damageType: EnumDamageType.PiercingAttack,
                        collider: new(stats.HeadCollider),
                        tier: stats.Tier,
                        hitWindow: new(stats.AttackWindowMs[0] / stats.AttackDurationMs, stats.AttackWindowMs[1] / stats.AttackDurationMs),
                        knockback: stats.Knockback,
                        stagger: stats.Stagger,
                        effects: _headCollisionEffects
                    );
            default: return null;
        }
    }
    private MeleeAttackDamageType? GetShaftDamageType(SpearAnimationSystem.AnimationType attackType, PikeStats stats)
    {
        switch (attackType)
        {
            case SpearAnimationSystem.AnimationType.High2hAttack:
            case SpearAnimationSystem.AnimationType.Low2hAttack:
                return new MeleeAttackDamageType(
                        damage: stats.ShaftDamage,
                        damageType: EnumDamageType.BluntAttack,
                        collider: new(stats.ShaftCollider),
                        tier: 0,
                        hitWindow: new(stats.ShaftCollisionWindowMs[0] / stats.AttackDurationMs, stats.ShaftCollisionWindowMs[1] / stats.AttackDurationMs),
                        knockback: stats.ShaftHitKnockback,
                        effects: _shaftCollisionEffects
                    );
            default: return null;
        }
    }

    private static Dictionary<SpearAnimationSystem.AnimationType, List<SpearAnimationSystem.AnimationParameters>> GetAnimations()
    {
        Dictionary<SpearAnimationSystem.AnimationType, List<SpearAnimationSystem.AnimationParameters>> result = new();

        SpearAnimationSystem.AnimationType[] animationTypes = Enum.GetValues<SpearAnimationSystem.AnimationType>();
        foreach ((SpearAnimationSystem.AnimationType type, SpearAnimationSystem.AnimationParameters? parameters) in animationTypes.Select(type => (type, GetAnimation(type))))
        {
            if (parameters != null) result.Add(type, new() { parameters.Value });
        }

        return result;
    }
    private static SpearAnimationSystem.AnimationParameters? GetAnimation(SpearAnimationSystem.AnimationType animationType)
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
            _ => null
        };
    }
}

public sealed class PikeStats
{
    public float MaxReach { get; set; }
    public float Damage { get; set; }
    public float ShaftDamage { get; set; }
    public float ShaftHitKnockback { get; set; }
    public float Knockback { get; set; }
    public float Stagger { get; set; } = 1.0f;
    public int Tier { get; set; }
    public float AttackDurationMs { get; set; }
    public float[] HeadCollider { get; set; } = new float[6];
    public float[] ShaftCollider { get; set; } = new float[6];
    public string? HeadHitTerrainSound { get; set; } = null;
    public string? HeadHitEntitySound { get; set; } = null;
    public string? ShaftHitTerrainSound { get; set; } = null;
    public string? ShaftHitEntitySound { get; set; } = null;
    public float[] AttackWindowMs { get; set; } = new float[2];
    public float[] ShaftCollisionWindowMs { get; set; } = new float[2];
}
