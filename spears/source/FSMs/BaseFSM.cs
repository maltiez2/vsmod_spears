﻿using MaltiezFSM.API;
using MaltiezFSM.Framework.Simplified;
using MaltiezFSM.Inputs;
using System.Collections.Generic;
using Vintagestory.API.Common;

namespace Spears;

public abstract class BaseControls
{
    protected BaseControls(ICoreAPI api, CollectibleObject collectible)
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
        RightMouseUp = new(api, "rmb-up", collectible, new(EnumEntityAction.RightMouseDown) { OnRelease = true });
        LeftMouseUp = new(api, "lmb-up", collectible, new(EnumEntityAction.LeftMouseDown) { OnRelease = true });
        StanceChange = new(api, "stance", collectible, Vintagestory.API.Client.GlKeys.R);
        GripChange = new(api, "grip", collectible, Vintagestory.API.Client.GlKeys.F);
        ItemDropped = new(api, "dropped", collectible, ISlotContentInput.SlotEventType.AllTaken);
        SlotDeselected = new(api, "deselected", collectible);

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

    protected virtual void OnStanceChange(ItemSlot slot, IPlayer player, StanceType newStance, bool blocking)
    {

    }

    protected void CancelAttack(ItemSlot slot, IPlayer player)
    {
        Fsm.SetState(slot, (2, "idle"));
        OnCancelAttack(slot, player, GetStance(slot));
    }

    protected void CancelBlock(ItemSlot slot, IPlayer player)
    {
        Fsm.SetState(slot, (2, "idle"));
        OnCancelBlock(slot, player, GetStance(slot));
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
    private ActionInput RightMouseDown { get; }
    [Input]
    private ActionInput LeftMouseDown { get; }
    [Input]
    private ActionInput RightMouseUp { get; }
    [Input]
    private ActionInput LeftMouseUp { get; }
    [Input]
    private KeyboardKey StanceChange { get; }
    [Input]
    private KeyboardKey GripChange { get; }

    [Input]
    private SlotContent ItemDropped { get; }
    [Input]
    private BeforeSlotChanged SlotDeselected { get; }

    [InputHandler(state: "*-*-*", "ItemDropped", "SlotDeselected")]
    private bool Deselected(ItemSlot slot, IPlayer? player, IInput input, IState state)
    {
        if (player == null) return false;
        Fsm.SetState(slot, "onehanded-lower-idle");
        OnDeselected(slot, player);
        return false;
    }

    [InputHandler(states: new string[] { "*-*-idle", "*-*-block" }, "LeftMouseDown")]
    private bool StartAttack(ItemSlot slot, IPlayer? player, IInput input, IState state)
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
    private bool CancelAttack(ItemSlot slot, IPlayer? player, IInput input, IState state)
    {
        if (player == null) return false;
        Fsm.SetState(slot, (2, "idle"));
        OnCancelAttack(slot, player, GetStance(slot));
        return false;
    }

    [InputHandler(states: new string[] { "*-*-idle", "*-*-attack" }, "RightMouseDown")]
    private bool StartBlock(ItemSlot slot, IPlayer? player, IInput input, IState state)
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
    private bool CancelBlock(ItemSlot slot, IPlayer? player, IInput input, IState state)
    {
        if (player == null) return false;
        Fsm.SetState(slot, (2, "idle"));
        OnCancelBlock(slot, player, GetStance(slot));
        return false;
    }

    [InputHandler(states: new string[] { "*-lower-idle", "*-lower-block" }, "StanceChange")]
    private bool ToggleStanceToLower(ItemSlot slot, IPlayer? player, IInput input, IState state)
    {
        if (player == null) return false;
        Fsm.SetState(slot, (1, "upper"));
        OnStanceChange(slot, player, GetStance(slot), Blocking(slot));
        return false;
    }
    [InputHandler(states: new string[] { "*-upper-idle", "*-upper-block" }, "StanceChange")]
    private bool ToggleStanceToUpper(ItemSlot slot, IPlayer? player, IInput input, IState state)
    {
        if (player == null) return false;
        Fsm.SetState(slot, (1, "lower"));
        OnStanceChange(slot, player, GetStance(slot), Blocking(slot));
        return false;
    }

    [InputHandler(states: new string[] { "onehanded-*-idle", "onehanded-*-block" }, "GripChange")]
    private bool ToggleGripToOnehanded(ItemSlot slot, IPlayer? player, IInput input, IState state)
    {
        if (player == null) return false;
        Fsm.SetState(slot, (0, "twohanded"));
        OnStanceChange(slot, player, GetStance(slot), Blocking(slot));
        return true;
    }
    [InputHandler(states: new string[] { "twohanded-*-idle", "twohanded-*-block" }, "GripChange")]
    private bool ToggleGripToTwohanded(ItemSlot slot, IPlayer? player, IInput input, IState state)
    {
        if (player == null || !player.Entity.LeftHandItemSlot.Empty) return false;
        Fsm.SetState(slot, (0, "onehanded"));
        OnStanceChange(slot, player, GetStance(slot), Blocking(slot));
        return true;
    }
    #endregion
}
