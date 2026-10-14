using System.Text.Json.Serialization;

namespace RisaBotApp.Models;

/// <summary>
/// The envelope every port-8080 API endpoint replies with.
///
/// Worth reading carefully, because it is a trap: the dashboard node answers
/// <c>200 OK</c> for failures too, carrying <c>{"ok": false, "error": "…"}</c> in the body
/// (see the do_POST handlers from dashboard.py:1172 — each one catches its own exceptions
/// and serialises them rather than letting the status code carry the news). So
/// <c>EnsureSuccessStatusCode</c> proves only that the HTTP request arrived; whether the
/// robot did the thing is <see cref="Ok"/>, and nothing else.
/// </summary>
public class DashboardApiResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("msg")]
    public string? Msg { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

/// <summary>Response from GET /api/get_param.</summary>
public class ParamResponse : DashboardApiResponse
{
    /// <summary>
    /// Untyped because ROS parameters are not: the same endpoint returns bools, doubles,
    /// ints and strings depending on which parameter was asked for. The parameters drawer
    /// decides how to render it from the declared type it already holds.
    /// </summary>
    [JsonPropertyName("value")]
    public System.Text.Json.JsonElement Value { get; set; }

    /// <summary>
    /// Present only when the parameter appears in the robot's loaded defaults, which is
    /// what lets the drawer show a "changed from default" marker. Absent, not null, for
    /// anything the YAML does not mention.
    /// </summary>
    [JsonPropertyName("default")]
    public System.Text.Json.JsonElement? Default { get; set; }
}

/// <summary>Request body for POST /api/set_param.</summary>
public class SetParamRequest
{
    [JsonPropertyName("node")]
    public string Node { get; set; } = string.Empty;

    [JsonPropertyName("param")]
    public string Param { get; set; } = string.Empty;

    /// <summary>
    /// A string regardless of the parameter's real type — the handler does
    /// <c>str(data['value'])</c> and hands it to the ROS param CLI, which parses it back.
    /// Sending a JSON number here works only because it round-trips through the same text.
    /// </summary>
    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// Response from POST /api/save_defaults.
///
/// Worth reading the count rather than just the ok flag: the handler only writes parameters
/// whose live value differs from what the YAML already holds, so <c>updated == 0</c> means the
/// file was left byte-identical. Reporting "saved" for that is misleading — nothing was
/// changed, and the operator's edit may simply never have been Set.
/// </summary>
public class SaveDefaultsResponse : DashboardApiResponse
{
    /// <summary>How many parameters were rewritten in params.yaml.</summary>
    [JsonPropertyName("updated")]
    public int Updated { get; set; }

    /// <summary>Absolute path of the file on the robot, as resolved by the dashboard node.</summary>
    [JsonPropertyName("path")]
    public string? Path { get; set; }
}

/// <summary>
/// Request body for POST /api/record_playback.
///
/// The handler accepts <c>record</c>, <c>stop</c>, <c>playback</c>, <c>save</c> and
/// <c>list</c> bare, and <c>save</c>, <c>load</c>, <c>delete</c>, <c>set_active</c> with a
/// name. Note the asymmetry: <c>save</c> is valid both ways, and every other named action
/// is rejected without a name — an empty name silently turns a load into an invalid
/// command rather than a default one.
/// </summary>
public class RecordPlaybackRequest
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

/// <summary>Request body for POST /api/calibrate_imu. Forwarded verbatim to
/// servo_controller, which is why the shape is dictated by that node and not by this app:
/// <c>{action:"zero"}</c>, then <c>{action:"set_scale", axis:"pitch", target:90.0}</c>,
/// then the same for roll.</summary>
public class ImuCalibrationRequest
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    /// <summary>"pitch" or "roll". Omitted for the zeroing step.</summary>
    [JsonPropertyName("axis")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Axis { get; set; }

    /// <summary>Degrees the robot is actually being held at, so the node can derive scale.</summary>
    [JsonPropertyName("target")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Target { get; set; }
}

/// <summary>Live state of the subsystem data logger (GET /api/logger/status).</summary>
public class LoggerStatus
{
    [JsonPropertyName("is_logging")]
    public bool IsLogging { get; set; }

    [JsonPropertyName("session_name")]
    public string SessionName { get; set; } = string.Empty;

    [JsonPropertyName("duration_sec")]
    public double DurationSec { get; set; }

    [JsonPropertyName("sample_count")]
    public int SampleCount { get; set; }

    [JsonPropertyName("log_dir")]
    public string LogDir { get; set; } = string.Empty;
}

/// <summary>Wrapper the status endpoint puts its payload inside.</summary>
public class LoggerStatusResponse : DashboardApiResponse
{
    [JsonPropertyName("data")]
    public LoggerStatus? Data { get; set; }
}

/// <summary>One completed logging session, read back off its session_meta.json.</summary>
public class LoggerSession
{
    /// <summary>Directory name — also the value to pass to the download endpoint.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>ISO start time, falling back to the directory name when the metadata file
    /// is missing or unreadable. So this is not reliably parseable as a date.</summary>
    [JsonPropertyName("time")]
    public string Time { get; set; } = string.Empty;

    [JsonPropertyName("duration")]
    public double Duration { get; set; }

    [JsonPropertyName("samples")]
    public int Samples { get; set; }

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;
}

/// <summary>Response from GET /api/logger/list. Newest first — the robot sorts the
/// directory names in reverse, and they are timestamp-prefixed.</summary>
public class LoggerListResponse : DashboardApiResponse
{
    [JsonPropertyName("sessions")]
    public List<LoggerSession> Sessions { get; set; } = new();
}

/// <summary>Response from POST /api/logger/start.</summary>
public class LoggerStartResponse : DashboardApiResponse
{
    [JsonPropertyName("session_name")]
    public string? SessionName { get; set; }
}

/// <summary>Response from GET /api/recording_data?name=… — the raw contents of a saved
/// recording's JSON file, whose shape is owned by servo_controller.</summary>
public class RecordingDataResponse : DashboardApiResponse
{
    [JsonPropertyName("data")]
    public System.Text.Json.JsonElement? Data { get; set; }
}
