using AnimationManagerLib;
using AnimationManagerLib.API;
using MaltiezFSM.Framework.Simplified.Systems;
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
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
        High2hBlock
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
            HandsTpCode = $"{code}-hands";
            LegsTpCode = $"{code}-legs";
            HandsFpCode = $"{code}-hands-fp";
            LegsFpCode = $"{code}-legs-fp";
            HandsTp = new("hands", HandsTpCode, EnumAnimationBlendMode.Average, weight: 512);
            LegsTp = new("hands", LegsTpCode, EnumAnimationBlendMode.Average, weight: 1);
            HandsFp = new("hands", HandsFpCode, EnumAnimationBlendMode.Average, weight: 512);
            LegsFp = new("hands", LegsFpCode, EnumAnimationBlendMode.Average, weight: 1);
            Parameters = parameters;
        }
    }

    public SpearAnimationSystem(ICoreClientAPI api, string debugName = "") : base(api, debugName)
    {
        _clientApi = api;
        _animationSystem = api.ModLoader.GetModSystem<AnimationManagerLibSystem>();
        _verticalTrackingAnimation = new("tracking", "spears-vertical-tracking", EnumAnimationBlendMode.AddAverage, weight: 1);
        _animationSystem.Register(_verticalTrackingAnimation, AnimationData.Player("spears-vertical-tracking"));
    }

    public void Track(IClientPlayer byPlayer, float trackingFactor = 0.8f)
    {
        float angle = byPlayer.CameraPitch * GameMath.RAD2DEG - 180;

        AnimationSequence sequence = new(
            _verticalTrackingAnimation,
            RunParameters.Set(90 + angle * trackingFactor),
            RunParameters.EaseOut(TimeSpan.FromSeconds(1))
            );

        _animationSystem.Run(new AnimationTarget(byPlayer.Entity.EntityId, AnimationTargetType.EntityThirdPerson), sequence);

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
    private readonly static Random _rand = new();
    private Dictionary<AnimationType, List<AnimationParameters>> _animations = new();

    private static int GetRandomIndex(int count) => (int)Math.Floor((decimal)(_rand.NextDouble() % (count - 1)));
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
    public void Start(SpearAnimationSystem.AnimationType attackType, IPlayer player, ItemSlot slot, System.Func<bool>? terrainCollisionCallback = null, System.Func<bool>? entityCollisionCallback = null)
    {
        _animationSystem?.Play(player, attackType);
        if (_side == EnumAppSide.Client)
        {
            _meleeSystem.StartClientSide((long)attackType, player, _attacks[attackType], slot, result => AttackResultHandler(result, attackType, terrainCollisionCallback, entityCollisionCallback));
        }
        else
        {
            _meleeSystem.StartServerSide((long)attackType, player, _attacks[attackType], result => AttackResultHandler(result, attackType, terrainCollisionCallback, entityCollisionCallback));
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

    private static bool AttackResultHandler(MeleeAttack.AttackResult result, SpearAnimationSystem.AnimationType attackType, System.Func<bool>? terrainCollisionCallback, System.Func<bool>? entityCollisionCallback)
    {
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
    private static bool AttackResultHandler(MeleeCollisionPacket result, SpearAnimationSystem.AnimationType attackType, System.Func<bool>? terrainCollisionCallback, System.Func<bool>? entityCollisionCallback)
    {
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