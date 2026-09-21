using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Windows;
using ZLongChat.Communication.Composite;
using ZLongChat.Communication.Http;
using ZLongChat.Communication.SignalR;
using ZLongChat.Communication.WebRtc;
using ZLongChat.PC.Services;

namespace ZLongChat.PC
{
    /// <summary>
    /// 前端组合根：在这里决定「用哪套通信实现」，并从配置文件读取参数。
    ///
    /// 想换通信方式（比如信令改成原生 WebSocket），只需要改这个方法的组装部分；
    /// 想改服务地址或 STUN/TURN，只需要改 appsettings.json，本文件不用动。
    /// </summary>
    public partial class App : Application
    {
        private ILoggerFactory? _loggerFactory;
        private ChatSession? _session;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            _loggerFactory = LoggerFactory.Create(builder => builder
                .AddConsole()
                .SetMinimumLevel(LogLevel.Debug));

            var logger = _loggerFactory.CreateLogger("ZLongChat.PC");

            // ---------------- 读取配置（appsettings.json → Local → 环境变量）----------------
            IConfigurationRoot configuration = AppConfiguration.Build(AppContext.BaseDirectory);
            var clientConfiguration = configuration.GetClientConfiguration(logger);

            // ---------------- 组装通信层 ----------------
            // 注意：全部组件共用同一个 clientConfiguration 实例。
            // 登录时若从服务端拉到新配置，会就地覆盖它，WebRTC 通道随即生效。
            var signaling = new SignalRSignalingService(
                clientConfiguration,
                _loggerFactory.CreateLogger<SignalRSignalingService>());

            // 通道一：P2P 直连（WebRTC DataChannel）
            var webRtc = new WebRtcMessageTransport(
                signaling,
                clientConfiguration,
                _loggerFactory.CreateLogger<WebRtcMessageTransport>());

            // 通道二：服务端转发（寄生在信令连接上）
            var relay = new SignalRRelayTransport(
                signaling,
                _loggerFactory.CreateLogger<SignalRRelayTransport>());

            // 复合策略：优先 P2P，超时/失败自动降级服务端
            var transport = new FallbackMessageTransport(
                webRtc,
                relay,
                _loggerFactory.CreateLogger<FallbackMessageTransport>());

            // REST 接口（登录、节点查询、拉取服务端配置）
            var api = new HttpChatApiClient(clientConfiguration);

            // ---------------- 组装业务层与界面 ----------------
            _session = new ChatSession(
                clientConfiguration,
                signaling,
                transport,
                webRtc,
                api,
                _loggerFactory.CreateLogger<ChatSession>());

            var window = new MainWindow(_session);
            MainWindow = window;
            window.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _loggerFactory?.Dispose();
            base.OnExit(e);
        }
    }
}
