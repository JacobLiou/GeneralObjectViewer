using System.Text.Json.Nodes;
using ObjectViewer.TestModels;

namespace ObjectViewer.Demo.Models;

public enum MachineState
{
    Offline,
    Idle,
    Running,
    Faulted,
    Maintenance,
}

public enum AxisKind
{
    Linear,
    Rotary,
}

/// <summary>Flagship realistic sample: an industrial robot station profile (PRD §16 showcase).</summary>
public sealed class RobotStation
{
    public string StationId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public MachineState State { get; set; }

    public RobotArm? PrimaryArm { get; set; }

    public List<Axis> Axes { get; set; } = [];

    public NetworkConfig? Network { get; set; }

    public SafetySettings? Safety { get; set; }

    public Dictionary<string, string> EnvironmentTags { get; set; } = [];

    public DateTime LastServiceAt { get; set; }

    public List<MaintenanceRecord> MaintenanceLog { get; set; } = [];
}

public sealed class RobotArm
{
    public string Model { get; set; } = string.Empty;

    public int ReachMm { get; set; }

    public double PayloadKg { get; set; }

    public bool Enabled { get; set; }
}

public sealed class Axis
{
    public int Index { get; set; }

    public string Name { get; set; } = string.Empty;

    public AxisKind Kind { get; set; }

    public double Position { get; set; }

    public double Home { get; set; }

    public double LowerLimit { get; set; }

    public double UpperLimit { get; set; }
}

public sealed class NetworkConfig
{
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; }

    public string Protocol { get; set; } = string.Empty;

    public int TimeoutMs { get; set; }

    public bool TlsEnabled { get; set; }
}

public sealed class SafetySettings
{
    public bool EmergencyStop { get; set; }

    public bool LightCurtain { get; set; }

    public double MaxSpeedPercent { get; set; }

    public string AccessLevel { get; set; } = string.Empty;
}

public sealed class MaintenanceRecord
{
    public DateTime Date { get; set; }

    public string Engineer { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;
}

/// <summary>Dictionary sample host: special-character keys + JSON-typed values (JsonNode).</summary>
public sealed class AppConfig
{
    public string ApplicationName { get; set; } = string.Empty;

    public Dictionary<string, JsonNode> Settings { get; set; } = [];
}

/// <summary>Shared-reference sample host: two customers sharing one Address instance.</summary>
public sealed class CustomerRegistry
{
    public string Region { get; set; } = string.Empty;

    public List<Customer> Customers { get; set; } = [];
}
