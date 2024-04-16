using MaltiezFSM.API;
using MaltiezFSM.Framework.Simplified;
using MaltiezFSM.Inputs;
using System;
using System.Collections.Generic;
using Vintagestory.API.Common;

namespace Javelins;

public abstract class JavelinControls
{
    protected JavelinControls(ICoreAPI api, CollectibleObject collectible, TimeSpan aimDelay)
    {
        Fsm = new FiniteStateMachineAttributesBased(api, _states, "onehanded-upper-idle");

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
        ItemDropped = new(api, "dropped", collectible, ISlotContentInput.SlotEventType.AllTaken);
        SlotDeselected = new(api, "deselected", collectible);
        ItemAdded = new(api, "dropped", collectible, ISlotContentInput.SlotEventType.AfterModified);
        SlotSelected = new(api, "deselected", collectible, ISlotInput.SlotEventType.ToSlot);
        Aim = new(api, "aim", collectible, new(EnumEntityAction.LeftMouseDown) { Delay = aimDelay }, inputProperties);


        ActionInputProperties interruptActions = new(
            //EnumEntityAction.Sneak,
            //EnumEntityAction.Sprint,
            //EnumEntityAction.Jump,
            //EnumEntityAction.Glide,
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
        OneHandedLower
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
    protected virtual void OnCancelAttack(ItemSlot slot, IPlayer player, StanceType stanceType)
    {

    }
    protected virtual void OnCancelAim(ItemSlot slot, IPlayer player, StanceType stanceType)
    {
    }
    protected virtual void OnStanceChange(ItemSlot slot, IPlayer player, StanceType newStance)
    {

    }

    protected void CancelAttack(ItemSlot slot, IPlayer player)
    {
        if (slot.Itemstack?.Item == null) return;
        Console.WriteLine("CancelAttack");
        Fsm.SetState(slot, (2, "idle"));
        OnCancelAttack(slot, player, GetStance(slot));
    }
    protected void CancelAim(ItemSlot slot, IPlayer player)
    {
        Fsm.SetState(slot, (2, "idle"));
        OnCancelAim(slot, player, GetStance(slot));
    }

    protected StanceType GetStance(ItemSlot slot)
    {
        bool lowerGrip = Fsm.CheckState(slot, 1, "lower");

        if (lowerGrip)
        {
            return StanceType.OneHandedLower;
        }
        else
        {
            return StanceType.OneHandedUpper;
        }
    }
    protected bool Attacking(ItemSlot slot) => Fsm.CheckState(slot, 2, "attack");
    protected bool Aiming(ItemSlot slot) => Fsm.CheckState(slot, 2, "aim");
    protected bool Idle(ItemSlot slot) => Fsm.CheckState(slot, 2, "idle");

    #region FSM
    protected IFiniteStateMachineAttributesBased Fsm;
    private static readonly List<HashSet<string>> _states = new()
    {
        new() // Grip
        {
            "onehanded"
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
        OnSelected(slot, player);
        return false;
    }

    [InputHandler(state: "*-*-aim", "LeftMouseUp")]
    protected bool StartThrow(ItemSlot slot, IPlayer? player, IInput input, IState state)
    {
        if (player == null) return false;
        if (!CanAttack(player)) return false;

        OnStartThrow(slot, player, GetStance(slot));

        return false;
    }
    [InputHandler(state: "*-*-*", "Aim")]
    protected bool StartAim(ItemSlot slot, IPlayer? player, IInput input, IState state)
    {
        if (player == null) return false;
        if (!CanAttack(player)) return false;

        if (OnStartAim(slot, player, GetStance(slot)))
        {
            Fsm.SetState(slot, (2, "aim"));
            return true;
        }

        return false;
    }
    [InputHandler(state: "*-*-aim", "RightMouseDown", "StanceChange", "InterruptAction")]
    protected bool CancelAim(ItemSlot slot, IPlayer? player, IInput input, IState state)
    {
        if (player == null) return false;
        Fsm.SetState(slot, (2, "idle"));
        OnCancelAim(slot, player, GetStance(slot));
        return false;
    }

    [InputHandler(state: "*-*-idle", "LeftMouseDown")]
    protected bool StartAttack(ItemSlot slot, IPlayer? player, IInput input, IState state)
    {
        if (player == null) return false;
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

    [InputHandler(state: "*-lower-idle", "StanceChange")]
    protected bool ToggleStanceToLower(ItemSlot slot, IPlayer? player, IInput input, IState state)
    {
        if (player == null) return false;
        if (!CanAttack(player)) return false;
        Fsm.SetState(slot, (1, "upper"));
        OnStanceChange(slot, player, GetStance(slot));
        return false;
    }
    [InputHandler(state: "*-upper-idle", "StanceChange")]
    protected bool ToggleStanceToUpper(ItemSlot slot, IPlayer? player, IInput input, IState state)
    {
        if (player == null) return false;
        if (!CanAttack(player)) return false;
        Fsm.SetState(slot, (1, "lower"));
        OnStanceChange(slot, player, GetStance(slot));
        return false;
    }
    #endregion
}
