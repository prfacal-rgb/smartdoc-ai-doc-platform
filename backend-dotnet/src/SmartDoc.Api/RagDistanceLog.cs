namespace SmartDoc.Api;

/// <summary>
/// Dedicated logger category for raw similarity-search distances (ADR 0016/0020). Routed to
/// its own file (logs/distance-.log) via a filtered Serilog sub-logger in Program.cs, on top
/// of also appearing in the general app log — isolated so it can be mined later to calibrate
/// Rag:MaxRelevantDistance without grepping it out of everything else.
/// </summary>
public static class RagDistanceLog
{
    public const string CategoryName = "SmartDoc.Api.Rag.Distance";
}
