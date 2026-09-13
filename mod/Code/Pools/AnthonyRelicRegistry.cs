using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Models;

namespace AutoAnthonyRelics.Pools;

/// <summary>
/// Slot-marker type registry: every concrete AnthonyRelicModel subclass in
/// this assembly, ordered by name (QuriousCraftingRelics ChaosRelicRegistry
/// pattern). Used by AnthonyRelicLocUpdater to rewrite the "relics" loc-table
/// entries per run seed, so in-game tooltips show the run's generated effects
/// instead of the generic boot-time text.
/// </summary>
public static class AnthonyRelicRegistry
{
    public const int Count = 60;

    public static IReadOnlyList<Type> Types { get; } = (
        from type in typeof(AnthonyRelicRegistry).Assembly.GetTypes()
        where !type.IsAbstract && type.BaseType == typeof(Models.AnthonyRelicModel)
        orderby type.Name
        select type).ToArray();

    /// <summary>
    /// Canonical ModelDb instances of all slots (resolved lazily after
    /// ModelDb.Init), name-ordered to match Types.
    /// </summary>
    private static IReadOnlyList<RelicModel>? _slotModels;

    public static IReadOnlyList<RelicModel> SlotModels => _slotModels ??= Types
        .Select(t => ModelDb.GetById<RelicModel>(ModelDb.GetId(t)))
        .ToArray();
}
