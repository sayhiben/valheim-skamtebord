using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace Skamtebord;

internal static class BoardItem
{
    internal const string PrefabName = "Skamtebord_Board";
    internal const string DisplayName = "Skamtebord";
    internal static Recipe Recipe;
    private static bool registered;
    private static readonly System.Reflection.MethodInfo Discover = AccessTools.Method(typeof(Player), "AddKnownRecipe");

    internal static void Register()
    {
        if (registered) return;
        var item = new CustomItem(PrefabName, "Hammer", new ItemConfig
        {
            Name = DisplayName,
            Description = "Four wheels. One very questionable Viking idea.\nUse from your hotbar or press B to skate. Push forward, steer left/right, brake backward, jump to ollie. Land tricks to earn Skamtebord XP.",
            Amount = 1,
            CraftingStation = null,
            MinStationLevel = 1,
            Weight = 3f,
            StackSize = 1,
            Icon = BoardModel.CreateIcon(),
            Requirements = new[] { new RequirementConfig("Wood", 8), new RequirementConfig("Resin", 4), new RequirementConfig("LeatherScraps", 2) }
        });
        var shared = item.ItemDrop.m_itemData.m_shared;
        item.ItemDrop.m_itemData.m_dropPrefab = item.ItemPrefab;
        shared.m_itemType = ItemDrop.ItemData.ItemType.Tool;
        shared.m_buildPieces = null;
        shared.m_useDurability = false;
        shared.m_maxQuality = 1;
        shared.m_movementModifier = 0f;
        shared.m_teleportable = true;
        foreach (Transform child in item.ItemPrefab.transform)
            if (child.name == "attach" || child.name == "model")
            {
                child.name = "unused_hammer_" + child.name;
                child.gameObject.SetActive(false);
            }
        // An original procedural visual avoids a fragile editor-version asset bundle.
        var attach = BoardModel.Create(item.ItemPrefab.transform);
        attach.name = "attach";
        if (!ItemManager.Instance.AddItem(item)) throw new System.InvalidOperationException("Could not register Skamtebord item.");
        Recipe = item.Recipe?.Recipe;
        registered = true;
        SkamtebordPlugin.Instance.Log("Registered skateboard and hand-crafting recipe: Wood x8, Resin x4, Leather scraps x2.");
    }

    internal static bool IsBoard(ItemDrop.ItemData item) => item?.m_dropPrefab?.name == PrefabName;

    internal static void OfferRecipe(Player player)
    {
        if (Recipe && player == Player.m_localPlayer && !player.IsRecipeKnown(DisplayName)) Discover.Invoke(player, new object[] { Recipe });
    }
}
