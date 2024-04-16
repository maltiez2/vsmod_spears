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

public sealed class SpearFsm : SpearControls
{
    public static readonly TimeSpan AimToHoldDelay = TimeSpan.FromSeconds(0.5);

    public SpearFsm(ICoreAPI api, CollectibleObject collectible, SpearStats stats) : base(api, collectible, AimToHoldDelay)
    {
        _api = api;

        if (api is ICoreClientAPI clientApi)
        {
            _animationSystem = new(clientApi, debugName: "spear-animations");
            _attacksForDebugRendering = GetAttacks(stats, clientApi);
            _attacksSystem = new(api, _animationSystem, _attacksForDebugRendering, debugName: "spear-attacks");

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
            _attacksSystem = new(api, _animationSystem, GetAttacks(stats, api), debugName: "spear-attacks");  
        }

        _blockParameters = BlockParameters.FrontBlock(api, stats.BlockDamageReduction, new(stats.BlockSound), new(stats.PerfectBlockSound), null, TimeSpan.FromMilliseconds(stats.PerfectBlockWindowMs));

        _aimingSystem = new(stats.DispersionMin, stats.DispersionMax, TimeSpan.FromMilliseconds(stats.AimDuration), "spear-aiming", api);
        _projectileSystem = new(api);

        _stats = stats;
    }

    public bool ChangeGrip(ItemSlot slot, IPlayer player, float delta)
    {
        StanceType stance = GetStance(slot);
        if (stance == StanceType.OneHandedUpper)
        {
            _animationSystem?.ResetGrip(player);
            return false;
        }
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

        /*if (_attacksForDebugRendering != null && _lastAttack != null)
        {
            _attacksForDebugRendering[_lastAttack.Value].RenderDebugColliders(player, slot);
        }*/
    }
    #endregion

    private readonly SpearAnimationSystem? _animationSystem;
    private readonly SpearAttacksSystem? _attacksSystem;
    private readonly SpearStats _stats;
    private readonly TimeSpan _easeOutTime = TimeSpan.FromSeconds(0.6);
    private readonly CollisionEffects _headCollisionEffects = new();
    private readonly CollisionEffects _shaftCollisionEffects = new();
    private readonly AimingSystem? _aimingSystem;
    private readonly ProjectileSystem? _projectileSystem;
    private readonly ICoreAPI _api;
    private readonly BlockParameters _blockParameters;
    private const float _gripFactor = 0.1f;
    private const int _throwDurationMs = 300;
    private const int _terrainHitCooldownMs = 500;
    private float _grip = 0;

    protected override bool OnStartAttack(ItemSlot slot, IPlayer player, StanceType stanceType)
    {
        _lastAttack = GetAttackAnimationType(stanceType);
        if (stanceType == StanceType.BlockUpper || stanceType == StanceType.BlockLower)
        {
            _attacksSystem?.Start(
                GetAttackAnimationType(stanceType),
                player,
                slot,
                attackFinishedCallback: () => OnBlockAttackFinished(slot, player, stanceType),
                terrainCollisionCallback: () => OnTerrainHit(slot, player),
                entityCollisionCallback: () => OnEntityHit(slot, player));
        }
        else
        {
            _attacksSystem?.Start(
                GetAttackAnimationType(stanceType),
                player,
                slot,
                attackFinishedCallback: () => OnAttackFinished(slot, player, stanceType),
                terrainCollisionCallback: () => OnTerrainHit(slot, player),
                entityCollisionCallback: () => OnEntityHit(slot, player));
        }

        return true;
    }
    protected override bool OnStartBlock(ItemSlot slot, IPlayer player, StanceType stanceType, bool attacking)
    {
        if (attacking) CancelAttack(slot, player);
        _animationSystem?.Play(player, GetBlockAnimationType(stanceType));
        BeginBlock(slot, player);
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
        _attacksSystem?.Stop(GetAttackAnimationType(stanceType), player);
        _animationSystem?.Play(player, GetStanceAnimationType(stanceType));
    }
    protected override void OnCancelBlock(ItemSlot slot, IPlayer player, StanceType stanceType, bool attacking)
    {
        if (stanceType == StanceType.OneHandedUpper)
        {
            _animationSystem?.ResetGrip(player, TimeSpan.FromSeconds(0.5));
            _grip = 0;
        }
        _animationSystem?.Play(player, GetStanceAnimationType(stanceType));
        EndBlock(slot, player);
    }
    protected override void OnCancelAim(ItemSlot slot, IPlayer player, StanceType stanceType)
    {
        _animationSystem?.Play(player, GetStanceAnimationType(stanceType));
        _aimingSystem?.Stop(slot, player);
    }

    protected override void OnStanceChange(ItemSlot slot, IPlayer player, StanceType newStance)
    {
        if (newStance == StanceType.OneHandedUpper)
        {
            _animationSystem?.ResetGrip(player, TimeSpan.FromSeconds(0.5));
            _grip = 0;
        }
        _animationSystem?.Play(player, GetStanceAnimationType(newStance));
    }
    protected override void OnDeselected(ItemSlot slot, IPlayer player)
    {
        player.Entity.Stats.Remove("walkspeed", "maltiezspears");
        _animationSystem?.EaseOut(player, _easeOutTime);
        _animationSystem?.ResetGrip(player);
        EndBlock(slot, player);
        _grip = 0;

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
    private void OnBlockAttackFinished(ItemSlot slot, IPlayer player, StanceType stanceType)
    {
        CancelAttack(slot, player);
        StartBlock(slot, player);
    }
    private bool OnEntityHit(ItemSlot slot, IPlayer player)
    {
        slot.Itemstack.Item.DamageItem(player.Entity.World, player.Entity, slot, _stats.DurabilitySpentOnEntityHit);
        return false;
    }
    private bool OnTerrainHit(ItemSlot slot, IPlayer player)
    {
        _attacksSystem?.Stop(GetAttackAnimationType(GetStance(slot)), player);
        _animationSystem?.Play(player, GetStanceAnimationType(GetStance(slot)));
        slot.Itemstack.Item.DamageItem(player.Entity.World, player.Entity, slot, _stats.DurabilitySpentOnTerrainHit);

        _api.World.RegisterCallback((dt) => CancelAttack(slot, player), millisecondDelay: _terrainHitCooldownMs);

        return true;
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
    private void BeginBlock(ItemSlot slot, IPlayer player)
    {
        if (player.Entity.GetBehavior<BlockAgainstDamage>() is not BlockAgainstDamage behavior) return;

        behavior.Start(_blockParameters);
    }
    private void EndBlock(ItemSlot slot, IPlayer player)
    {
        if (player.Entity.GetBehavior<BlockAgainstDamage>() is not BlockAgainstDamage behavior) return;

        behavior.Stop();
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

    #region Animations
    private static SpearAnimationSystem.AnimationType GetAttackAnimationType(StanceType stance)
    {
        return stance switch
        {
            StanceType.OneHandedUpper => SpearAnimationSystem.AnimationType.High1hAttack,
            StanceType.OneHandedLower => SpearAnimationSystem.AnimationType.Low1hAttack,
            StanceType.TwoHandedUpper => SpearAnimationSystem.AnimationType.High2hAttack,
            StanceType.TwoHandedLower => SpearAnimationSystem.AnimationType.Low2hAttack,
            StanceType.BlockLower => SpearAnimationSystem.AnimationType.Low2hBlockAttack,
            StanceType.BlockUpper => SpearAnimationSystem.AnimationType.High2hBlockAttack,
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
            StanceType.BlockLower => SpearAnimationSystem.AnimationType.Low2hBlock,
            StanceType.BlockUpper => SpearAnimationSystem.AnimationType.High2hBlock,
            _ => throw new NotImplementedException()
        };
    }
    private static SpearAnimationSystem.AnimationType GetBlockAnimationType(StanceType stance)
    {
        return stance switch
        {
            StanceType.TwoHandedUpper => SpearAnimationSystem.AnimationType.High2hBlock,
            StanceType.TwoHandedLower => SpearAnimationSystem.AnimationType.Low2hBlock,
            StanceType.BlockLower => SpearAnimationSystem.AnimationType.Low2hBlock,
            StanceType.BlockUpper => SpearAnimationSystem.AnimationType.High2hBlock,
            _ => SpearAnimationSystem.AnimationType.High2hBlock
        };
    }


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
                "spear-high-2h",
                RunParameters.EaseIn(TimeSpan.FromMilliseconds(0.4 * stats.AttackDuration2hMs), 0, ProgressModifierType.Sqrt),
                RunParameters.EaseIn(TimeSpan.FromMilliseconds(0.3 * stats.AttackDuration2hMs), 1, ProgressModifierType.Cubic),
                RunParameters.EaseIn(TimeSpan.FromMilliseconds(0.6 * stats.AttackDuration2hMs), 0, ProgressModifierType.Bounce)
                ),
            SpearAnimationSystem.AnimationType.Low2hAttack => new(
                "spear-low-2h",
                RunParameters.EaseIn(TimeSpan.FromMilliseconds(0.4 * stats.AttackDuration2hMs), 0, ProgressModifierType.SqrtSqrt),
                RunParameters.EaseIn(TimeSpan.FromMilliseconds(0.3 * stats.AttackDuration2hMs), 1, ProgressModifierType.Cubic),
                RunParameters.EaseIn(TimeSpan.FromMilliseconds(0.6 * stats.AttackDuration2hMs), 0, ProgressModifierType.Bounce)
                ),
            SpearAnimationSystem.AnimationType.Low1hAttack => new(
                "spear-low-1h",
                RunParameters.EaseIn(TimeSpan.FromMilliseconds(0.4 * stats.AttackDuration1hMs), 0, ProgressModifierType.Bounce),
                RunParameters.EaseIn(TimeSpan.FromMilliseconds(0.3 * stats.AttackDuration1hMs), 1, ProgressModifierType.Cubic),
                RunParameters.EaseIn(TimeSpan.FromMilliseconds(0.6 * stats.AttackDuration1hMs), 0, ProgressModifierType.Bounce)
                ),
            SpearAnimationSystem.AnimationType.High1hAttack => new(
                "spear-high-1h",
                RunParameters.EaseIn(TimeSpan.FromMilliseconds(0.4 * stats.AttackDuration1hMs), 0, ProgressModifierType.Bounce),
                RunParameters.EaseIn(TimeSpan.FromMilliseconds(0.3 * stats.AttackDuration1hMs), 1, ProgressModifierType.Cubic),
                RunParameters.EaseIn(TimeSpan.FromMilliseconds(0.6 * stats.AttackDuration1hMs), 0, ProgressModifierType.Bounce)
                ),
            SpearAnimationSystem.AnimationType.Low2hBlockAttack => new(
                "spear-low-block",
                RunParameters.EaseIn(TimeSpan.FromMilliseconds(0.4 * stats.AttackDurationBlockMs), 0, ProgressModifierType.Bounce),
                RunParameters.EaseIn(TimeSpan.FromMilliseconds(0.3 * stats.AttackDurationBlockMs), 1, ProgressModifierType.Cubic),
                RunParameters.EaseIn(TimeSpan.FromMilliseconds(0.6 * stats.AttackDurationBlockMs), 0, ProgressModifierType.Bounce)
                ),
            SpearAnimationSystem.AnimationType.High2hBlockAttack => new(
                "spear-high-block",
                RunParameters.EaseIn(TimeSpan.FromMilliseconds(0.4 * stats.AttackDurationBlockMs), 0, ProgressModifierType.Bounce),
                RunParameters.EaseIn(TimeSpan.FromMilliseconds(0.3 * stats.AttackDurationBlockMs), 1, ProgressModifierType.Cubic),
                RunParameters.EaseIn(TimeSpan.FromMilliseconds(0.6 * stats.AttackDurationBlockMs), 0, ProgressModifierType.Bounce)
                ),
            SpearAnimationSystem.AnimationType.Aim => new(
                "spear-throw",
                RunParameters.EaseIn(TimeSpan.FromMilliseconds(700), 0, ProgressModifierType.CosShifted)
                ),
            SpearAnimationSystem.AnimationType.Throw => new(
                "spear-throw",
                RunParameters.EaseIn(TimeSpan.FromMilliseconds(_throwDurationMs), 1, ProgressModifierType.Cubic)
                ),

            SpearAnimationSystem.AnimationType.Low2hBlock => new(
                "spear-2h-stances",
                RunParameters.EaseIn(TimeSpan.FromMilliseconds(500), 2, ProgressModifierType.Bounce)
                ),
            SpearAnimationSystem.AnimationType.High2hBlock => new(
                "spear-2h-stances",
                RunParameters.EaseIn(TimeSpan.FromMilliseconds(500), 3, ProgressModifierType.Bounce)
                ),

            SpearAnimationSystem.AnimationType.Low2hStance => new(
                "spear-2h-stances",
                RunParameters.EaseIn(TimeSpan.FromMilliseconds(500), 0, ProgressModifierType.Bounce)
                ),
            SpearAnimationSystem.AnimationType.High2hStance => new(
                "spear-2h-stances",
                RunParameters.EaseIn(TimeSpan.FromMilliseconds(500), 1, ProgressModifierType.Bounce)
                ),
            SpearAnimationSystem.AnimationType.Low1hStance => new(
                "spear-1h-stances",
                RunParameters.EaseIn(TimeSpan.FromMilliseconds(500), 0, ProgressModifierType.Bounce)
                ),
            SpearAnimationSystem.AnimationType.High1hStance => new(
                "spear-1h-stances",
                RunParameters.EaseIn(TimeSpan.FromMilliseconds(500), 1, ProgressModifierType.Bounce)
                ),
            _ => null
        };
    }
    #endregion
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

    #region Throw
    public float ProjectileSpeed { get; set; } = 1;
    public float DispersionMin { get; set; } = 0;
    public float DispersionMax { get; set; } = 0;
    public float AimDuration { get; set; } = 0;
    public float ThrowDamageMultiplier { get; set; } = 1;
    #endregion

    #region Block
    public float BlockDamageReduction { get; set; } = 0;
    public string? BlockSound { get; set; } = null;
    public string? PerfectBlockSound { get; set; } = null;
    public float PerfectBlockWindowMs { get; set; } = 0;
    #endregion
}
