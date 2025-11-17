using System.Collections.Generic;
using Tibia.Protobuf.Appearances;

namespace Assets_Editor;

/// <summary>
/// Provides helper methods to keep Market and Cyclopedia flag identifiers aligned with the owning appearance id.
/// </summary>
public static class AppearanceSyncHelper
{
    public static void SyncMarketAndCyclopedia(Appearance appearance)
    {
        if (appearance == null)
        {
            return;
        }

        if (appearance.Flags == null)
        {
            return;
        }
        uint id = appearance.Id;

        if (appearance.Flags.Market != null)
        {
            appearance.Flags.Market.TradeAsObjectId = id;
            appearance.Flags.Market.ShowAsObjectId = id;
        }

        if (appearance.Flags.Cyclopediaitem != null)
        {
            appearance.Flags.Cyclopediaitem.CyclopediaType = id;
        }
    }

    public static void SyncMarketAndCyclopedia(IEnumerable<Appearance> appearances)
    {
        if (appearances == null)
        {
            return;
        }

        foreach (var appearance in appearances)
        {
            SyncMarketAndCyclopedia(appearance);
        }
    }

    public static void SyncMarketAndCyclopedia(Appearances appearances)
    {
        if (appearances == null)
        {
            return;
        }

        SyncMarketAndCyclopedia(appearances.Object);
        SyncMarketAndCyclopedia(appearances.Outfit);
        SyncMarketAndCyclopedia(appearances.Effect);
        SyncMarketAndCyclopedia(appearances.Missile);
    }
}
