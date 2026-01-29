using KaistRcp;
using System.ComponentModel;
using System.Threading.Channels;
using RcpStatus = KaistRcp.RcpStatus<System.Text.Json.JsonElement?>;

namespace Robot2;

public class Robot : BackgroundWorker {
    public string Id { get; init; }
    public RcpMode Mode => _status.Mode;
    public Robot(string id, MqttService mqttService) {
        Id = id;
        _mqttService = mqttService;
        _ = ExecuteAsync(new CancellationToken());
        _status = new RcpStatus(
        Id,
        0,
        0,
        RcpMode.M,
        RcpWorkingState.I,
        null,
        [],
        null,
        null
        );
    }
    public event EventHandler<(string Id, RcpMode Mode)>? ModeChanged;
    public event EventHandler<(string Id, RcpWorkingState WorkingState)>? WorkingStateChanged;
    public event EventHandler<(string Id, string? CompletionReason)>? CompletionReasonChanged;
    public event EventHandler<(string Id, string? JobId)>? JobIdChanged;
    public event EventHandler<(string Id, string? RecipeId)>? RecipeChanged;
    public event EventHandler<(string Id, long Sequence)>? SequenceChanged;
    public event EventHandler<(string Id, long EventSequence)>? EventSequenceChanged;
    private RcpStatus _status = new(
        "",
        0,
        0,
        RcpMode.M,
        RcpWorkingState.I,
        null,
        [],
        null,
        null
        );

    private async Task ExecuteAsync(CancellationToken ct) {
        while (!ct.IsCancellationRequested) {
            try {
                var e = await _eventChannel.Reader.ReadAsync(ct);
                switch (e) {
                case RcpStatusCommand rcpCommand:
                    SequenceChanged?.Invoke(this, (Id, _status.Sequence));
                    await SendStatus();
                    break;
                case RcpSyncCommand rcpCommand:
                    if (rcpCommand.Sequence > _status.Sequence) {
                        _status = _status with {
                            Sequence = rcpCommand.Sequence,
                            EventSeq = rcpCommand.Sequence
                        };
                        SequenceChanged?.Invoke(this, (Id, _status.Sequence));
                        EventSequenceChanged?.Invoke(this,( Id, _status.EventSeq));
                        await SendStatus();
                    }
                    break;
                case RcpAutoCommand rcpCommand:
                    if (_status.Mode == RcpMode.A) break;
                    _status = _status with {
                        Mode = RcpMode.A,
                        EventSeq = _status.Sequence,
                    };
                    ModeChanged?.Invoke(this, (Id, _status.Mode));
                    SequenceChanged?.Invoke(this, (Id, _status.Sequence));
                    EventSequenceChanged?.Invoke(this, (Id, _status.EventSeq));
                    await SendStatus();
                    break;
                case RcpManualCommand rcpCommand:
                    if (_status.Mode == RcpMode.M) break;
                    _status = _status with {
                        Mode = RcpMode.M,
                        EventSeq = _status.Sequence,
                    };
                    ModeChanged?.Invoke(this, (Id, _status.Mode));
                    SequenceChanged?.Invoke(this, (Id, _status.Sequence));
                    EventSequenceChanged?.Invoke(this, (Id, _status.EventSeq));
                    await SendStatus();
                    break;
                case RcpStartCommand rcpCommand:
                    if (_status.Mode != RcpMode.A) break;
                    if (rcpCommand.RefSeq != _status.EventSeq) break;
                    _status = _status with {
                        EventSeq = _status.Sequence,
                        WorkingState = RcpWorkingState.R,
                        CompletionReason = null,
                        JobId = rcpCommand.jobId,
                        RecipeId = rcpCommand.recipeId,
                    };
                    WorkingStateChanged?.Invoke(this, (Id, _status.WorkingState));
                    CompletionReasonChanged?.Invoke(this, (Id, _status.CompletionReason));
                    JobIdChanged?.Invoke(this, (Id, _status.JobId));
                    RecipeChanged?.Invoke(this, (Id, _status.RecipeId));
                    SequenceChanged?.Invoke(this, (Id, _status.Sequence));
                    EventSequenceChanged?.Invoke(this, (Id, _status.EventSeq));
                    await SendStatus();
                    break;
                case RcpStopCommand rcpCommand:
                    if (_status.Mode != RcpMode.A) break;
                    if (rcpCommand.RefSeq != _status.EventSeq) break;
                    _status = _status with {
                        EventSeq = _status.Sequence,
                        WorkingState = RcpWorkingState.S,
                        CompletionReason = null,
                    };
                    WorkingStateChanged?.Invoke(this, (Id, _status.WorkingState));
                    CompletionReasonChanged?.Invoke(this, (Id, _status.CompletionReason));
                    SequenceChanged?.Invoke(this, (Id, _status.Sequence));
                    EventSequenceChanged?.Invoke(this, (Id, _status.EventSeq));
                    await SendStatus();
                    break;
                case RcpPauseCommand rcpCommand:
                    if (_status.Mode != RcpMode.A) break;
                    if (rcpCommand.RefSeq != _status.EventSeq) break;
                    _status = _status with {
                        EventSeq = _status.Sequence,
                        WorkingState = RcpWorkingState.P,
                        CompletionReason = null,
                    };
                    WorkingStateChanged?.Invoke(this, (Id, _status.WorkingState));
                    CompletionReasonChanged?.Invoke(this, (Id, _status.CompletionReason));
                    SequenceChanged?.Invoke(this, (Id, _status.Sequence));
                    EventSequenceChanged?.Invoke(this, (Id, _status.EventSeq));
                    await SendStatus();

                    await Task.Delay(1000, ct);

                    _status = _status with {
                        EventSeq = _status.Sequence,
                        WorkingState = RcpWorkingState.C,
                        CompletionReason = "Paused",
                    };
                    WorkingStateChanged?.Invoke(this, (Id, _status.WorkingState));
                    CompletionReasonChanged?.Invoke(this, (Id, _status.CompletionReason));
                    SequenceChanged?.Invoke(this, (Id, _status.Sequence));
                    EventSequenceChanged?.Invoke(this, (Id, _status.EventSeq));
                    await SendStatus();
                    break;
                case RcpResumeCommand rcpCommand:
                    if (rcpCommand.RefSeq != _status.EventSeq) break;
                    _status = _status with {
                        EventSeq = _status.Sequence,
                        WorkingState = RcpWorkingState.M,
                        CompletionReason = null,
                    };
                    WorkingStateChanged?.Invoke(this, (Id, _status.WorkingState));
                    CompletionReasonChanged?.Invoke(this, (Id, _status.CompletionReason));
                    SequenceChanged?.Invoke(this, (Id, _status.Sequence));
                    EventSequenceChanged?.Invoke(this, (Id, _status.EventSeq));
                    await SendStatus();

                    await Task.Delay(1000, ct);

                    _status = _status with {
                        EventSeq = _status.Sequence,
                        WorkingState = RcpWorkingState.R,
                        CompletionReason = null,
                    };
                    WorkingStateChanged?.Invoke(this, (Id, _status.WorkingState));
                    CompletionReasonChanged?.Invoke(this, (Id, _status.CompletionReason));
                    SequenceChanged?.Invoke(this, (Id, _status.Sequence));
                    EventSequenceChanged?.Invoke(this, (Id, _status.EventSeq));
                    await SendStatus();
                    break;
                case RcpAbortCommand rcpCommand:
                    if (rcpCommand.RefSeq != _status.EventSeq) break;
                    _status = _status with {
                        EventSeq = _status.Sequence,
                        WorkingState = RcpWorkingState.A,
                        CompletionReason = null,
                    };
                    WorkingStateChanged?.Invoke(this, (Id, _status.WorkingState));
                    CompletionReasonChanged?.Invoke(this, (Id, _status.CompletionReason));
                    SequenceChanged?.Invoke(this, (Id, _status.Sequence));
                    EventSequenceChanged?.Invoke(this, (Id, _status.EventSeq));
                    await SendStatus();

                    await Task.Delay(1000, ct);

                    _status = _status with {
                        EventSeq = _status.Sequence,
                        WorkingState = RcpWorkingState.C,
                        CompletionReason = "Aborted",
                    };
                    WorkingStateChanged?.Invoke(this, (Id, _status.WorkingState));
                    CompletionReasonChanged?.Invoke(this, (Id, _status.CompletionReason));
                    SequenceChanged?.Invoke(this, (Id, _status.Sequence));
                    EventSequenceChanged?.Invoke(this, (Id, _status.EventSeq));
                    await SendStatus();
                    break;
                case RcpEndCommand rcpCommand:
                    if (rcpCommand.RefSeq != _status.EventSeq) break;
                    if (_status.WorkingState != RcpWorkingState.C) break;
                    _status = _status with {
                        EventSeq = _status.Sequence,
                        WorkingState = RcpWorkingState.I,
                        CompletionReason = null,
                        JobId = null,
                        RecipeId = null,
                    };
                    WorkingStateChanged?.Invoke(this, (Id, _status.WorkingState));
                    CompletionReasonChanged?.Invoke(this, (Id, _status.CompletionReason));
                    JobIdChanged?.Invoke(this, (Id, _status.JobId));
                    RecipeChanged?.Invoke(this, (Id, _status.RecipeId));
                    SequenceChanged?.Invoke(this, (Id, _status.Sequence));
                    EventSequenceChanged?.Invoke(this, (Id, _status.EventSeq));
                    await SendStatus();
                    break;
                case RcpCompletedCommand rcpCommand:
                    _status = _status with {
                        EventSeq = _status.Sequence,
                        WorkingState = RcpWorkingState.C,
                        CompletionReason = GetCompletionReason(),
                    };
                    WorkingStateChanged?.Invoke(this, (Id, _status.WorkingState));
                    CompletionReasonChanged?.Invoke(this, (Id, _status.CompletionReason));
                    SequenceChanged?.Invoke(this, (Id, _status.Sequence));
                    EventSequenceChanged?.Invoke(this, (Id, _status.EventSeq));
                    await SendStatus();
                    break;
                default:
                    break;

                    string GetCompletionReason() {
                        return _status.WorkingState switch {
                            RcpWorkingState.A => "Aborted",
                            RcpWorkingState.P => "Paused",
                            RcpWorkingState.S => "Stopped",
                            _ => "Done",
                        };
                    }
                }
            } catch (Exception ex) {
                Console.WriteLine(ex.ToString());
            }
        }
    }

    private async Task SendStatus() {
        await _mqttService.QueueMessage(_status);
        _status = _status with { Sequence = _status.Sequence + 1 };
    }

    public async Task WriteChannel(RcpCommand cmd, CancellationToken ct = default) {
        await _eventChannel.Writer.WriteAsync(cmd, ct);
    }

    private readonly MqttService _mqttService;
    private readonly Channel<object> _eventChannel = Channel.CreateUnbounded<object>(new UnboundedChannelOptions {
        AllowSynchronousContinuations = false,
        SingleReader = true,
        SingleWriter = true,
    });
}
