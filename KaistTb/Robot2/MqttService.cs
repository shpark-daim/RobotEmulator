using Daim.Xms.Xcp;
using KaistRcp;
using MQTTnet;
using System.Text;
using System.Text.Json;
using RcpStatus = KaistRcp.RcpStatus<System.Text.Json.JsonElement?>;

namespace Robot2 {
    public class MqttService {
        private readonly string _brokerAddress;
        private readonly int _port;
        private IMqttClient _mqttClient;
        private readonly MqttClientFactory _mqttFactory = new();
        public bool IsConnected => _mqttClient?.IsConnected ?? false;
        public event EventHandler<bool>? ConnectionChanged;
        public event Action<string, RcpCommand>? CommandReceived;

        private bool _isProcessingQueue = false;
        private readonly Queue<RcpStatus> _statusQ = new();

        public MqttService(string brokerAddress, int port = 1883) {
            _brokerAddress = brokerAddress;
            _port = port;

            _mqttFactory = new MqttClientFactory();
            _mqttClient = _mqttFactory.CreateMqttClient();
            _mqttClient.ConnectedAsync += MqttClientConnected;
            _mqttClient.DisconnectedAsync += MqttClientDisConnected;
            _mqttClient.ApplicationMessageReceivedAsync += OnMessageReceived;
        }

        public async Task ConnectAsync() {
            var options = new MqttClientOptionsBuilder()
                        .WithTcpServer("localhost", 1883) // MQTT 브로커 주소
                        .WithClientId("r1" + Guid.NewGuid().ToString()[..8])
                        .WithCleanSession()
                        .Build();
            await _mqttClient.ConnectAsync(options);
        }

        public async Task DisconnectAsync() {
            await _mqttClient.UnsubscribeAsync(Rcp.MakeSubAllTargetAllCmdTopic());
            var disconnectOptions = _mqttFactory.CreateClientDisconnectOptionsBuilder().Build();
            await _mqttClient.DisconnectAsync(disconnectOptions);
        }

        public async Task QueueMessage(RcpStatus status) {
            _statusQ.Enqueue(status);

            if (!_isProcessingQueue) {
                await ProcessMessageQueue();
            }
        }

        private async Task MqttClientConnected(MqttClientConnectedEventArgs e) {
            var result = e.ConnectResult;
            if (result.ResultCode != MqttClientConnectResultCode.Success) {
                // FIXME : LOGGING
                Console.WriteLine("Mqtt fail to connect: {ResultCode}", result.ResultCode);
            } else {
                Console.WriteLine("Mqtt connected");
                var mqttSubscribeOption = _mqttFactory.CreateSubscribeOptionsBuilder()
                    .WithTopicFilter(f => {
                        f.WithTopic(Rcp.MakeSubAllTargetAllCmdTopic());
                    })
                    .Build();
                await _mqttClient.SubscribeAsync(mqttSubscribeOption);
                await _mqttClient.SubscribeAsync(mqttSubscribeOption);
            }
            ConnectionChanged?.Invoke(this, true);
        }

        private async Task MqttClientDisConnected(MqttClientDisconnectedEventArgs e) {
            Console.WriteLine("Mqtt disconneceted: {Reason}, {ReasonString}", e.Reason, e.ReasonString);
            ConnectionChanged?.Invoke(this, false);
            await Task.Delay(1000);
            await ConnectAsync();
        }

        private async Task OnMessageReceived(MqttApplicationMessageReceivedEventArgs e) {
            var message = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);
            var topic = Xcp.ParseTopic(e.ApplicationMessage.Topic);
            Console.WriteLine("MqttRecv: {Topic} {MqttMsg}", topic, message);
            var topicInfo = Xcp.ParseTopic(e.ApplicationMessage.Topic);
            RcpCommand? cmdObj = (topicInfo.Protocol, topicInfo.SubType) switch {
                (Rcp.Identifier, Rcp.CmdStatus) => new RcpStatusCommand(),
                (Rcp.Identifier, Rcp.CmdSync) => JsonSerializer.Deserialize(message, RcpContext.Default.RcpSyncCommand) ,
                (Rcp.Identifier, Rcp.CmdAuto) => new RcpAutoCommand(),
                (Rcp.Identifier, Rcp.CmdManual) => new RcpManualCommand(),
                (Rcp.Identifier, Rcp.CmdStart) => JsonSerializer.Deserialize(message, RcpContext.Default.RcpStartCommand),
                (Rcp.Identifier, Rcp.CmdStop) => JsonSerializer.Deserialize(message, RcpContext.Default.RcpStopCommand),
                (Rcp.Identifier, Rcp.CmdPause) => JsonSerializer.Deserialize(message, RcpContext.Default.RcpPauseCommand),
                (Rcp.Identifier, Rcp.CmdResume) => JsonSerializer.Deserialize(message, RcpContext.Default.RcpResumeCommand),
                (Rcp.Identifier, Rcp.CmdAbort) => JsonSerializer.Deserialize(message, RcpContext.Default.RcpAbortCommand),
                (Rcp.Identifier, Rcp.CmdEnd) => JsonSerializer.Deserialize(message, RcpContext.Default.RcpEndCommand),
                _ => throw new NotImplementedException()
            };
            if (cmdObj == null) throw new Exception("Fail to deserialize command");
            CommandReceived?.Invoke(topicInfo.Target, cmdObj);
        }

        private async Task ProcessMessageQueue() {
            _isProcessingQueue = true;
            try {
                while (_statusQ.Count > 0) {
                    var status = _statusQ.Dequeue();
                    await SendStatus(status);
                }
            } catch (Exception ex) {
                Console.WriteLine($"메시지 처리 중 오류: {ex.Message}");
            } finally {
                _isProcessingQueue = false;
            }
        }

        private async Task SendStatus(RcpStatus status, CancellationToken ct = default) {
            var topic = Rcp.MakeStatusTopic(status.Id);
            var msg = JsonSerializer.Serialize(status);
            var applicationMessage = new MqttApplicationMessageBuilder()
                    .WithTopic(topic).WithPayload(msg).Build();
            await _mqttClient.PublishAsync(applicationMessage, ct);
            Console.WriteLine("MqttSend: {Topic} {MqttMsg}", topic, msg);
        }
    }
}
