using MaltiezFSM.API;
using MaltiezFSM.Framework.Simplified;
using MaltiezFSM.Inputs;
using System;
using System.Collections.Generic;
using Vintagestory.API.Common;

namespace Spears;

public abstract class BaseControls
{
    protected BaseControls(ICoreAPI api, CollectibleObject collectible)
    {
        Console.WriteLine("Init BaseControls");

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
        RightMouseUp = new(api, "rmb-up", collectible, new(EnumEntityAction.RightMouseDown) { OnRelease = true });
        LeftMouseUp = new(api, "lmb-up", collectible, new(EnumEntityAction.LeftMouseDown) { OnRelease = true });
        StanceChange = new(api, "stance", collectible, Vintagestory.API.Client.GlKeys.R);
        GripChange = new(api, "grip", collectible, Vintagestory.API.Client.GlKeys.F);
        ItemDropped = new(api, "dropped", collectible, ISlotContentInput.SlotEventType.AllTaken);
        SlotDeselected = new(api, "deselected", collectible);
        ItemAdded = new(api, "dropped", collectible, ISlotContentInput.SlotEventType.AfterModified);
        SlotDeselected = new(api, "deselected", collectible, ISlotInput.SlotEventType.ToSlot);

        Fsm.Init(this, collectible);
    }

    protected enum StanceType
    {
        OneHandedUpper,
        OneHandedLower,
        TwoHandedUpper,
        TwoHandedLower
    };

    protected virtual void OnDeselected(ItemSlot slot, IPlayer player)
    {

    }
    protected virtual void OnSelected(ItemSlot slot, IPlayer player)
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

    protected virtual void OnCancelAttack(ItemSlot slot, IPlayer player, StanceType stanceType, bool blocking)
    {

    }

    protected virtual void OnCancelBlock(ItemSlot slot, IPlayer player, StanceType stanceType, bool attacking)
    {

    }

    protected virtual void OnStanceChange(ItemSlot slot, IPlayer player, StanceType newStance, bool blocking)
    {

    }

    protected void CancelAttack(ItemSlot slot, IPlayer player, bool blocking)
    {
        Fsm.SetState(slot, (2, "idle"));
        OnCancelAttack(slot, player, GetStance(slot), blocking);
    }

    protected void CancelBlock(ItemSlot slot, IPlayer player, bool attacking)
    {
        Fsm.SetState(slot, (2, "idle"));
        OnCancelBlock(slot, player, GetStance(slot), attacking);
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
            (false, false) => StanceType.TwoHandedUpper
        };
    }
    protected bool Blocking(ItemSlot slot) => Fsm.CheckState(slot, 2, "block");
    protected bool Attacking(ItemSlot slot) => Fsm.CheckState(slot, 2, "attack");
    protected void EnsureStance(ItemSlot slot, IPlayer player)
    {
        bool onehanded = Fsm.CheckState(slot, 0, "onehanded");
        if (player.Entity.LeftHandItemSlot.Empty || !onehanded) return;
        
        Fsm.SetState(slot, (0, "onehanded"));
        OnStanceChange(slot, player, GetStance(slot), Blocking(slot));
    }

    #region FSM
    protected IFiniteStateMachineAttributesBased Fsm;
    private static readonly List<HashSet<string>> States = new()
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
    [Input]
    protected SlotContent ItemAdded { get; }
    [Input]
    protected AfterSlotChanged SlotSelected { get; }

    [InputHandler(state: "*-*-*", "ItemDropped", "SlotDeselected")]
    protected bool Deselected(ItemSlot slot, IPlayer? player, IInput input, IState state)
    {
        if (player == null) return false;
        Fsm.SetState(slot, "onehanded-lower-idle");
        OnDeselected(slot, player);
        return false;
    }
    [InputHandler(state: "*-*-*", "ItemAdded", "SlotSelected")]
    protected bool Selected(ItemSlot slot, IPlayer? player, IInput input, IState state)
    {
        if (player == null) return false;
        Fsm.SetState(slot, "onehanded-lower-idle");
        OnSelected(slot, player);
        return false;
    }

    [InputHandler(states: new string[] { "*-*-idle", "*-*-block" }, "LeftMouseDown")]
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

    [InputHandler(state: "*-*-attack", "LeftMouseUp")]
    protected bool CancelAttack(ItemSlot slot, IPlayer? player, IInput input, IState state)
    {
        if (player == null) return false;
        Fsm.SetState(slot, (2, "idle"));
        OnCancelAttack(slot, player, GetStance(slot), Blocking(slot));
        return false;
    }

    [InputHandler(states: new string[] { "*-*-idle", "*-*-attack" }, "RightMouseDown")]
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

    [InputHandler(state: "*-*-block", "RightMouseUp")]
    protected bool CancelBlock(ItemSlot slot, IPlayer? player, IInput input, IState state)
    {
        if (player == null) return false;
        Fsm.SetState(slot, (2, "idle"));
        OnCancelBlock(slot, player, GetStance(slot), Attacking(slot));
        return false;
    }

    [InputHandler(states: new string[] { "*-lower-idle", "*-lower-block" }, "StanceChange")]
    protected bool ToggleStanceToLower(ItemSlot slot, IPlayer? player, IInput input, IState state)
    {
        if (player == null) return false;
        Fsm.SetState(slot, (1, "upper"));
        OnStanceChange(slot, player, GetStance(slot), Blocking(slot));
        return false;
    }
    [InputHandler(states: new string[] { "*-upper-idle", "*-upper-block" }, "StanceChange")]
    protected bool ToggleStanceToUpper(ItemSlot slot, IPlayer? player, IInput input, IState state)
    {
        if (player == null) return false;
        Fsm.SetState(slot, (1, "lower"));
        OnStanceChange(slot, player, GetStance(slot), Blocking(slot));
        return false;
    }

    [InputHandler(states: new string[] { "onehanded-*-idle", "onehanded-*-block" }, "GripChange")]
    protected bool ToggleGripToOnehanded(ItemSlot slot, IPlayer? player, IInput input, IState state)
    {
        if (player == null) return false;
        Fsm.SetState(slot, (0, "twohanded"));
        OnStanceChange(slot, player, GetStance(slot), Blocking(slot));
        return true;
    }
    [InputHandler(states: new string[] { "twohanded-*-idle", "twohanded-*-block" }, "GripChange")]
    protected bool ToggleGripToTwohanded(ItemSlot slot, IPlayer? player, IInput input, IState state)
    {
        if (player == null || !player.Entity.LeftHandItemSlot.Empty) return false;
        Fsm.SetState(slot, (0, "onehanded"));
        OnStanceChange(slot, player, GetStance(slot), Blocking(slot));
        return true;
    }
    #endregion
}
