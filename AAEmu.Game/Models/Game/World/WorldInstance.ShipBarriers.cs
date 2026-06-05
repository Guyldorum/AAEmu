using System.Collections.Generic;
using AAEmu.Game.Models;

namespace AAEmu.Game.Models.Game.World;

/// <summary>
/// Backing fields for ship static barriers (BAI-derived ship collision polylines).
/// Companion of <see cref="WorldInstance"/> — see WorldInstance.ShipBarrierScriptApi.cs
/// for the public GM/script surface.
/// </summary>
public partial class WorldInstance
{
    /// <summary>
    /// BAI-derived ship collision polylines (non-null when WorldConfig.GeoDataMode
    /// was on at world init).
    /// </summary>
    public ShipStaticBarrierZones ShipStaticBarriers { get; set; }

    /// <summary>Serial for BAI-derived barrier names (per instance).</summary>
    internal int ShipBarrierBaiNameSerial;

    /// <summary>World cells whose <c>areasmission</c> polygons were already ingested for ship barriers.</summary>
    internal readonly HashSet<(int CellX, int CellY)> ShipBarrierBaiIngestedCells = [];

    /// <summary>Protects <see cref="ShipStaticBarriers"/> mutations and BAI ingest bookkeeping.</summary>
    internal readonly object ShipStaticBarriersMutationLock = new();

    /// <summary>Allocates the barrier container when GeoDataMode is on. Called once during world load.</summary>
    public void InitShipStaticBarriers()
    {
        if (AppConfiguration.Instance.World.GeoDataMode)
            ShipStaticBarriers = new ShipStaticBarrierZones();
    }
}
