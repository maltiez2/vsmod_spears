using OpenTK.Core.Native;
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.Common;
using Vintagestory.GameContent;

namespace Spears;

public class JavelinQuiver : ItemWearable
{
    public JavelinQuiver()
    {
        
    }
}

public sealed class AttributeRequirementInventory : InventoryBase
{
    public AttributeRequirementInventory(string className, string instanceID, ICoreAPI api, int? slotsCount = null, string? itemAttribute = null, int? maxStackSize = null) : base(className, instanceID, api)
    {
        _slotAttribute = itemAttribute ?? _defaultSlotAttribute;
        _currentSlotsCount = slotsCount ?? _defaultSlotsCount;
        _slots = GenEmptySlots(_currentSlotsCount);
        InvNetworkUtil = new PlayerInventoryNetworkUtil(this, api);
        _maxStackSize = maxStackSize ?? 1;
    }
    
    public override ItemSlot? this[int slotId]
    {
        get
        {
            if (slotId < 0 || slotId >= Count)
            {
                return null;
            }
            return _slots[slotId];
        }
        set
        {
            if (slotId < 0 || slotId >= Count)
            {
                throw new ArgumentOutOfRangeException(nameof(slotId));
            }

            _slots[slotId] = value ?? throw new ArgumentNullException(nameof(value));
        }
    }

    public override int Count => _currentSlotsCount;

    public override void FromTreeAttributes(ITreeAttribute tree)
    {
        _slotAttribute = tree.GetString("slotAttribute", _defaultSlotAttribute);
        _maxStackSize = tree.GetInt("maxStackSize", _maxStackSize);
        _slots = SlotsFromTreeAttributes(tree);
        if (_slots.Length < _defaultSlotsCount)
        {
            _slots = _slots.Append(new ItemSlotOffhand(this));
        }
        _currentSlotsCount = _slots.Length;
    }
    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        tree.SetString("slotAttribute", _slotAttribute);
        tree.SetInt("maxStackSize", _maxStackSize);
        SlotsToTreeAttributes(_slots, tree);
        ResolveBlocksOrItems();
    }

    protected override ItemSlot NewSlot(int i)
    {
        return new AttributeRequirementSlot(this, _slotAttribute, _maxStackSize);
    }

    private const string _defaultSlotAttribute = "storedInQuiver";
    private const int _defaultSlotsCount = 8;
    private string _slotAttribute;
    private int _currentSlotsCount = _defaultSlotsCount;
    private int _maxStackSize;
    private ItemSlot[] _slots;
}

public sealed class AttributeRequirementSlot : ItemSlot
{
    public override int MaxSlotStackSize => _maxStackSize;

    public AttributeRequirementSlot(InventoryBase inventory, string attribute, int maxStackSize)
        : base(inventory)
    {
        _attribute = attribute;
        _maxStackSize = maxStackSize;
    }

    public override bool CanHold(ItemSlot sourceSlot)
    {
        if (!base.CanHold(sourceSlot)) return false;

        return sourceSlot.Itemstack?.Item?.Attributes?[_attribute]?.AsBool() ?? false;
    }

    private readonly string _attribute;
    private readonly int _maxStackSize;
}

public sealed class InventoryContainers : InventoryBasePlayer
{
    private ItemSlot[] slots;

    public InventoryContainers(string className, string playerUID, ICoreAPI api) : base(className, playerUID, api)
    {
        
    }

    public override ItemSlot? this[int slotId]
    {
        get
        {
            if (slotId < 0 || slotId >= Count)
            {
                return null;
            }
            return slots[slotId];
        }
        set
        {
            if (slotId < 0 || slotId >= Count)
            {
                throw new ArgumentOutOfRangeException(nameof(slotId));
            }

            slots[slotId] = value ?? throw new ArgumentNullException(nameof(value));
        }
    }

    public override int Count => throw new System.NotImplementedException();

    public override void FromTreeAttributes(ITreeAttribute tree) => throw new System.NotImplementedException();
    public override void ToTreeAttributes(ITreeAttribute tree) => throw new System.NotImplementedException();
}

public sealed class AttachableSlot : ItemSlot
{
    public override int MaxSlotStackSize => 1;

    public AttachableSlot(InventoryBase inventory, AttachableContainers.SlotType slotType)
        : base(inventory)
    {
        _slotType = slotType;
    }

    public override bool CanHold(ItemSlot sourceSlot)
    {
        if (!base.CanHold(sourceSlot)) return false;

        string slotTypeString = sourceSlot.Itemstack?.Item?.Attributes?[_attribute]?.AsString("None") ?? "None";

        AttachableContainers.SlotType slotType = (AttachableContainers.SlotType)Enum.Parse(typeof(AttachableContainers.SlotType), slotTypeString);

        return slotType == _slotType;
    }

    private const string _attribute = "attachableSlot";
    private readonly AttachableContainers.SlotType _slotType;
}

public class AttachableContainers : InventoryBasePlayer
{
    public enum SlotType
    {
        LeftHip,
        RightHip,
        LeftLeg,
        RightLeg,
        LeftShoulder,
        RightShoulder,
        Chest,
        BeltFront,
        BeltFrontLeft,
        BeltFrontRight,
        BeltLeft,
        BeltRight,
        BeltBackLeft,
        BeltBackRight,

        SlotTypesCount,
        None
    }
    
    protected ItemSlot[] backPackSlots;

    protected List<ItemSlot> backPackContents = new List<ItemSlot>();

    private ItemSlot[] _attachableSlots;
    private readonly Dictionary<SlotType, List<ItemSlot>> _attachableSlots = new();

    public override int CountForNetworkPacket => (int)SlotType.SlotTypesCount;

    public override int Count => backPackSlots.Length + _attachableSlots.Select(entry => entry.Value.Count).Aggregate((first, second) => first + second);

    public override ItemSlot this[int slotId]
    {
        get
        {
            if (slotId < 0 || slotId >= Count)
            {
                return null;
            }

            if (slotId < backPackSlots.Length)
            {
                return backPackSlots[slotId];
            }

            return backPackContents[slotId - backPackSlots.Length];
        }
        set
        {
            if (slotId < 0 || slotId >= Count)
            {
                throw new ArgumentOutOfRangeException("slotId");
            }

            if (value == null)
            {
                throw new ArgumentNullException("value");
            }

            if (slotId < backPackSlots.Length)
            {
                backPackSlots[slotId] = value;
            }

            backPackContents[slotId - backPackSlots.Length] = value;
        }
    }

    public AttachableContainers(string className, string playerUID, ICoreAPI api)
        : base(className, playerUID, api)
    {
        backPackSlots = GenEmptySlots(4);
        baseWeight = 1f;
    }

    public AttachableContainers(string inventoryId, ICoreAPI api)
        : base(inventoryId, api)
    {
        backPackSlots = GenEmptySlots(4);
        baseWeight = 1f;
    }

    public override void AfterBlocksLoaded(IWorldAccessor world)
    {
        base.AfterBlocksLoaded(world);
        ReloadBackPackSlots();
    }

    public override void FromTreeAttributes(ITreeAttribute tree)
    {
        backPackSlots = SlotsFromTreeAttributes(tree);
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        SlotsToTreeAttributes(backPackSlots, tree);
    }

    protected override ItemSlot NewSlot(int i)
    {
        return new AttachableSlot(this, (SlotType)i);
    }

    public override float GetSuitability(ItemSlot sourceSlot, ItemSlot targetSlot, bool isMerge)
    {
        float num = (((sourceSlot.Itemstack.Collectible.GetStorageFlags(sourceSlot.Itemstack) & EnumItemStorageFlags.Backpack) <= (EnumItemStorageFlags)0) ? 1 : 2);
        if (targetSlot is ItemSlotBackpackContent && !openedByPlayerGUIds.Contains(playerUID) && !(sourceSlot is DummySlot))
        {
            num *= 0.35f;
        }

        if (targetSlot is ItemSlotBackpackContent && (targetSlot.StorageType & (targetSlot.StorageType - 1)) == 0 && (targetSlot.StorageType & sourceSlot.Itemstack.Collectible.GetStorageFlags(sourceSlot.Itemstack)) > (EnumItemStorageFlags)0)
        {
            num *= 1.2f;
        }

        float suitability = base.GetSuitability(sourceSlot, targetSlot, isMerge);
        int num2;
        if (sourceSlot.Inventory is InventoryGeneric)
        {
            ItemStack itemstack = sourceSlot.Itemstack;
            if (itemstack == null || !itemstack.Collectible.Tool.HasValue)
            {
                num2 = 1;
                goto IL_00cb;
            }
        }

        num2 = 0;
        goto IL_00cb;
    IL_00cb:
        return (suitability + (float)num2) * num + (float)((sourceSlot is ItemSlotOutput || sourceSlot is ItemSlotCraftingOutput) ? 1 : 0);
    }

    public override void OnItemSlotModified(ItemSlot slot)
    {
        if (slot is ItemSlotBackpackContent)
        {
            SaveSlotIntoBackpack((ItemSlotBackpackContent)slot);
            return;
        }

        ReloadBackPackSlots();
        if (Api.Side == EnumAppSide.Server)
        {
            (Api.World.PlayerByUid(playerUID) as IServerPlayer)?.BroadcastPlayerData();
        }
    }

    public override void PerformNotifySlot(int slotId)
    {
        ItemSlotBackpackContent itemSlotBackpackContent = this[slotId] as ItemSlotBackpackContent;
        if (itemSlotBackpackContent != null)
        {
            base.PerformNotifySlot(itemSlotBackpackContent.BackpackIndex);
        }

        base.PerformNotifySlot(slotId);
    }

    public override object ActivateSlot(int slotId, ItemSlot sourceSlot, ref ItemStackMoveOperation op)
    {
        if (slotId < backPackSlots.Length && backPackSlots[slotId].Itemstack == null)
        {
            ReloadBackPackSlots();
        }

        return base.ActivateSlot(slotId, sourceSlot, ref op);
    }

    public override void DidModifyItemSlot(ItemSlot slot, ItemStack extractedStack = null)
    {
        base.DidModifyItemSlot(slot, extractedStack);
    }

    private void SaveSlotIntoBackpack(ItemSlotBackpackContent slot)
    {
        backPackSlots[slot.BackpackIndex].Itemstack.Attributes.GetTreeAttribute("backpack").GetTreeAttribute("slots")["slot-" + slot.SlotIndex] = new ItemstackAttribute(slot.Itemstack);
    }

    private void ReloadBackPackSlots()
    {
        int defaultValue = 189;
        ItemSlotBackpackContent itemSlotBackpackContent = null;
        IPlayer player = Api.World.PlayerByUid(playerUID);
        if (Api.Side == EnumAppSide.Client)
        {
            itemSlotBackpackContent = player?.InventoryManager.CurrentHoveredSlot as ItemSlotBackpackContent;
            if (itemSlotBackpackContent?.Inventory != this)
            {
                itemSlotBackpackContent = null;
            }
        }

        backPackContents.Clear();
        for (int i = 0; i < backPackSlots.Length; i++)
        {
            ItemStack itemstack = backPackSlots[i].Itemstack;
            if (itemstack == null || itemstack.ItemAttributes == null)
            {
                continue;
            }

            itemstack.ResolveBlockOrItem(Api.World);
            int num = itemstack.ItemAttributes["backpack"]["quantitySlots"].AsInt();
            string hexBackgroundColor = itemstack.ItemAttributes["backpack"]["slotBgColor"].AsString();
            EnumItemStorageFlags storageType = (EnumItemStorageFlags)itemstack.ItemAttributes["backpack"]["storageFlags"].AsInt(defaultValue);
            if (num == 0)
            {
                continue;
            }

            ITreeAttribute treeAttribute = itemstack.Attributes.GetTreeAttribute("backpack");
            if (treeAttribute == null)
            {
                treeAttribute = new TreeAttribute();
                ITreeAttribute treeAttribute2 = new TreeAttribute();
                for (int j = 0; j < num; j++)
                {
                    ItemSlotBackpackContent itemSlotBackpackContent2 = new ItemSlotBackpackContent(this, i, j, storageType);
                    itemSlotBackpackContent2.HexBackgroundColor = hexBackgroundColor;
                    backPackContents.Add(itemSlotBackpackContent2);
                    treeAttribute2["slot-" + j] = new ItemstackAttribute(null);
                    if (player != null && itemSlotBackpackContent != null && itemSlotBackpackContent.BackpackIndex == i && itemSlotBackpackContent.SlotIndex == j)
                    {
                        player.InventoryManager.CurrentHoveredSlot = itemSlotBackpackContent2;
                    }
                }

                treeAttribute["slots"] = treeAttribute2;
                itemstack.Attributes["backpack"] = treeAttribute;
                continue;
            }

            ITreeAttribute treeAttribute3 = treeAttribute.GetTreeAttribute("slots");
            int num2 = 0;
            foreach (KeyValuePair<string, IAttribute> item in treeAttribute3)
            {
                ItemSlotBackpackContent itemSlotBackpackContent3 = new ItemSlotBackpackContent(this, i, num2, storageType);
                itemSlotBackpackContent3.HexBackgroundColor = hexBackgroundColor;
                if (item.Value?.GetValue() != null)
                {
                    ItemstackAttribute itemstackAttribute = (ItemstackAttribute)item.Value;
                    itemSlotBackpackContent3.Itemstack = itemstackAttribute.value;
                    itemSlotBackpackContent3.Itemstack.ResolveBlockOrItem(Api.World);
                }

                backPackContents.Add(itemSlotBackpackContent3);
                if (player != null && itemSlotBackpackContent != null && itemSlotBackpackContent.BackpackIndex == i && itemSlotBackpackContent.SlotIndex == num2)
                {
                    player.InventoryManager.CurrentHoveredSlot = itemSlotBackpackContent3;
                }

                num2++;
            }
        }
    }

    public override void DiscardAll()
    {
        for (int i = 0; i < backPackSlots.Length; i++)
        {
            if (backPackSlots[i].Itemstack != null)
            {
                dirtySlots.Add(i);
            }

            backPackSlots[i].Itemstack = null;
        }

        ReloadBackPackSlots();
    }

    public override void DropAll(Vec3d pos, int maxStackSize = 0)
    {
        int timer = (base.Player?.Entity?.Properties.Attributes)?["droppedItemsOnDeathTimer"].AsInt(GlobalConstants.TimeToDespawnPlayerInventoryDrops) ?? GlobalConstants.TimeToDespawnPlayerInventoryDrops;
        for (int i = 0; i < backPackSlots.Length; i++)
        {
            ItemSlot itemSlot = backPackSlots[i];
            if (itemSlot.Itemstack != null)
            {
                EnumHandling handling = EnumHandling.PassThrough;
                itemSlot.Itemstack.Collectible.OnHeldDropped(Api.World, base.Player, itemSlot, itemSlot.StackSize, ref handling);
                if (handling == EnumHandling.PassThrough)
                {
                    dirtySlots.Add(i);
                    spawnItemEntity(itemSlot.Itemstack, pos, timer);
                    itemSlot.Itemstack = null;
                }
            }
        }

        ReloadBackPackSlots();
    }
}
