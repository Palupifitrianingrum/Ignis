using Ignis.Core.Entities;
using Ignis.Core.Enums;
using Ignis.Core.ValueObjects;

namespace Ignis.Core.Services;

public static class HotspotFilter
{
    public static IEnumerable<Hotspot> Apply(IEnumerable<Hotspot> source, Region? region, HotspotSource? src, HotspotStatus? status) =>
        source.Where(h => (region == null || region.Contains(h.Latitude, h.Longitude))
                       && (src == null || h.Source == src)
                       && (status == null || h.Status == status));
}
