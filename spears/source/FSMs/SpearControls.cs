using MaltiezFSM.API;
using MaltiezFSM.Framework.Simplified;
using MaltiezFSM.Inputs;
using System;
using System.Collections.Generic;
using Vintagestory.API.Common;

namespace Spears;

public abstract class SpearControls
{
    protected SpearControls(ICoreAPI api, CollectibleObject collectible, TimeSpan aimDelay)
    {
        Fsm = new FiniteStateMachineAttributesBased(api, _states, "onehanded-lower-idle");

        BaseInputProperties inputProperties = new()
        {
            /*Statuses = new IStatusModifier.StatusType[] { IStatusModifier.StatusType.OnGround },
            StatusesCheckType = IStandardInput.MultipleCheckType.All,*/
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
        SlotSelected = new(api, "deselected", collectible, ISlotInput.SlotEventType.ToSlot);
        Aim = new(api, "aim", collectible, new(EnumEntityAction.LeftMouseDown) { Delay = aimDelay }, inputProperties);


        ActionInputProperties interruptActions = new(
            /*EnumEntityAction.Sneak,
            EnumEntityAction.Sprint,
            EnumEntityAction.Jump,
            EnumEntityAction.Glide,*/
            EnumEntityAction.FloorSit
            );

        InterruptAction = new(api, "interrupt", collectible, interruptActions);

        Fsm.Init(this, collectible);
    }

    protected static bool CanAttack(IPlayer player)
    {
        EntityControls controls = player.Entity.Controls;

        /*if (controls.Sneak) return false;
        if (controls.Sprint && controls.Backward) return false;
        if (controls.FloorSitting) return false;
        if (controls.Gliding) return false;
        if (controls.Jump) return false;*/

        return true;
    }

    protected enum StanceType
    {
        OneHandedUpper,
        OneHandedLower,
        TwoHandedUpper,
        TwoHandedLower,
        BlockLower,
        BlockUpper
    };

    protected virtual void OnDeselected(ItemSlot slot, IPlayer player)
    {

    }
    protected virtual void OnSelected(ItemSlot slot, IPlayer player)
    {

    }
    protected virtual bool OnStartThrow(ItemSlot slot, IPlayer player, StanceType stanceType)
    {
        return false;
    }
    protected virtual bool OnStartAim(ItemSlot slot, IPlayer player, StanceType stanceType)
    {
        return false;
    }
    protected virtual bool OnStartAttack(ItemSlot slot, IPlayer player, StanceType stanceType)
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
    protected virtual void OnCancelAim(ItemSlot slot, IPlayer player, StanceType stanceType)
    {
    }
    protected virtual void OnCancelBlock(ItemSlot slot, IPlayer player, StanceType stanceType, bool attacking)
    {

    }
    protected virtual void OnStanceChange(ItemSlot slot, IPlayer player, StanceType newStance)
    {

    }

    protected void CancelAttack(ItemSlot slot, IPlayer player)
    {
        if (slot.Itemstack?.Item == null) return;
        Fsm.SetState(slot, (2, "idle"));
        OnCancelAttack(slot, player, GetStance(slot));
    }
    protected void CancelBlock(ItemSlot slot, IPlayer player, bool attacking)
    {
        Fsm.SetState(slot, (2, "idle"));
        OnCancelBlock(slot, player, GetStance(slot), attacking);
    }
    protected void CancelAim(ItemSlot slot, IPlayer player)
    {
        Fsm.SetState(slot, (2, "idle"));
        OnCancelAim(slot, player, GetStance(slot));
    }
    protected bool StartBlock(ItemSlot slot, IPlayer? player)
    {
        if (player == null) return false;
        if (!EnsureStance(slot, player)) return false;
        if (!CanAttack(player)) return false;
        if (OnStartBlock(slot, player, GetStance(slot), Attacking(slot)))
        {
            Fsm.SetState(slot, (2, "block"));
            return true;
        }
        return false;
    }

    protected StanceType GetStance(ItemSlot slot)
    {
        bool onehanded = Fsm.CheckState(slot, 0, "onehanded");
        bool lowerGrip = Fsm.CheckState(slot, 1, "lower");
        bool block = Fsm.CheckState(slot, 2, "block");

        return (onehanded, lowerGrip, block) switch
        {
            (true, true, false) => StanceType.OneHandedLower,
            (true, false, false) => StanceType.OneHandedUpper,
            (false, true, false) => StanceType.TwoHandedLower,
            (false, false, false) => StanceType.TwoHandedUpper,
            (true, true, true) => StanceType.BlockLower,
            (true, false, true) => StanceType.BlockUpper,
            (false, true, true) => StanceType.BlockLower,
            (false, false, true) => StanceType.BlockUpper
        };
    }
    protected bool Blocking(ItemSlot slot) => Fsm.CheckState(slot, 2, "block");
    protected bool Attacking(ItemSlot slot) => Fsm.CheckState(slot, 2, "attack");
    protected bool Aiming(ItemSlot slot) => Fsm.CheckState(slot, 2, "aim");
    protected bool Idle(ItemSlot slot) => Fsm.CheckState(slot, 2, "idle");
    protected bool EnsureStance(ItemSlot slot, IPlayer player)
    {
        bool onehanded = Fsm.CheckState(slot, 0, "onehanded");
        if (player.Entity.LeftHandItemSlot.Empty || onehanded) return true;

        Fsm.SetState(slot, (0, "onehanded"));
        OnStanceChange(slot, player, GetStance(slot));
        return false;
    }

    #region FSM
    protected IFiniteStateMachineAttributesBased Fsm;
    private static readonly List<HashSet<string>> _states = new()
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
            "block",
            "aim"
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
    protected ActionInput Aim { get; }

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

    [InputHandler(state: "*-*-*", "ItemDropped", "SlotDeselected")]
    protected bool Deselected(ItemSlot slot, IPlayer? player, IInput input, IState state)
    {
        if (player == null) return false;
        Fsm.SetState(slot, (2, "idle"));
        OnDeselected(slot, player);
        return false;
    }
    [InputHandler(state: "*-*-*", "ItemAdded", "SlotSelected")]
    protected bool Selected(ItemSlot slot, IPlayer? player, IInput input, IState state)
    {
        if (player == null) return false;
        Fsm.SetState(slot, (2, "idle"));
        EnsureStance(slot, player);
        OnSelected(slot, player);
        return false;
    }

    [InputHandler(states: new string[] { "*-*-aim" }, "LeftMouseUp")]
    protected bool StartThrow(ItemSlot slot, IPlayer? player, IInput input, IState state)
    {
        if (player == null) return false;
        if (!EnsureStance(slot, player)) return false;
        if (!CanAttack(player)) return false;
        
        OnStartThrow(slot, player, GetStance(slot));

        return false;
    }
    [InputHandler(states: new string[] { "*-*-*", "*-*-block" }, "Aim")]
    protected bool StartAim(ItemSlot slot, IPlayer? player, IInput input, IState state)
    {
        if (player == null) return false;
        if (!EnsureStance(slot, player)) return false;
        if (!CanAttack(player)) return false;

        if (OnStartAim(slot, player, GetStance(slot)))
        {
            Fsm.SetState(slot, (2, "aim"));
            return true;
        }

        return false;
    }
    [InputHandler(states: new string[] { "*-*-aim" }, "RightMouseDown", "GripChange", "StanceChange", "InterruptAction")]
    protected bool CancelAim(ItemSlot slot, IPlayer? player, IInput input, IState state)
    {
        if (player == null) return false;
        Fsm.SetState(slot, (2, "idle"));
        OnCancelAim(slot, player, GetStance(slot));
        return false;
    }

    [InputHandler(states: new string[] { "*-*-idle", "*-*-block" }, "LeftMouseDown")]
    protected bool StartAttack(ItemSlot slot, IPlayer? player, IInput input, IState state)
    {
        if (player == null) return false;
        if (!EnsureStance(slot, player)) return false;
        if (!CanAttack(player)) return false;
        if (OnStartAttack(slot, player, GetStance(slot)))
        {
            Fsm.SetState(slot, (2, "attack"));
            return true;
        }
        return false;
    }
    [InputHandler(state: "*-*-attack", "InterruptAction")]
    protected bool CancelAttack(ItemSlot slot, IPlayer? player, IInput input, IState state)
    {
        if (player == null) return false;
        Fsm.SetState(slot, (2, "idle"));
        OnCancelAttack(slot, player, GetStance(slot));
        return false;
    }

    [InputHandler(states: new string[] { "twohanded-*-idle", "twohanded-*-attack" }, "RightMouseDown")]
    protected bool StartBlock(ItemSlot slot, IPlayer? player, IInput input, IState state)
    {
        if (player == null) return false;
        if (!EnsureStance(slot, player)) return false;
        if (!CanAttack(player)) return false;
        if (OnStartBlock(slot, player, GetStance(slot), Attacking(slot)))
        {
            Fsm.SetState(slot, (2, "block"));
            return true;
        }
        return false;
    }
    [InputHandler(state: "*-*-block", "RightMouseUp", "InterruptAction")]
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
        if (!CanAttack(player)) return false;
        Fsm.SetState(slot, (1, "upper"));
        OnStanceChange(slot, player, GetStance(slot));
        return false;
    }
    [InputHandler(states: new string[] { "*-upper-idle", "*-upper-block" }, "StanceChange")]
    protected bool ToggleStanceToUpper(ItemSlot slot, IPlayer? player, IInput input, IState state)
    {
        if (player == null) return false;
        if (!CanAttack(player)) return false;
        Fsm.SetState(slot, (1, "lower"));
        OnStanceChange(slot, player, GetStance(slot));
        return false;
    }

    [InputHandler(states: new string[] { "twohanded-*-idle" }, "GripChange")]
    protected bool ToggleGripToOnehanded(ItemSlot slot, IPlayer? player, IInput input, IState state)
    {
        if (player == null) return false;
        if (!CanAttack(player)) return false;
        Fsm.SetState(slot, (0, "onehanded"));
        OnStanceChange(slot, player, GetStance(slot));
        return true;
    }
    [InputHandler(states: new string[] { "onehanded-*-idle" }, "GripChange")]
    protected bool ToggleGripToTwohanded(ItemSlot slot, IPlayer? player, IInput input, IState state)
    {
        if (player == null || !player.Entity.LeftHandItemSlot.Empty) return false;
        if (!CanAttack(player)) return false;
        Fsm.SetState(slot, (0, "twohanded"));
        OnStanceChange(slot, player, GetStance(slot));
        return true;
    }
    #endregion
}
