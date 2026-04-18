namespace VoxScript.Core.Home;

public enum StatusLevel { Ready, Warming, Unavailable, Off }

public sealed record StatusResult(StatusLevel Level, string Label);
