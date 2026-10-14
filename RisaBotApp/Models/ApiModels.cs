using System.Text.Json.Serialization;

namespace RisaBotApp.Models;

/// <summary>Response from GET /api/logs.</summary>
public class LogsResponse
{
    [JsonPropertyName("has_error")]
    public bool HasError { get; set; }

    [JsonPropertyName("error_count")]
    public int ErrorCount { get; set; }

    [JsonPropertyName("errors")]
    public List<string> Errors { get; set; } = [];

    [JsonPropertyName("logs")]
    public List<string> Logs { get; set; } = [];

    [JsonPropertyName("status_summary")]
    public string StatusSummary { get; set; } = string.Empty;
}

/// <summary>Response from GET /api/launch_status.</summary>
public class LaunchStatusResponse
{
    [JsonPropertyName("launch_active")]
    public bool LaunchActive { get; set; }

    [JsonPropertyName("service_state")]
    public string ServiceState { get; set; } = string.Empty;
}

/// <summary>Response from POST /api/launch_start and POST /api/launch_stop.</summary>
public class LaunchActionResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("launch_active")]
    public bool LaunchActive { get; set; }

    [JsonPropertyName("service_state")]
    public string ServiceState { get; set; } = string.Empty;

    [JsonPropertyName("output")]
    public string Output { get; set; } = string.Empty;
}

/// <summary>
/// UI-facing state of risabot.service. Distinct from the raw systemd bool because
/// the unit is Type=simple: systemd reports "active" the moment the launch process
/// forks, while the ~15 ROS nodes behind it take seconds more to come up. Starting
/// is only cleared once the dashboard node actually answers.
/// </summary>
public enum LaunchState
{
    Stopped,
    Starting,
    Running,
    Stopping,
}

/// <summary>Response from POST /api/restart_ros.</summary>
public class RestartRosResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("output")]
    public string Output { get; set; } = string.Empty;
}

/// <summary>Request body for POST /api/connect.</summary>
public class ConnectRequest
{
    [JsonPropertyName("ssid")]
    public string Ssid { get; set; } = string.Empty;

    [JsonPropertyName("password")]
    public string Password { get; set; } = string.Empty;
}

/// <summary>Response from POST /api/connect.</summary>
public class ConnectResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("ssid")]
    public string Ssid { get; set; } = string.Empty;
}

// ── BPU model management (port 8000) ─────────────────────────────────────────

/// <summary>
/// Response from GET /api/model/info — what YOLO model is on the robot's disk.
///
/// "On disk" is not the same as "running": the detector calls dnn.load() once in
/// SignageDetector.__init__, so a model uploaded while the stack is up is inert until
/// risabot.service restarts. <see cref="LaunchActive"/> is what lets the UI say which
/// situation the operator is in.
/// </summary>
public class ModelInfoResponse
{
    [JsonPropertyName("present")]
    public bool Present { get; set; }

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    /// <summary>Formatted on the robot, so the timestamp reads in the robot's timezone
    /// rather than being re-derived from an epoch against the PC's clock.</summary>
    [JsonPropertyName("mtime_iso")]
    public string? MTimeIso { get; set; }

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }

    [JsonPropertyName("launch_active")]
    public bool LaunchActive { get; set; }

    [JsonPropertyName("backup")]
    public ModelBackupInfo? Backup { get; set; }
}

/// <summary>The model displaced by the last upload, which rollback would restore.</summary>
public class ModelBackupInfo
{
    [JsonPropertyName("present")]
    public bool Present { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("mtime_iso")]
    public string? MTimeIso { get; set; }
}

/// <summary>Response from POST /api/model/upload and POST /api/model/rollback.</summary>
public class ModelUploadResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }

    /// <summary>Whether the previous model was kept as .bak — i.e. whether rollback
    /// has anything to restore after this call.</summary>
    [JsonPropertyName("backup_kept")]
    public bool BackupKept { get; set; }

    /// <summary>True until something has actually re-run dnn.load(). Set by the robot,
    /// not inferred here, so the answer accounts for the restart it may have done.</summary>
    [JsonPropertyName("restart_required")]
    public bool RestartRequired { get; set; }

    [JsonPropertyName("restarted")]
    public bool Restarted { get; set; }

    [JsonPropertyName("service_state")]
    public string? ServiceState { get; set; }

    /// <summary>Set when the file landed but could not be handed to the sunrise user —
    /// the upload succeeded and the detector still may not be able to read it.</summary>
    [JsonPropertyName("warning")]
    public string? Warning { get; set; }
}
