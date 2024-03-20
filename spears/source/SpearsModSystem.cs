using AnimationManagerLib;
using AnimationManagerLib.API;
using MaltiezFSM.Framework.Simplified.Systems;
using System;
using System.Numerics;
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

        _clientApi = api as ICoreClientAPI;

        if (_clientApi != null) _animationSystem = new(_clientApi);


        _fsm = new(api, this, _animationSystem);
    }

    public override void OnHeldRenderOpaque(ItemSlot inSlot, IClientPlayer byPlayer)
    {
        base.OnHeldRenderOpaque(inSlot, byPlayer);

        _fsm?.DebugRender(byPlayer, inSlot);

        _animationSystem?.Track(byPlayer);
    }

    private SpearFsm? _fsm;
    private ICoreClientAPI? _clientApi;
    private AnimationSystem? _animationSystem;
}

public class AnimationSystem
{
    public AnimationSystem(ICoreClientAPI api)
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

public class SpearFsm : MaltiezFSM.Framework.Simplified.BaseItemInteractions
{
    public SpearFsm(ICoreAPI api, CollectibleObject collectible, AnimationSystem? animationSystem) : base(api, collectible)
    {
        _api = api;

        if (api is ICoreClientAPI clientApi)
        {
            _meleeSystem = new(clientApi, "SpearMeleeSystem");
            _meleeAttackDamageType = new(5.0f, EnumDamageType.PiercingAttack, new(new(0, 0.05f, -1.6f), new(0, 0, -0.5f)));
            _meleeAttack = new(clientApi, new(TimeSpan.FromSeconds(0.3f), TimeSpan.FromSeconds(0.5f)), new MeleeAttackDamageType[] { _meleeAttackDamageType }, 5.0f);
            _animationSystem = animationSystem;
        }
    }

    public void DebugRender(IClientPlayer byPlayer, ItemSlot inSlot)
    {
        _meleeAttack?.RenderDebugColliders(byPlayer, inSlot);
    }

    protected override bool OnAttackStart(ItemSlot slot, IPlayer? player)
    {
        _api.Logger.Warning($"Attack start");

        if (_meleeSystem == null || _meleeAttack == null || player == null) return true;

        _attackId = _meleeSystem.Start(player, _meleeAttack, slot, result => OnAttackHit(result, slot, player));

        _animationSystem?.StartAttack(player);

        return true;
    }

    protected virtual bool OnAttackHit(MeleeAttack.AttackResult result, ItemSlot inSlot, IPlayer player)
    {
        _api.Logger.Warning($"Attack hit: {result.Result}");

        if (result.Terrain != null)
        {
            foreach ((_, Vector3 point) in result.Terrain)
            {
                AdvancedParticleProperties advancedParticleProperties = new();
                advancedParticleProperties.basePos.X = point.X;
                advancedParticleProperties.basePos.Y = point.Y;
                advancedParticleProperties.basePos.Z = point.Z;
                _api.World.SpawnParticles(advancedParticleProperties);
            }
        }

        if (result.Entities != null)
        {
            foreach ((_, Vector3 point) in result.Entities)
            {
                AdvancedParticleProperties advancedParticleProperties = new();
                advancedParticleProperties.basePos.X = point.X;
                advancedParticleProperties.basePos.Y = point.Y;
                advancedParticleProperties.basePos.Z = point.Z;
                _api.World.SpawnParticles(advancedParticleProperties);
            }
        }

        if (result.Result == MeleeAttack.Result.HitTerrain || result.Result == MeleeAttack.Result.Finished)
        {
            Fsm.SetState(inSlot, "idle");
            _animationSystem?.StopAttack(player);
            return true;
        }

        return false;
    }

    protected override bool OnAttackCancel(ItemSlot slot, IPlayer? player)
    {
        _api.Logger.Warning($"Attack cancel");
        _animationSystem?.StopAttack(player);
        _meleeSystem?.Stop(_attackId);
        return true;
    }

    private readonly MeleeSystem? _meleeSystem;
    private readonly MeleeAttack? _meleeAttack;
    private readonly MeleeAttackDamageType? _meleeAttackDamageType;
    private readonly AnimationSystem? _animationSystem;
    private readonly ICoreAPI _api;
    private long _attackId = 0;
}