using MeleeWeaponsFramework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;

namespace MaltiezSpears.Javelins;

public sealed class JavelinStats : MeleeWeaponParameters
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

public class Javelin : MeleeWeaponItem
{
    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        Stats = Attributes[MeleeWeaponStatsAttribute].AsObject<JavelinStats>();
    }

    protected JavelinStats Stats;
}
