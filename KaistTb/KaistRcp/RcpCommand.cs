using Daim.Xms.Xcp;
using System.Text.Json.Serialization;

namespace KaistRcp; 
public abstract record RcpCommand : IXcpCommand {
    public static string Identifier => Rcp.Identifier;
}

public record RcpTaskCommand([property: JsonPropertyOrder(-1)] long RefSeq)
    : RcpCommand();

public record RcpStatusCommand()
    : RcpCommand();

public record RcpSyncCommand(long Sequence)
    : RcpCommand();

public record RcpAutoCommand()
    : RcpCommand();

public record RcpManualCommand()
    : RcpCommand();

public record RcpStartCommand(string jobId, string recipeId, long RefSeq = 0)
    : RcpTaskCommand(RefSeq);

public record RcpStopCommand(long RefSeq = 0)
    : RcpTaskCommand(RefSeq);

public record RcpPauseCommand(long RefSeq = 0)
    : RcpTaskCommand(RefSeq);

public record RcpResumeCommand(long RefSeq = 0)
    : RcpTaskCommand(RefSeq);

public record RcpAbortCommand(long RefSeq = 0)
    : RcpTaskCommand(RefSeq);

public record RcpEndCommand(long RefSeq = 0)
    : RcpTaskCommand(RefSeq);

// Use in Emulator
public record RcpCompletedCommand() : RcpCommand();