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

public class SpearFsm : SpearControls
{
    public static readonly TimeSpan AimToHoldDelay = TimeSpan.FromSeconds(0.5);
    
    public SpearFsm(ICoreAPI api, CollectibleObject collectible, PikeStats stats) : base(api, collectible, AimToHoldDelay)
    {
        
    }
}

public sealed class SpearStats
{
    public float MaxReach { get; set; }
    public float OneHandedDamage { get; set; }
    public float TwoHandedDamage { get; set; }
    public float ShaftDamage { get; set; }
    public float PushKnockback { get; set; }
    public float ShaftHitKnockback { get; set; }
    public float TwoHandedKnockback { get; set; }
    public float OneHandedKnockback { get; set; }
    public int OneHandedTier { get; set; }
    public int TwoHandedTier { get; set; }
    public float[] SpearHeadCollider { get; set; } = new float[6];
    public float[] ShaftCollider { get; set; } = new float[6];
    public string? HeadHitTerrainSound { get; set; } = null;
    public string? HeadHitEntitySound { get; set; } = null;
    public string? ShaftHitTerrainSound { get; set; } = null;
    public string? ShaftHitEntitySound { get; set; } = null;
    public float[] OneHandedAttackWindowMs { get; set; } = new float[2];
    public float[] TwoHandedAttackWindowMs { get; set; } = new float[2];
    public float[] PushAttackWindowMs { get; set; } = new float[2];
}
