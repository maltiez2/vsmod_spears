using AnimationManagerLib;
using AnimationManagerLib.API;
using MaltiezFSM.Framework.Simplified.Systems;
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Spears;

public sealed class SpearAnimationSystem : BaseSystem
{
    public enum AnimationType
    {
        Low1hAttack,
        High1hAttack,
        Low2hAttack,
        High2hAttack,
        Low1hBlockAttack,
        High1hBlockAttack,
        Low2hBlockAttack,
        High2hBlockAttack,
        Low1hBlock,
        High1hBlock,
        Low2hBlock,
        High2hBlock,
        Low1hStance,
        High1hStance,
        Low2hStance,
        High2hStance,
        Idle1h,
        Idle2h,
        Aim,
        Throw
    }
    public readonly struct AnimationParameters
    {
        public readonly AnimationId HandsTp;
        public readonly AnimationId LegsTp;
        public readonly AnimationId HandsFp;
        public readonly AnimationId LegsFp;
        public readonly string HandsTpCode;
        public readonly string LegsTpCode;
        public readonly string HandsFpCode;
        public readonly string LegsFpCode;
        public readonly IEnumerable<RunParameters> Parameters;

        public AnimationParameters(string code, params RunParameters[] parameters)
        {
            long id = _id++;
            HandsTpCode = $"{code}-hands";
            LegsTpCode = $"{code}-legs";
            HandsFpCode = $"{code}-hands-fp";
            LegsFpCode = $"{code}-legs-fp";
            HandsTp = new("hands", $"{HandsTpCode}-{id}", EnumAnimationBlendMode.Average, weight: 512);
            LegsTp = new("legs", $"{LegsTpCode}-{id}", EnumAnimationBlendMode.AddAverage, weight: 0);
            HandsFp = new("hands", $"{HandsFpCode}-{id}", EnumAnimationBlendMode.Average, weight: 512);
            LegsFp = new("legs", $"{LegsFpCode}-{id}", EnumAnimationBlendMode.AddAverage, weight: 0);
            Parameters = parameters;
        }

        private static long _id = 0;
    }

    public SpearAnimationSystem(ICoreClientAPI api, string gripAnimation = "pike-grip", string debugName = "") : base(api, debugName)
    {
        _clientApi = api;
        _animationSystem = api.ModLoader.GetModSystem<AnimationManagerLibSystem>();

        _verticalTrackingAnimation = new("tracking", "spears-vertical-tracking", EnumAnimationBlendMode.AddAverage, weight: 1);
        _animationSystem.Register(_verticalTrackingAnimation, AnimationData.Player("spears-vertical-tracking"));

        _gripAnimation = new("grip", gripAnimation, EnumAnimationBlendMode.AddAverage, weight: 1);
        _animationSystem.Register(_gripAnimation, AnimationData.Player(gripAnimation));
    }

    public bool TpTracking { get; set; } = false;

    public void SetGrip(IPlayer player, float value)
    {
        float frame = value * 100f;

        AnimationSequence sequence = new(
            _gripAnimation,
            RunParameters.Set(frame)
        );

        _animationSystem.Run(new AnimationTarget(player.Entity.EntityId, AnimationTargetType.EntityThirdPerson), sequence);
        _animationSystem.Run(new AnimationTarget(player.Entity.EntityId, AnimationTargetType.EntityFirstPerson), sequence, synchronize: false);
    }
    public void ResetGrip(IPlayer player, TimeSpan? duration = null)
    {
        AnimationSequence sequence = new(
            _gripAnimation,
            RunParameters.EaseOut(duration ?? TimeSpan.FromSeconds(1))
            );

        _animationSystem.Run(new AnimationTarget(player.Entity.EntityId, AnimationTargetType.EntityThirdPerson), sequence);
        _animationSystem.Run(new AnimationTarget(player.Entity.EntityId, AnimationTargetType.EntityFirstPerson), sequence, synchronize: false);
    }
    public void Track(IClientPlayer byPlayer, float trackingFactor = 0.8f)
    {
        float angle = byPlayer.CameraPitch * GameMath.RAD2DEG - 180;

        AnimationSequence sequence = new(
            _verticalTrackingAnimation,
            RunParameters.Set(90 + angle * trackingFactor),
            RunParameters.EaseOut(TimeSpan.FromSeconds(1))
            );

        if (TpTracking) _animationSystem.Run(new AnimationTarget(byPlayer.Entity.EntityId, AnimationTargetType.EntityThirdPerson), sequence);

        if (byPlayer == _clientApi.World.Player && _clientApi.Settings.Bool["immersiveFpMode"])
        {
            _animationSystem.Run(new AnimationTarget(byPlayer.Entity.EntityId, AnimationTargetType.EntityFirstPerson), sequence, synchronize: false);
        }
    }
    public void Play(IPlayer player, AnimationType animation)
    {
        AnimationParameters parameters = _animations[animation][GetRandomIndex(_animations[animation].Count)];

        bool immersiveFp = _clientApi.Settings.Bool["immersiveFpMode"];

        AnimationTarget tpTarget = new(player.Entity.EntityId, AnimationTargetType.EntityThirdPerson);
        AnimationTarget fpTarget = new(player.Entity.EntityId, AnimationTargetType.EntityFirstPerson);

        _animationSystem.Run(tpTarget, new AnimationSequence(parameters.HandsTp, parameters.Parameters.ToArray()));
        _animationSystem.Run(tpTarget, new AnimationSequence(parameters.LegsTp, parameters.Parameters.ToArray()));

        if (immersiveFp)
        {
            _animationSystem.Run(fpTarget, new AnimationSequence(parameters.HandsTp, parameters.Parameters.ToArray()), synchronize: false);
            _animationSystem.Run(fpTarget, new AnimationSequence(parameters.LegsTp, parameters.Parameters.ToArray()), synchronize: false);
        }
        else
        {
            _animationSystem.Run(fpTarget, new AnimationSequence(parameters.HandsFp, parameters.Parameters.ToArray()), synchronize: false);
            _animationSystem.Run(fpTarget, new AnimationSequence(parameters.LegsFp, parameters.Parameters.ToArray()), synchronize: false);
        }
    }
    public void EaseOut(IPlayer player, AnimationType animation, TimeSpan duration)
    {
        AnimationParameters parameters = _animations[animation][GetRandomIndex(_animations[animation].Count)];
        RunParameters runParameters = RunParameters.EaseOut(duration);

        AnimationTarget tpTarget = new(player.Entity.EntityId, AnimationTargetType.EntityThirdPerson);
        AnimationTarget fpTarget = new(player.Entity.EntityId, AnimationTargetType.EntityFirstPerson);

        _animationSystem.Run(tpTarget, new AnimationSequence(parameters.HandsTp, runParameters));
        _animationSystem.Run(tpTarget, new AnimationSequence(parameters.LegsTp, runParameters));
        _animationSystem.Run(fpTarget, new AnimationSequence(parameters.HandsFp, runParameters), synchronize: false);
        _animationSystem.Run(fpTarget, new AnimationSequence(parameters.LegsFp, runParameters), synchronize: false);
    }
    public void EaseOut(IPlayer player, TimeSpan duration, AnimationType validAnimation = AnimationType.Low2hStance)
    {
        EaseOut(player, validAnimation, duration);
    }
    public void RegisterAnimations(Dictionary<AnimationType, List<AnimationParameters>> animations)
    {
        _animations = animations;

        foreach ((AnimationType animationType, List<AnimationParameters> variants) in _animations)
        {
            foreach (AnimationParameters data in variants)
            {
                _animationSystem.Register(data.HandsTp, AnimationData.Player(data.HandsTpCode));
                _animationSystem.Register(data.LegsTp, AnimationData.Player(data.LegsTpCode));
                _animationSystem.Register(data.HandsFp, AnimationData.Player(data.HandsFpCode));
                _animationSystem.Register(data.LegsFp, AnimationData.Player(data.LegsFpCode));
            }
        }
    }

    private readonly ICoreClientAPI _clientApi;
    private readonly IAnimationManagerSystem _animationSystem;
    private readonly AnimationId _verticalTrackingAnimation;
    private readonly AnimationId _gripAnimation;
    private readonly static Random _rand = new();
    private Dictionary<AnimationType, List<AnimationParameters>> _animations = new();

    private static int GetRandomIndex(int count) => _rand.Next(0, count - 1);
}

public sealed class SpearAttacksSystem : BaseSystem
{
    public SpearAttacksSystem(ICoreAPI api, SpearAnimationSystem? animations, Dictionary<SpearAnimationSystem.AnimationType, MeleeAttack> attacks, string debugName = "") : base(api, debugName)
    {
        _api = api;
        _side = api.Side;
        _attacks = attacks;
        _meleeSystem = new(api, $"melee-{debugName}");
        _animationSystem = animations;
    }
    public void Start(SpearAnimationSystem.AnimationType attackType, IPlayer player, ItemSlot slot, Action attackFinishedCallback, System.Func<bool>? terrainCollisionCallback = null, System.Func<bool>? entityCollisionCallback = null)
    {
        _animationSystem?.Play(player, attackType);
        if (_side == EnumAppSide.Client)
        {
            _meleeSystem.StartClientSide((long)attackType, player, _attacks[attackType], slot, result => AttackResultHandler(result, attackType, attackFinishedCallback, terrainCollisionCallback, entityCollisionCallback));
        }
        else
        {
            _meleeSystem.StartServerSide((long)attackType, player, _attacks[attackType], result => AttackResultHandler(result, attackType, attackFinishedCallback, terrainCollisionCallback, entityCollisionCallback));
        }
    }
    public void Stop(SpearAnimationSystem.AnimationType attackType, IPlayer player, TimeSpan? duration = null)
    {
        _meleeSystem.Stop((long)attackType, player);
        _animationSystem?.EaseOut(player, attackType, duration ?? TimeSpan.FromSeconds(0.5));
    }

    private readonly EnumAppSide _side;
    private readonly ICoreAPI _api;
    private readonly Dictionary<SpearAnimationSystem.AnimationType, MeleeAttack> _attacks;
    private readonly MeleeSystem _meleeSystem;
    private readonly SpearAnimationSystem? _animationSystem;

    private static bool AttackResultHandler(MeleeAttack.AttackResult result, SpearAnimationSystem.AnimationType attackType, Action attackFinishedCallback, System.Func<bool>? terrainCollisionCallback, System.Func<bool>? entityCollisionCallback)
    {
        if (result.Result == MeleeAttack.Result.Finished)
        {
            attackFinishedCallback?.Invoke();
            return true;
        }

        if (result.Terrain != null && result.Terrain.Any() && terrainCollisionCallback?.Invoke() == true)
        {
            return true;
        }

        if (result.Entities != null && result.Entities.Any() && entityCollisionCallback?.Invoke() == true)
        {
            return true;
        }

        return false;
    }
    private static bool AttackResultHandler(MeleeCollisionPacket result, SpearAnimationSystem.AnimationType attackType, Action attackFinishedCallback, System.Func<bool>? terrainCollisionCallback, System.Func<bool>? entityCollisionCallback)
    {
        if (result.Finished)
        {
            attackFinishedCallback?.Invoke();
            return true;
        }

        if (result.Blocks.Length > 0 && terrainCollisionCallback?.Invoke() == true)
        {
            return true;
        }

        if (result.Entities.Length > 0 && entityCollisionCallback?.Invoke() == true)
        {
            return true;
        }

        return false;
    }
}

public class AimingSystem
{
    private static readonly Random _rand = new();

    private readonly float _dispersionMin;
    private readonly float _dispersionMax;
    private readonly TimeSpan _aimTime;
    private readonly string _timeAttrName;
    private readonly Dictionary<long, float> _aimingTimes = new();
    private readonly Dictionary<long, float> _minDispersions = new();
    private readonly Dictionary<long, float> _maxDispersions = new();
    private bool _isAiming = false;
    private readonly ICoreAPI _api;


    public AimingSystem(float dispersionMin_MOA, float dispersionMax_MOA, TimeSpan duration, string attribute, ICoreAPI api)
    {
        _dispersionMin = dispersionMin_MOA;
        _dispersionMax = dispersionMax_MOA;
        _aimTime = duration;
        _timeAttrName = "fsmlib." + attribute + ".timePassed";
        _api = api;
    }

    public void Start(ItemSlot slot, IPlayer player)
    {
        player.Entity.Attributes.SetInt("aiming-noreticle", 1);
        WriteStartTimeTo(slot, _api.World.ElapsedMilliseconds);
        SetAimingTime(player);
        SetDispersions(player);
        _isAiming = true;
    }
    public void Stop(ItemSlot slot, IPlayer player)
    {
        player.Entity.Attributes.SetInt("aiming-noreticle", 0);
        WriteStartTimeTo(slot, 0);
        _isAiming = false;
    }

    public DirectionOffset GetShootingDirectionOffset(ItemSlot slot, IPlayer player)
    {
        long currentTime = _api.World.ElapsedMilliseconds;
        float aimProgress = _isAiming ? Math.Clamp((currentTime - ReadStartTimeFrom(slot)) / _aimingTimes[player.Entity.EntityId], 0, 1) : 0;
        float aimingInaccuracy = Math.Max(0.001f, 1f - player.Entity.Attributes.GetFloat("aimingAccuracy"));
        float dispersion = GetDispersion(aimProgress, player) * aimingInaccuracy;
        float randomPitch = (float)(2 * (_rand.NextDouble() - 0.5) * (Math.PI / 180 / 60) * dispersion);
        float randomYaw = (float)(2 * (_rand.NextDouble() - 0.5) * (Math.PI / 180 / 60) * dispersion);
        return new(Angle.FromDegrees(randomPitch), Angle.FromDegrees(randomYaw));
    }
    public TimeSpan GetAimingDuration(ItemSlot slot, IPlayer player)
    {
        long entityId = player.Entity.EntityId;
        if (_aimingTimes.ContainsKey(entityId))
        {
            return TimeSpan.FromSeconds(_aimingTimes[entityId]);
        }

        return _aimTime;
    }
    private void SetAimingTime(IPlayer player)
    {
        long entityId = player.Entity.EntityId;
        if (!_aimingTimes.ContainsKey(entityId)) _aimingTimes.Add(entityId, 0);
        _aimingTimes[entityId] = (float)_aimTime.TotalSeconds;
    }
    private void SetDispersions(IPlayer player)
    {
        long entityId = player.Entity.EntityId;

        if (!_minDispersions.ContainsKey(entityId)) _minDispersions.Add(entityId, 0);
        _minDispersions[entityId] = _dispersionMin;

        if (!_maxDispersions.ContainsKey(entityId)) _maxDispersions.Add(entityId, 0);
        _maxDispersions[entityId] = _dispersionMax;
    }
    private float GetDispersion(float progress, IPlayer player)
    {
        long entityId = player.Entity.EntityId;

        return Math.Max(0, _maxDispersions[entityId] - (_maxDispersions[entityId] - _minDispersions[entityId]) * progress);
    }
    private void WriteStartTimeTo(ItemSlot slot, long time)
    {
        slot?.Itemstack?.Attributes.SetLong(_timeAttrName, time);
        slot?.MarkDirty();
    }
    private long ReadStartTimeFrom(ItemSlot slot)
    {
        long? startTime = slot?.Itemstack?.Attributes?.GetLong(_timeAttrName, 0);
        return startTime == null || startTime == 0 ? _api.World.ElapsedMilliseconds : startTime.Value;
    }
}

public class ProjectileSystem
{
    private readonly string _impactSound;
    private readonly string _hitSound;
    private readonly ICoreAPI _api;

    public ProjectileSystem(ICoreAPI api, string impactSound = "game:sounds/arrow-impact", string hitSound = "game:sounds/player/projectilehit")
    {
        _impactSound = impactSound;
        _hitSound = hitSound;
        _api = api;
    }

    public void Spawn(ItemStack stack, IPlayer player, float speed, float damageMultiplier, DirectionOffset directionOffset)
    {
        for (int count = 0; count < stack.StackSize; count++)
        {
            Vec3d projectilePosition = ProjectilePosition(player, new Vec3f(0.0f, 0.0f, 0.0f));
            Vec3d projectileVelocity = ProjectileVelocity(player, directionOffset, speed);
            SpawnProjectile(stack, player, projectilePosition, projectileVelocity, damageMultiplier);
        }
    }
    private static Vec3d ProjectilePosition(IPlayer player, Vec3f muzzlePosition)
    {
        Vec3f worldPosition = MaltiezFSM.Framework.Utils.FromCameraReferenceFrame(player.Entity, muzzlePosition);
        return player.Entity.SidedPos.AheadCopy(0).XYZ.Add(worldPosition.X, player.Entity.LocalEyePos.Y + worldPosition.Y, worldPosition.Z);
    }
    private static Vec3d ProjectileVelocity(IPlayer player, DirectionOffset dispersion, float speed)
    {
        Vec3d pos = player.Entity.ServerPos.XYZ.Add(0, player.Entity.LocalEyePos.Y, 0);
        Vec3d aheadPos = pos.AheadCopy(1, player.Entity.SidedPos.Pitch + dispersion.Pitch.Radians, player.Entity.SidedPos.Yaw + dispersion.Yaw.Radians);
        return (aheadPos - pos).Normalize() * speed;
    }
    private void SpawnProjectile(ItemStack projectileStack, IPlayer player, Vec3d position, Vec3d velocity, float damageMultiplier)
    {
        if (projectileStack?.Item?.Code == null) return;

        List<MaltiezFSM.Systems.ProjectileDamageType>? damageTypes = projectileStack.Collectible.GetBehavior<MaltiezFSM.Systems.AdvancedProjectileBehavior>()?.DamageTypes;

        if (damageTypes == null || damageTypes.Count == 0)
        {
            return;
        }

        AssetLocation entityTypeAsset = new(projectileStack.Collectible.Attributes["projectile"].AsString(projectileStack.Collectible.Code.Path));

        EntityProperties entityType = _api.World.GetEntityType(entityTypeAsset);

        if (entityType == null)
        {
            return;
        }


        if (_api.ClassRegistry.CreateEntity(entityType) is not MaltiezFSM.Systems.AdvancedEntityProjectile projectile)
        {
            return;
        }

        projectile.FiredBy = player.Entity;
        projectile.ProjectileStack = projectileStack;
        projectile.DropOnImpactChance = 1 - projectileStack.Collectible.Attributes["breakChanceOnImpact"].AsFloat(0);
        projectile.DamageStackOnImpact = true;
        projectile.ServerPos.SetPos(position);
        projectile.ServerPos.Motion.Set(velocity);
        projectile.Pos.SetFrom(projectile.ServerPos);
        projectile.World = _api.World;
        projectile.SetRotation();

        projectile.DamageTypes = damageTypes;
        projectile.DamageMultiplier = damageMultiplier;
        projectile.HitSound = _hitSound;
        projectile.ImpactSound = _impactSound;

        _api.World.SpawnEntity(projectile);
    }
}