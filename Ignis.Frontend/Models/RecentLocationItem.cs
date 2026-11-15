using System;

namespace Ignis.Frontend.Models;

public class RecentLocationItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string PinColorHex { get; set; } = "#F97316";
}
