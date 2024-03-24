using MaltiezFSM.API;
using MaltiezFSM.Framework.Simplified;
using MaltiezFSM.Inputs;
using System;
using System.Collections.Generic;
using Vintagestory.API.Common;

namespace Spears;

public abstract class PikeControls
{
    protected PikeControls(ICoreAPI api, CollectibleObject collectible)
    {
        Fsm = new FiniteStateMachineAttributesBased(api, States, "shoulder-idle");

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
        ItemAdded = new(api, "added", collectible, ISlotContentInput.SlotEventType.AfterModified);
        SlotSelected = new(api, "selected", collectible, ISlotInput.SlotEventType.ToSlot);

        ActionInputProperties interruptActions = new(
            EnumEntityAction.Sneak,
            EnumEntityAction.Sprint,
            EnumEntityAction.Jump,
            EnumEntityAction.Glide,
            EnumEntityAction.FloorSit
            );

        InterruptAction = new(api, "interrupt", collectible, interruptActions);

        Fsm.Init(this, collectible);
    }

    protected static bool CanAttack(IPlayer player)
    {
        EntityControls controls = player.Entity.Controls;

        if (controls.Sneak) return false;
        if (controls.Sprint) return false;
        if (controls.FloorSitting) return false;
        if (controls.Gliding) return false;
        if (controls.Jump) return false;

        return true;
    }

    protected enum StanceType
    {
        Upper,
        Lower,
        Shoulder
    };

    protected virtual void OnDeselected(ItemSlot slot, IPlayer player)
    {

    }
    protected virtual void OnSelected(ItemSlot slot, IPlayer player)
    {

    }
    protected virtual bool OnStartAttack(ItemSlot slot, IPlayer player, StanceType stanceType)
    {
        return false;
    }
    protected virtual void OnCancelAttack(ItemSlot slot, IPlayer player, StanceType stanceType)
    {

    }
    protected virtual void OnStanceChange(ItemSlot slot, IPlayer player, StanceType newStance)
    {

    }
    protected void CancelAttack(ItemSlot slot, IPlayer player)
    {
        Fsm.SetState(slot, (1, "idle"));
        OnCancelAttack(slot, player, GetStance(slot));
    }

    protected StanceType GetStance(ItemSlot slot)
    {
        if (Fsm.CheckState(slot, 0, "lower")) return StanceType.Lower;
        if (Fsm.CheckState(slot, 0, "upper")) return StanceType.Upper;
        return StanceType.Shoulder;
    }
    protected bool Attacking(ItemSlot slot) => Fsm.CheckState(slot, 1, "attack");
    protected bool EnsureStance(ItemSlot slot, IPlayer player)
    {
        bool shoulder = Fsm.CheckState(slot, 0, "shoulder");
        if (player.Entity.LeftHandItemSlot.Empty || shoulder) return true;

        Fsm.SetState(slot, (0, "shoulder"));
        OnStanceChange(slot, player, GetStance(slot));
        return false;
    }

    #region FSM
    protected IFiniteStateMachineAttributesBased Fsm;
    private static readonly List<HashSet<string>> States = new()
    {
        new() // Stance
        {
            "lower",
            "upper",
            "shoulder"
        },
        new() // Action
        {
            "idle",
            "attack"
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
    [Input]
    protected ActionInput InterruptAction { get; }

    [InputHandler(state: "*-*", "ItemDropped", "SlotDeselected")]
    protected bool Deselected(ItemSlot slot, IPlayer? player, IInput input, IState state)
    {
        if (player == null) return false;
        Fsm.SetState(slot, "shoulder-idle");
        OnDeselected(slot, player);
        return false;
    }
    [InputHandler(state: "*-*", "ItemAdded", "SlotSelected")]
    protected bool Selected(ItemSlot slot, IPlayer? player, IInput input, IState state)
    {
        if (player == null) return false;
        Fsm.SetState(slot, "shoulder-idle");
        OnSelected(slot, player);
        return false;
    }

    [InputHandler(states: new string[] { "lower-idle", "upper-idle" }, "LeftMouseDown")]
    protected bool StartAttack(ItemSlot slot, IPlayer? player, IInput input, IState state)
    {
        if (player == null) return false;
        if (!EnsureStance(slot, player)) return false;
        if (!CanAttack(player)) return false;
        if (OnStartAttack(slot, player, GetStance(slot)))
        {
            Fsm.SetState(slot, (1, "attack"));
            return true;
        }
        return false;
    }
    [InputHandler(state: "*-attack", "InterruptAction")]
    protected bool CancelAttack(ItemSlot slot, IPlayer? player, IInput input, IState state)
    {
        if (player == null) return false;
        Fsm.SetState(slot, (1, "idle"));
        OnCancelAttack(slot, player, GetStance(slot));
        return false;
    }

    [InputHandler(state: "lower-idle", "StanceChange")]
    protected bool ToggleStanceToLower(ItemSlot slot, IPlayer? player, IInput input, IState state)
    {
        if (player == null) return false;
        if (!CanAttack(player)) return false;
        Fsm.SetState(slot, (0, "upper"));
        OnStanceChange(slot, player, GetStance(slot));
        return false;
    }
    [InputHandler(state: "upper-idle", "StanceChange")]
    protected bool ToggleStanceToUpper(ItemSlot slot, IPlayer? player, IInput input, IState state)
    {
        if (player == null) return false;
        if (!CanAttack(player)) return false;
        Fsm.SetState(slot, (0, "lower"));
        OnStanceChange(slot, player, GetStance(slot));
        return false;
    }

    [InputHandler(states: new string[] { "lower-idle", "upper-idle" }, "GripChange", "InterruptAction")]
    protected bool PutOnShoulder(ItemSlot slot, IPlayer? player, IInput input, IState state)
    {
        if (player == null) return false;
        Fsm.SetState(slot, (0, "shoulder"));
        OnStanceChange(slot, player, GetStance(slot));
        return true;
    }
    [InputHandler(state: "shoulder-*", "GripChange")]
    protected bool TakeFromShoulder(ItemSlot slot, IPlayer? player, IInput input, IState state)
    {
        if (player == null || !player.Entity.LeftHandItemSlot.Empty) return false;
        if (!CanAttack(player)) return false;
        Fsm.SetState(slot, (0, "lower"));
        OnStanceChange(slot, player, GetStance(slot));
        return true;
    }
    #endregion
}
