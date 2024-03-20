using MaltiezFSM.API;
using MaltiezFSM.Framework.Simplified;
using MaltiezFSM.Inputs;
using System.Collections.Generic;
using Vintagestory.API.Common;

namespace Spears;

public class SpearControls
{
    public SpearControls(ICoreAPI api, CollectibleObject collectible)
    {
        Fsm = new FiniteStateMachineAttributesBased(api, States, "onehanded-lower-idle");

        BaseInputProperties inputProperties = new()
        {
            Statuses = new IStatusModifier.StatusType[] { IStatusModifier.StatusType.OnGround },
            StatusesCheckType = IStandardInput.MultipleCheckType.All,
            KeyModifiers = EnumModifierKey.ALT,
            KeyModifiersCheckType = IKeyModifier.KeyModifierType.NotPresent
        };

        RightMouseDown = new(api, "rmb", collectible, new(EnumEntityAction.RightMouseDown), inputProperties);
        LeftMouseDown = new(api, "lmb", collectible, new(EnumEntityAction.LeftMouseDown), inputProperties);
        RightMouseUp = new(api, "rmb-up", collectible, new(EnumEntityAction.RightMouseDown));
        LeftMouseUp = new(api, "lmb-up", collectible, new(EnumEntityAction.LeftMouseDown));
        StanceChange = new(api, "stance", collectible, Vintagestory.API.Client.GlKeys.R);
        GripChange = new(api, "grip", collectible, Vintagestory.API.Client.GlKeys.F);
        ItemDropped = new(api, "dropped", collectible, ISlotContentInput.SlotEventType.AllTaken);
        SlotDeselected = new(api, "deselected", collectible);

        Fsm.Init(this, collectible);
    }

    public enum StanceType
    {
        OneHandedUpper,
        OneHandedLower,
        TwoHandedUpper,
        TwoHandedLower
    };

    protected virtual void OnDeselected(ItemSlot slot, IPlayer player)
    {

    }
    protected virtual bool OnStartAttack(ItemSlot slot, IPlayer player, StanceType stanceType, bool blocking)
    {
        return false;
    }

    protected virtual bool OnStartBlock(ItemSlot slot, IPlayer player, StanceType stanceType, bool attacking)
    {
        return false;
    }

    protected virtual void OnCancelAttack(ItemSlot slot, IPlayer player, StanceType stanceType)
    {

    }

    protected virtual void OnCancelBlock(ItemSlot slot, IPlayer player, StanceType stanceType)
    {

    }

    protected virtual void OnStanceChange(ItemSlot slot, IPlayer player, StanceType newStance)
    {

    }

    protected StanceType GetStance(ItemSlot slot)
    {
        bool onehanded = Fsm.CheckState(slot, 0, "onehanded");
        bool lowerGrip = Fsm.CheckState(slot, 1, "lower");

        return (onehanded, lowerGrip) switch
        {
            (true, true) => StanceType.OneHandedLower,
            (true, false) => StanceType.OneHandedUpper,
            (false, true) => StanceType.TwoHandedLower,
            (false, false) => StanceType.TwoHandedLower
        };
    }
    protected bool Blocking(ItemSlot slot) => Fsm.CheckState(slot, 1, "block");
    protected bool Attacking(ItemSlot slot) => Fsm.CheckState(slot, 1, "attack");
    protected void EnsureStance(ItemSlot slot, IPlayer player)
    {
        bool onehanded = Fsm.CheckState(slot, 0, "onehanded");
        if (player.Entity.LeftHandItemSlot.Empty || !onehanded) return;
        
        Fsm.SetState(slot, (0, "onehanded"));
        OnStanceChange(slot, player, GetStance(slot));
    }

    #region FSM
    protected IFiniteStateMachineAttributesBased Fsm;
    protected static readonly List<HashSet<string>> States = new()
    {
        new() // Grip
        {
            "onehanded",
            "twohanded"
        },
        new() // Stance
        {
            "lower",
            "upper"
        },
        new() // Action
        {
            "idle",
            "attack",
            "block"
        }
    };

    [Input]
    protected ActionInput RightMouseDown { get; }
    [Input]
    protected ActionInput LeftMouseDown { get; }
    [Input]
    protected ActionInput RightMouseUp { get; }
    [Input]
    protected ActionInput LeftMouseUp { get; }
    [Input]
    protected KeyboardKey StanceChange { get; }
    [Input]
    protected KeyboardKey GripChange { get; }

    [Input]
    protected SlotContent ItemDropped { get; }
    [Input]
    protected BeforeSlotChanged SlotDeselected { get; }

    [InputHandler(state: "*-*-*", "ItemDropped", "SlotDeselected")]
    protected bool Deselected(ItemSlot slot, IPlayer? player, IInput input, IState state)
    {
        if (player == null) return false;
        Fsm.SetState(slot, "onehanded-lower-idle");
        OnDeselected(slot, player);
        return false;
    }

    [InputHandler(state: "@*-*-(idle|block)", "RightMouseDown")]
    protected bool StartAttack(ItemSlot slot, IPlayer? player, IInput input, IState state)
    {
        if (player == null) return false;
        EnsureStance(slot, player);
        if (OnStartAttack(slot, player, GetStance(slot), Blocking(slot)))
        {
            Fsm.SetState(slot, (2, "attack"));
            return true;
        }
        return false;
    }

    [InputHandler(state: "*-*-attack", "RightMouseUp")]
    protected bool CancelAttack(ItemSlot slot, IPlayer? player, IInput input, IState state)
    {
        if (player == null) return false;
        OnCancelAttack(slot, player, GetStance(slot));
        Fsm.SetState(slot, (2, "idle"));
        return false;
    }

    [InputHandler(state: "*-*-(idle|attack)", "LeftMouseDown")]
    protected bool StartBlock(ItemSlot slot, IPlayer? player, IInput input, IState state)
    {
        if (player == null) return false;
        EnsureStance(slot, player);
        if (OnStartBlock(slot, player, GetStance(slot), Attacking(slot)))
        {
            Fsm.SetState(slot, (2, "block"));
            return true;
        }
        return false;
    }

    [InputHandler(state: "*-*-block", "LeftMouseUp")]
    protected bool CancelBlock(ItemSlot slot, IPlayer? player, IInput input, IState state)
    {
        if (player == null) return false;
        OnCancelBlock(slot, player, GetStance(slot));
        Fsm.SetState(slot, (2, "idle"));
        return false;
    }

    [InputHandler(state: "@*-lower-(idle|block)", "StanceChange")]
    protected bool ToggleStanceToLower(ItemSlot slot, IPlayer? player, IInput input, IState state)
    {
        if (player == null) return false;
        Fsm.SetState(slot, (1, "upper"));
        OnStanceChange(slot, player, GetStance(slot));
        return false;
    }
    [InputHandler(state: "@*-upper-(idle|block)", "StanceChange")]
    protected bool ToggleStanceToUpper(ItemSlot slot, IPlayer? player, IInput input, IState state)
    {
        if (player == null) return false;
        Fsm.SetState(slot, (1, "lower"));
        OnStanceChange(slot, player, GetStance(slot));
        return false;
    }

    [InputHandler(state: "@*-*-(idle|block)", "GripChange")]
    protected bool ToggleGripToOnehanded(ItemSlot slot, IPlayer? player, IInput input, IState state)
    {
        if (player == null) return false;
        Fsm.SetState(slot, (0, "twohanded"));
        OnStanceChange(slot, player, GetStance(slot));
        return true;
    }
    [InputHandler(state: "@*-*-(idle|block)", "GripChange")]
    protected bool ToggleGripToTwohanded(ItemSlot slot, IPlayer? player, IInput input, IState state)
    {
        if (player == null || !player.Entity.LeftHandItemSlot.Empty) return false;
        Fsm.SetState(slot, (0, "onehanded"));
        OnStanceChange(slot, player, GetStance(slot));
        return true;
    }
    #endregion
}
