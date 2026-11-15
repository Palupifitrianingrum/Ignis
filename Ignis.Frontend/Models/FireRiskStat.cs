using System;

namespace Ignis.Frontend.Models;

public class FireRiskStat
{
    public string Level { get; set; } = string.Empty;
    public string CountText { get; set; } = string.Empty;
    public string BackgroundHex { get; set; } = "#F1F5F9";
    public string DotColorHex { get; set; } = "#64748B";
    public string TextColorHex { get; set; } = "#0F172A";
}
