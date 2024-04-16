using AnimationManagerLib.API;
using MaltiezFSM;
using MaltiezFSM.Framework.Simplified.Systems;
using Spears;
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Javelins;

public sealed class JavelinFsm : JavelinControls
{
    public static readonly TimeSpan AimToHoldDelay = TimeSpan.FromSeconds(0.5);

    public JavelinFsm(ICoreAPI api, CollectibleObject collectible, JavelinStats stats) : base(api, collectible, AimToHoldDelay)
    {
        _api = api;

        if (api is ICoreClientAPI clientApi)
        {
            _animationSystem = new(clientApi, debugName: "javelin-animations");
            _attacksForDebugRendering = GetAttacks(stats, clientApi);
            _attacksSystem = new(api, _animationSystem, _attacksForDebugRendering, debugName: "javelin-attacks");

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
            _attacksSystem = new(api, _animationSystem, GetAttacks(stats, api), debugName: "javelin-attacks");
        }

        _aimingSystem = new(stats.DispersionMin, stats.DispersionMax, TimeSpan.FromMilliseconds(stats.AimDuration), "javelin-aiming", api);
        _projectileSystem = new(api);

        _stats = stats;
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
    private readonly JavelinStats _stats;
    private readonly TimeSpan _easeOutTime = TimeSpan.FromSeconds(0.6);
    private readonly CollisionEffects _headCollisionEffects = new();
    private readonly CollisionEffects _shaftCollisionEffects = new();
    private readonly AimingSystem? _aimingSystem;
    private readonly ProjectileSystem? _projectileSystem;
    private readonly ICoreAPI _api;
    private const int _throwDurationMs = 300;
    private const int _terrainHitCooldownMs = 500;
    private long _attackCancelTimer = -1;

    protected override bool OnStartAttack(ItemSlot slot, IPlayer player, StanceType stanceType)
    {
        Console.WriteLine("OnStartAttack");
        _lastAttack = GetAttackAnimationType(stanceType);
        if (_attackCancelTimer != -1) _api.World.UnregisterCallback(_attackCancelTimer);
        _attacksSystem?.Start(
                GetAttackAnimationType(stanceType),
                player,
                slot,
                attackFinishedCallback: () => OnAttackFinished(slot, player, stanceType),
                terrainCollisionCallback: () => OnTerrainHit(slot, player),
                entityCollisionCallback: () => OnEntityHit(slot, player));

        return true;
    }
    protected override bool OnStartAim(ItemSlot slot, IPlayer player, StanceType stanceType)
    {
        CancelAttack(slot, player);
        _animationSystem?.Play(player, SpearAnimationSystem.AnimationType.Aim);
        _aimingSystem?.Start(slot, player);
        return true;
    }
    protected override bool OnStartThrow(ItemSlot slot, IPlayer player, StanceType stanceType)
    {
        _animationSystem?.Play(player, SpearAnimationSystem.AnimationType.Throw);

        _api.World.RegisterCallback((dt) => Throw(slot, player, stanceType), millisecondDelay: _throwDurationMs);

        return true;
    }

    protected override void OnCancelAttack(ItemSlot slot, IPlayer player, StanceType stanceType)
    {
        Console.WriteLine("OnCancelAttack");
        _attackCancelTimer = -1;
        _attacksSystem?.Stop(GetAttackAnimationType(stanceType), player);
        _animationSystem?.Play(player, GetStanceAnimationType(stanceType));
    }
    protected override void OnCancelAim(ItemSlot slot, IPlayer player, StanceType stanceType)
    {
        _animationSystem?.Play(player, GetStanceAnimationType(stanceType));
        _aimingSystem?.Stop(slot, player);
    }

    protected override void OnStanceChange(ItemSlot slot, IPlayer player, StanceType newStance)
    {
        _animationSystem?.Play(player, GetStanceAnimationType(newStance));
    }
    protected override void OnDeselected(ItemSlot slot, IPlayer player)
    {
        player.Entity.Stats.Remove("walkspeed", "maltiezspears");
        _animationSystem?.EaseOut(player, _easeOutTime, SpearAnimationSystem.AnimationType.Low1hStance);

        HarmonyPatches.FpHandsOffset = HarmonyPatches.DefaultFpHandsOffset;
    }
    protected override void OnSelected(ItemSlot slot, IPlayer player)
    {
        _animationSystem?.Play(player, GetStanceAnimationType(GetStance(slot)));

        HarmonyPatches.FpHandsOffset = 0f;
    }

    private void OnAttackFinished(ItemSlot slot, IPlayer player, StanceType stanceType)
    {
        CancelAttack(slot, player);
    }
    private bool OnEntityHit(ItemSlot slot, IPlayer player)
    {
        if (_stats.StopAttackOnEntityHit)
        {
            _attacksSystem?.Stop(GetAttackAnimationType(GetStance(slot)), player);
            _animationSystem?.Play(player, GetStanceAnimationType(GetStance(slot)));
            Console.WriteLine("Enqueue Cancel attack");
            _attackCancelTimer = _api.World.RegisterCallback((dt) => CancelAttack(slot, player), millisecondDelay: _terrainHitCooldownMs);
        }

        slot.Itemstack.Item.DamageItem(player.Entity.World, player.Entity, slot, _stats.DurabilitySpentOnEntityHit);
        
        return _stats.StopAttackOnEntityHit;
    }
    private bool OnTerrainHit(ItemSlot slot, IPlayer player)
    {
        if (_stats.StopAttackOnTerrainHit)
        {
            _attacksSystem?.Stop(GetAttackAnimationType(GetStance(slot)), player);
            _animationSystem?.Play(player, GetStanceAnimationType(GetStance(slot)));
            _attackCancelTimer = _api.World.RegisterCallback((dt) => CancelAttack(slot, player), millisecondDelay: _terrainHitCooldownMs);
        }
        
        slot.Itemstack.Item.DamageItem(player.Entity.World, player.Entity, slot, _stats.DurabilitySpentOnTerrainHit);

        return _stats.StopAttackOnTerrainHit;
    }

    private void Throw(ItemSlot slot, IPlayer player, StanceType stanceType)
    {
        if (slot.Itemstack?.Item == null) return;

        if (_aimingSystem != null && _projectileSystem != null)
        {
            DirectionOffset offset = _aimingSystem.GetShootingDirectionOffset(slot, player);
            _projectileSystem.Spawn(slot.TakeOutWhole(), player, _stats.ProjectileSpeed, _stats.ThrowDamageMultiplier, offset);
            _aimingSystem.Stop(slot, player);
        }
        else
        {
            _ = slot.TakeOutWhole();
        }
    }

    #region Attacks
    private Dictionary<SpearAnimationSystem.AnimationType, MeleeAttack> GetAttacks(JavelinStats stats, ICoreAPI api)
    {
        Dictionary<SpearAnimationSystem.AnimationType, MeleeAttack> result = new();

        SpearAnimationSystem.AnimationType[] animationTypes = Enum.GetValues<SpearAnimationSystem.AnimationType>();
        foreach ((SpearAnimationSystem.AnimationType type, MeleeAttack? attack) in animationTypes.Select(type => (type, GetAttack(type, stats, api))))
        {
            if (attack != null) result.Add(type, attack);
        }

        return result;
    }
    private MeleeAttack? GetAttack(SpearAnimationSystem.AnimationType attackType, JavelinStats stats, ICoreAPI api)
    {
        List<MeleeAttackDamageType> damageTypes = new();
        MeleeAttackDamageType? headDamage = GetHeadDamageType(attackType, stats);
        MeleeAttackDamageType? shaftDamage = GetShaftDamageType(attackType, stats);
        if (headDamage != null) damageTypes.Add(headDamage);
        if (shaftDamage != null) damageTypes.Add(shaftDamage);

        return new MeleeAttack(api as ICoreClientAPI, GetAttackDuration(attackType, stats), damageTypes, stats.MaxReach);
    }
    private static TimeSpan GetAttackDuration(SpearAnimationSystem.AnimationType attackType, JavelinStats stats)
    {
        switch (attackType)
        {
            case SpearAnimationSystem.AnimationType.Low1hAttack:
            case SpearAnimationSystem.AnimationType.High1hAttack:
                return TimeSpan.FromMilliseconds(stats.AttackDuration1hMs);
            default:
                return TimeSpan.Zero;
        }
    }
    private MeleeAttackDamageType? GetHeadDamageType(SpearAnimationSystem.AnimationType attackType, JavelinStats stats)
    {
        switch (attackType)
        {
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
    private MeleeAttackDamageType? GetShaftDamageType(SpearAnimationSystem.AnimationType attackType, JavelinStats stats)
    {
        switch (attackType)
        {
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
            default: return null;
        }
    }
    #endregion

    #region Animations
    private static SpearAnimationSystem.AnimationType GetAttackAnimationType(StanceType stance)
    {
        return stance switch
        {
            StanceType.OneHandedUpper => SpearAnimationSystem.AnimationType.High1hAttack,
            StanceType.OneHandedLower => SpearAnimationSystem.AnimationType.Low1hAttack,
            _ => throw new NotImplementedException()
        };
    }
    private static SpearAnimationSystem.AnimationType GetStanceAnimationType(StanceType stance)
    {
        return stance switch
        {
            StanceType.OneHandedUpper => SpearAnimationSystem.AnimationType.High1hStance,
            StanceType.OneHandedLower => SpearAnimationSystem.AnimationType.Low1hStance,
            _ => throw new NotImplementedException()
        };
    }


    private static Dictionary<SpearAnimationSystem.AnimationType, List<SpearAnimationSystem.AnimationParameters>> GetAnimations(JavelinStats stats)
    {
        Dictionary<SpearAnimationSystem.AnimationType, List<SpearAnimationSystem.AnimationParameters>> result = new();

        SpearAnimationSystem.AnimationType[] animationTypes = Enum.GetValues<SpearAnimationSystem.AnimationType>();
        foreach ((SpearAnimationSystem.AnimationType type, SpearAnimationSystem.AnimationParameters? parameters) in animationTypes.Select(type => (type, GetAnimation(type, stats))))
        {
            if (parameters != null) result.Add(type, new() { parameters.Value });
        }

        return result;
    }
    private static SpearAnimationSystem.AnimationParameters? GetAnimation(SpearAnimationSystem.AnimationType animationType, JavelinStats stats)
    {
        SpearAnimationSystem.AnimationParameters empty = new(
            "spearsempty",
            RunParameters.EaseOut(TimeSpan.FromMilliseconds(1000))
            );

        return animationType switch
        {
            SpearAnimationSystem.AnimationType.Low1hAttack => new(
                "javelin-low-1h",
                RunParameters.EaseIn(TimeSpan.FromMilliseconds(stats.AttackWindow1h[0]), 0, ProgressModifierType.CosShifted),
                RunParameters.EaseIn(TimeSpan.FromMilliseconds(stats.AttackWindow1h[1] - stats.AttackWindow1h[0]), 1, ProgressModifierType.Cubic),
                RunParameters.EaseIn(TimeSpan.FromMilliseconds(stats.AttackEaseOutAnimationTypeMs), 0, ProgressModifierType.Bounce)
                ),
            SpearAnimationSystem.AnimationType.High1hAttack => new(
                "javelin-high-1h",
                RunParameters.EaseIn(TimeSpan.FromMilliseconds(stats.AttackWindow1h[0]), 0, ProgressModifierType.Sin),
                RunParameters.EaseIn(TimeSpan.FromMilliseconds(stats.AttackWindow1h[1] - stats.AttackWindow1h[0]), 1, ProgressModifierType.Cubic),
                RunParameters.EaseIn(TimeSpan.FromMilliseconds(stats.AttackEaseOutAnimationTypeMs), 0, ProgressModifierType.Bounce)
                ),
            SpearAnimationSystem.AnimationType.Aim => new(
                "spear-throw",
                RunParameters.EaseIn(TimeSpan.FromMilliseconds(700), 0, ProgressModifierType.CosShifted)
                ),
            SpearAnimationSystem.AnimationType.Throw => new(
                "spear-throw",
                RunParameters.EaseIn(TimeSpan.FromMilliseconds(_throwDurationMs), 1, ProgressModifierType.Cubic)
                ),
            SpearAnimationSystem.AnimationType.Low1hStance => new(
                "javelin-1h-stances",
                RunParameters.EaseIn(TimeSpan.FromMilliseconds(500), 0, ProgressModifierType.Bounce)
                ),
            SpearAnimationSystem.AnimationType.High1hStance => new(
                "javelin-1h-stances",
                RunParameters.EaseIn(TimeSpan.FromMilliseconds(500), 1, ProgressModifierType.Bounce)
                ),
            _ => null
        };
    }
    #endregion
}

public sealed class JavelinStats
{
    public float MaxReach { get; set; }
    public bool StopAttackOnTerrainHit { get; set; } = true;
    public bool StopAttackOnEntityHit { get; set; } = false;

    #region Damage
    public float DamageShaft { get; set; }
    public float Damage1hAttack { get; set; }
    public int ShaftDamageTier { get; set; }
    public int HeadDamageTier { get; set; }
    #endregion

    #region Knockback
    public float KnockbackAttack1h { get; set; }
    public float StaggerAttack1h { get; set; }
    #endregion

    #region Colliders
    public float[] HeadCollider { get; set; } = new float[6];
    public float[] ShaftCollider { get; set; } = new float[6];
    #endregion

    #region Timing
    public float AttackDuration1hMs { get; set; }
    public float[] AttackWindow1h { get; set; } = new float[2];
    public float[] ShaftCollisionWindow1h { get; set; } = new float[2];
    public float AttackEaseOutAnimationTypeMs { get; set; }
    #endregion

    #region Grip
    public float GripMaxLength1h { get; set; } = 1;
    public float GripMinLength1h { get; set; } = 0;
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

    #region Throw
    public float ProjectileSpeed { get; set; } = 1;
    public float DispersionMin { get; set; } = 0;
    public float DispersionMax { get; set; } = 0;
    public float AimDuration { get; set; } = 0;
    public float ThrowDamageMultiplier { get; set; } = 1;
    #endregion
}
