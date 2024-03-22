using AnimationManagerLib;
using AnimationManagerLib.API;
using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Spears;

public class SpearsModSystem : ModSystem
{
    public override void Start(ICoreAPI api)
    {
        api.RegisterItemClass("Spears:SpearItem", typeof(SpearItem));
    }
}

public class SpearItem : Item
{
    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        SpearStats stats;

        _fsm = new(api, this, stats);
    }

    public override void OnHeldRenderOpaque(ItemSlot inSlot, IClientPlayer byPlayer)
    {
        base.OnHeldRenderOpaque(inSlot, byPlayer);

        _fsm?.OnRender(inSlot, byPlayer);
    }

    private SpearFsm? _fsm;
}

public class AnimationSystem_old
{
    public AnimationSystem_old(ICoreClientAPI api)
    {
        _clientApi = api;
        _animationSystem = api.ModLoader.GetModSystem<AnimationManagerLibSystem>();

        _verticalTrackingAnimation = new("tracking", "spears-vertical-tracking", EnumAnimationBlendMode.AddAverage, weight: 1);
        _meleeAttackAnimation = new("attack", "spears-javelin-attack-0", EnumAnimationBlendMode.Average, weight: 512);

        _animationSystem.Register(_verticalTrackingAnimation, AnimationData.Player("spears-vertical-tracking"));
        _animationSystem.Register(_meleeAttackAnimation, AnimationData.Player("spears-javelin"));
    }

    public void Track(IClientPlayer byPlayer)
    {
        if (byPlayer != _clientApi.World.Player) return;

        float angle = byPlayer.CameraPitch * GameMath.RAD2DEG - 180;
        float trackingFactor = 0.8f;

        AnimationSequence sequence = new(
            _verticalTrackingAnimation,
            RunParameters.Set(90 + angle * trackingFactor),
            RunParameters.EaseOut(TimeSpan.FromSeconds(1))
            );

        //VSImGui.Debug.DebugWidgets.Text("Spears", "camera", 0, $"Angle: {angle}");

        bool immersiveFp = _clientApi.Settings.Bool["immersiveFpMode"];
        _animationSystem.Run(new AnimationTarget(byPlayer.Entity.EntityId, AnimationTargetType.EntityThirdPerson), sequence);
        if (immersiveFp) _animationSystem.Run(new AnimationTarget(byPlayer.Entity.EntityId, AnimationTargetType.EntityFirstPerson), sequence);
    }
    public void StartAttack(IPlayer byPlayer)
    {
        AnimationSequence sequence = new(
            _meleeAttackAnimation,
            RunParameters.EaseIn(TimeSpan.FromMilliseconds(200), 0, ProgressModifierType.Sqrt),
            RunParameters.EaseIn(TimeSpan.FromMilliseconds(300), 1, ProgressModifierType.Cubic),
            RunParameters.EaseOut(TimeSpan.FromMilliseconds(400))
            );

        _animationSystem.Run(new AnimationTarget(byPlayer.Entity.EntityId, AnimationTargetType.EntityThirdPerson), sequence);
        _animationSystem.Run(new AnimationTarget(byPlayer.Entity.EntityId, AnimationTargetType.EntityFirstPerson), sequence);
    }
    public void StopAttack(IPlayer byPlayer)
    {
        AnimationSequence sequence = new(
            _meleeAttackAnimation,
            RunParameters.EaseOut(TimeSpan.FromMilliseconds(400))
            );

        _animationSystem.Run(new AnimationTarget(byPlayer.Entity.EntityId, AnimationTargetType.EntityThirdPerson), sequence);
        _animationSystem.Run(new AnimationTarget(byPlayer.Entity.EntityId, AnimationTargetType.EntityFirstPerson), sequence);
    }

    private readonly ICoreClientAPI _clientApi;
    private readonly IAnimationManagerSystem _animationSystem;
    private readonly AnimationId _verticalTrackingAnimation;
    private readonly AnimationId _meleeAttackAnimation;
}