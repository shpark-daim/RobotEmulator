using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using RcpStatus = KaistRcp.RcpStatus<System.Text.Json.JsonElement?>;

namespace KaistRcp;
public static class Rcp {
    public const string PrefixDefault = "xflow";
    public const string Identifier = "rcp";
    public const string Version = "v1";
    public const string BroadcastId = "*";

    public const string TypeStatus = "status";
    public const string TypeCmd = "cmd";

    public const string CmdStatus = "status";
    public const string CmdSync = "sync";
    public const string CmdAuto = "auto";
    public const string CmdManual = "manual";

    public const string CmdStart = "start";
    public const string CmdStop = "stop";
    public const string CmdPause = "pause";
    public const string CmdResume = "resume";
    public const string CmdAbort = "abort";
    public const string CmdEnd = "end";

    public static string MakeStatusTopic(string target, string prefix = PrefixDefault) =>
     $"{prefix}/{Identifier}/{Version}/{target}/{TypeStatus}";

    public static string MakeCmdTopic(string target, string cmd, string prefix = PrefixDefault) =>
        $"{prefix}/{Identifier}/{Version}/{target}/{TypeCmd}/{cmd}";

    public static string MakeSubAllTargetStatusTopic(string prefix = PrefixDefault) =>
        MakeStatusTopic("+", prefix);

    public static string MakeSubAllTargetAllCmdTopic(string prefix = PrefixDefault) =>
        MakeCmdTopic("+", "+", prefix);

    public static string MakeSubAllCmdTopic(string target, string prefix = PrefixDefault) =>
        MakeCmdTopic(target, "+", prefix);

    public static string MakePubBroadcastCmdTopic(string cmd, string prefix = PrefixDefault) =>
        MakeCmdTopic(BroadcastId, cmd, prefix);
}

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(RcpStatus))]
[JsonSerializable(typeof(RcpStatusCommand))]
[JsonSerializable(typeof(RcpSyncCommand))]
[JsonSerializable(typeof(RcpAutoCommand))]
[JsonSerializable(typeof(RcpManualCommand))]
[JsonSerializable(typeof(RcpStartCommand))]
[JsonSerializable(typeof(RcpStopCommand))]
[JsonSerializable(typeof(RcpPauseCommand))]
[JsonSerializable(typeof(RcpResumeCommand))]
[JsonSerializable(typeof(RcpAbortCommand))]
[JsonSerializable(typeof(RcpEndCommand))]
public partial class RcpContext : JsonSerializerContext {
    static RcpContext() {
        OptionsWithRelaxedEscaping = new(Default.Options) {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };
    }
    public static JsonSerializerOptions OptionsWithRelaxedEscaping { get; }
}