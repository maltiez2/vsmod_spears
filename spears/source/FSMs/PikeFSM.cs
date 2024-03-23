using AnimationManagerLib.API;
using MaltiezFSM;
using MaltiezFSM.Framework.Simplified.Systems;
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Spears;

public class PikeFsm : PikeControls
{
    public PikeFsm(ICoreAPI api, CollectibleObject collectible, PikeStats stats) : base(api, collectible)
    {
        Console.WriteLine("Init PikeFsm");

        if (api is ICoreClientAPI clientApi)
        {
            AnimationSystem = new(clientApi, debugName: "pike-animations");
            _attacksForDebugRendering = GetAttacks(stats, clientApi);
            AttacksSystem = new(api, AnimationSystem, _attacksForDebugRendering, debugName: "pike-attacks");

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
    protected readonly PikeStats Stats;

    protected override bool OnStartAttack(ItemSlot slot, IPlayer player, StanceType stanceType)
    {
        Console.WriteLine($"OnStartAttack: {stanceType}");
        _lastAttack = GetAttackAnimationType(stanceType);
        AttacksSystem?.Start(GetAttackAnimationType(stanceType), player, slot, () => OnAttackFinished(slot, player, stanceType), terrainCollisionCallback: () => OnTerrainHit(slot, player));
        return true;
    }
    protected virtual void OnAttackFinished(ItemSlot slot, IPlayer player, StanceType stanceType)
    {
        Console.WriteLine($"OnAttackFinished: {stanceType}");
        CancelAttack(slot, player);
    }
    protected override void OnCancelAttack(ItemSlot slot, IPlayer player, StanceType stanceType)
    {
        AnimationSystem?.Play(player, GetStanceAnimationType(stanceType));
        Console.WriteLine($"OnCancelAttack: {stanceType}");
    }
    protected override void OnStanceChange(ItemSlot slot, IPlayer player, StanceType newStance)
    {
        Console.WriteLine($"OnStanceChange: {newStance}");
        AnimationSystem?.Play(player, GetStanceAnimationType(newStance));
    }
    protected override void OnDeselected(ItemSlot slot, IPlayer player)
    {
        Console.WriteLine($"OnDeselected");
        AnimationSystem?.EaseOut(player, _easeOutTime);
    }
    protected override void OnSelected(ItemSlot slot, IPlayer player)
    {
        Console.WriteLine($"OnSelected");
        AnimationSystem?.Play(player, GetStanceAnimationType(StanceType.Shoulder));
    }

    protected virtual bool OnTerrainHit(ItemSlot slot, IPlayer player)
    {
        CancelAttack(slot, player);
        return true;
    }

    private readonly TimeSpan _easeOutTime = TimeSpan.FromSeconds(0.6);
    private readonly Dictionary<string, AdvancedParticleProperties> _terrainHitEffects = new();
    private readonly Dictionary<string, AdvancedParticleProperties> _entityHitEffects = new();

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
        HitWindow hitWindow;

        switch (attackType)
        {
            case SpearAnimationSystem.AnimationType.High2hAttack:
            case SpearAnimationSystem.AnimationType.Low2hAttack:
                hitWindow = new(TimeSpan.FromMilliseconds(stats.AttackWindowMs[0]), TimeSpan.FromMilliseconds(stats.AttackWindowMs[1]));
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
                        hitSound: stats.HeadHitEntitySound != null ? new(stats.HeadHitEntitySound) : null,
                        terrainSound: stats.HeadHitTerrainSound != null ? new(stats.HeadHitTerrainSound) : null,
                        knockback: stats.Knockback,
                        stagger: stats.Stagger
                    )
                {
                    TerrainCollisionParticles = _terrainHitEffects,
                    EntityCollisionParticles = _entityHitEffects
                };
            default: return null;
        }
    }
    private static MeleeAttackDamageType? GetShaftDamageType(SpearAnimationSystem.AnimationType attackType, PikeStats stats)
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
    public float[] HeadCollider { get; set; } = new float[6];
    public float[] ShaftCollider { get; set; } = new float[6];
    public string? HeadHitTerrainSound { get; set; } = null;
    public string? HeadHitEntitySound { get; set; } = null;
    public string? ShaftHitTerrainSound { get; set; } = null;
    public string? ShaftHitEntitySound { get; set; } = null;
    public float[] AttackWindowMs { get; set; } = new float[2];
}
