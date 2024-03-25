using MaltiezFSM.Framework;
using MaltiezFSM.Framework.Simplified.Systems;
using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Spears;

public class BlockEntityBehavior : EntityBehavior
{
    public BlockEntityBehavior(Entity entity) : base(entity)
    {
        CoreApi = entity.Api;
        Name = "spears:block";
    }

    public override string PropertyName()
    {
        return Name;
    }
    public override void OnEntityReceiveDamage(DamageSource damageSource, ref float damage)
    {
        if (CurrentBlock == null) return;

        if (!CheckDirection(entity, damageSource.HitPosition)) return;

        long currentTime = CoreApi.World.ElapsedMilliseconds;
        if (currentTime - BlockStartTime <= PerfectBlockTime)
        {
            OnPerfectBlock(CurrentBlock.Value, damageSource, ref damage);
        }
        else
        {
            OnBlock(CurrentBlock.Value, damageSource, ref damage);
        }
    }

    public void Start(BlockParameters parameters)
    {
        Stop();
        BlockStartTime = CoreApi.World.ElapsedMilliseconds;
        PerfectBlockTime = (long)parameters.PerfectBlockWindow.TotalMilliseconds;
    }
    public void Stop()
    {
        if (CurrentBlock != null)
        {
            OnCancel(CurrentBlock.Value);
        }
        CurrentBlock = null;
    }


    protected readonly string Name;
    protected BlockParameters? CurrentBlock;
    protected ICoreAPI CoreApi;
    protected long BlockStartTime;
    protected long PerfectBlockTime;

    protected virtual void OnPerfectBlock(BlockParameters parameters, DamageSource damageSource, ref float damage)
    {
        parameters.PerfectBlockDamageHandler(entity, damageSource, ref damage);
    }
    protected virtual void OnBlock(BlockParameters parameters, DamageSource damageSource, ref float damage)
    {
        parameters.BlockDamageHandler(entity, damageSource, ref damage);
    }
    protected virtual void OnCancel(BlockParameters parameters)
    {
        parameters.CancelCallback(entity, this);
    }

    protected virtual bool CheckDirection(Entity receiver, Vec3d hitPosition)
    {
        if (CurrentBlock == null) return false;

        Vec3f attackDirection = (hitPosition - receiver.LocalEyePos).ToVec3f();
        Vec3f playerViewDirection = EntityPos.GetViewVector(receiver.SidedPos.Pitch, receiver.SidedPos.Yaw);
        Vec3f direction = Utils.ToReferenceFrame(playerViewDirection, attackDirection);
        DirectionOffset offset = new(direction, new Vec3f(0, 0, 1));

        return CurrentBlock.Value.Coverage.Check(offset);
    }
}

public delegate void BlockDelegate(Entity entity, DamageSource damageSource, ref float damage);

public readonly struct BlockParameters
{
    public readonly BlockDelegate BlockDamageHandler;
    public readonly BlockDelegate PerfectBlockDamageHandler;
    public readonly TimeSpan PerfectBlockWindow;
    public readonly Action<Entity, BlockEntityBehavior> CancelCallback;
    public readonly DirectionConstrain Coverage;

    public BlockParameters(BlockDelegate damageHandler, BlockDelegate prefectBlockDamageHandler, TimeSpan perfectBlockWindow, Action<Entity, BlockEntityBehavior> cancelCallback, DirectionConstrain coverage)
    {
        BlockDamageHandler = damageHandler;
        PerfectBlockDamageHandler = prefectBlockDamageHandler;
        PerfectBlockWindow = perfectBlockWindow;
        CancelCallback = cancelCallback;
        Coverage = coverage;
    }

    public static BlockParameters FrontBlock(
        ICoreAPI api,
        float damageReduction,
        AssetLocation? blockSound = null,
        AssetLocation? perfectBlockSound = null,
        AssetLocation? cancelSound = null,
        TimeSpan? perfectBlockWindow = null)
    {
        void regularHandler(Entity entity, DamageSource damageSource, ref float damage)
        {
            if (damageSource.Type != EnumDamageType.BluntAttack && damageSource.Type != EnumDamageType.PiercingAttack && damageSource.Type != EnumDamageType.SlashingAttack) return;

            damage *= damageReduction;

            if (blockSound != null && api.Side == EnumAppSide.Server) api.World.PlaySoundAt(blockSound, entity);
        }

        void perfectHandler(Entity entity, DamageSource damageSource, ref float damage)
        {
            if (damageSource.Type != EnumDamageType.BluntAttack && damageSource.Type != EnumDamageType.PiercingAttack && damageSource.Type != EnumDamageType.SlashingAttack) return;

            damage = 0;

            if (perfectBlockSound != null && api.Side == EnumAppSide.Server) api.World.PlaySoundAt(perfectBlockSound, entity);
        }

        void cancelHandler(Entity entity, BlockEntityBehavior behavior)
        {
            if (cancelSound != null && api.Side == EnumAppSide.Server) api.World.PlaySoundAt(cancelSound, entity);
        }

        DirectionConstrain coverage = DirectionConstrain.FromDegrees(120, -120, 120, -120);

        return new(regularHandler, perfectHandler, perfectBlockWindow ?? TimeSpan.Zero, cancelHandler, coverage);
    }
}

public readonly struct DirectionConstrain
{
    /// <summary>
    /// In radians. Positive direction: top.
    /// </summary>
    public readonly Angle PitchTop;
    /// <summary>
    /// In radians. Positive direction: top.
    /// </summary>
    public readonly Angle PitchBottom;
    /// <summary>
    /// In radians. Positive direction: right.
    /// </summary>
    public readonly Angle YawLeft;
    /// <summary>
    /// In radians. Positive direction: right.
    /// </summary>
    public readonly Angle YawRight;

    public DirectionConstrain(Angle pitchTop, Angle pitchBottom, Angle yawRight, Angle yawLeft)
    {
        PitchTop = pitchTop;
        PitchBottom = pitchBottom;
        YawLeft = yawLeft;
        YawRight = yawRight;
    }

    public static DirectionConstrain FromDegrees(float top, float bottom, float right, float left)
    {
        return new(Angle.FromDegrees(top), Angle.FromDegrees(bottom), Angle.FromDegrees(right), Angle.FromDegrees(left));
    }

    public bool Check(DirectionOffset offset)
    {
        return offset.Pitch <= PitchTop &&
            offset.Pitch >= PitchBottom &&
            offset.Yaw >= YawLeft &&
            offset.Yaw <= YawRight;
    }
}