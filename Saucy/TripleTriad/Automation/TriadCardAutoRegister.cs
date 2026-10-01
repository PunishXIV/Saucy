using Dalamud.Game.ClientState.Conditions;
using ECommons;
using ECommons.GameHelpers;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Saucy.IPC;
using System;
using System.Collections.Generic;
namespace Saucy.TripleTriad;

internal static unsafe class TriadCardAutoRegister
{
    private const string ThrottleKey = "Saucy.TriadCardAutoRegister";
    private const int IntervalMs = 1500;
    private const int MaxAttemptsPerItem = 3;

    private static readonly InventoryType[] Containers =
    [
        InventoryType.Inventory1,
        InventoryType.Inventory2,
        InventoryType.Inventory3,
        InventoryType.Inventory4,
    ];

    private static readonly Dictionary<uint, int> attempts = [];

    public static void Tick()
    {
        if (!C.AutoRegisterTriadCards || !EzThrottler.Throttle(ThrottleKey, IntervalMs))
        {
            return;
        }

        if (!CanUseItemsNow())
        {
            return;
        }

        try
        {
            if (FindUnregisteredCardItem() is not { } card)
            {
                return;
            }

            attempts[card.ItemId] = attempts.GetValueOrDefault(card.ItemId) + 1;
            var cardName = TriadCardDB.Get().FindById(card.CardId)?.Name ?? $"Card #{card.CardId}";
            Svc.Log.Info($"[Saucy] Registering Triple Triad card {cardName} (item {card.ItemId}).");
            AgentInventoryContext.Instance()->UseItem(card.ItemId);
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, "Triple Triad card auto-register failed");
        }
    }

    private static bool CanUseItemsNow()
    {
        if (TriadRunSession.ModuleEnabled ||
            TriadMapNavigation.IsNavigationActive ||
            TriadUiState.IsAutomationFlowActive() ||
            TriadUiState.IsResultVisible() ||
            TriadUiState.IsMatchRegistrationVisible())
        {
            return false;
        }

        if (!Player.Available || !Player.Interactable || GenericHelpers.IsOccupied() ||
            Svc.Condition[ConditionFlag.Casting] || Svc.Condition[ConditionFlag.BetweenAreas])
        {
            return false;
        }

        return !Lifestream.IsBusyNow() && !Vnavmesh.IsMoving();
    }

    private static GameCardInfo? FindUnregisteredCardItem()
    {
        var inventory = InventoryManager.Instance();
        if (inventory == null)
        {
            return null;
        }

        var cardDb = GameCardDB.Get();
        foreach (var type in Containers)
        {
            var container = inventory->GetInventoryContainer(type);
            if (container == null || !container->IsLoaded)
            {
                continue;
            }

            for (var i = 0; i < container->Size; i++)
            {
                var slot = container->GetInventorySlot(i);
                if (slot == null || slot->ItemId == 0)
                {
                    continue;
                }

                var itemId = slot->ItemId;
                if (attempts.GetValueOrDefault(itemId) >= MaxAttemptsPerItem)
                {
                    continue;
                }

                var card = cardDb.FindByItemId(itemId);
                if (card == null || TriadMemoryReads.TryIsCardOwned(card.CardId))
                {
                    continue;
                }

                return card;
            }
        }

        return null;
    }
}
