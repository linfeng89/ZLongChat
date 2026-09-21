using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using ZLongChat.Contracts;
using ZLongChat.PC.Services;

namespace ZLongChat.PC
{
    /// <summary>
    /// 主窗口：只负责界面。
    ///
    /// 改造前这里有 870 行，混杂着 SignalR 连接、WebRTC 协商、ICE 缓存、二进制分帧、
    /// 心跳与 ACK 逻辑；现在这些全部下沉到 ZLongChat.Communication，
    /// 本文件只做「把用户操作转成 ChatSession 调用」和「把事件显示到界面」。
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly ChatSession _session;

        public MainWindow(ChatSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));

            InitializeComponent();

            BtnLogin.Click += BtnLogin_Click;
            BtnConnect.Click += BtnConnect_Click;
            BtnSend.Click += BtnSend_Click;
            Closing += MainWindow_Closing;

            _session.Notice += OnSessionNotice;
            _session.StatusChanged += OnSessionStatusChanged;
            _session.MessageReceived += OnMessageReceived;
        }

        // ------------------------------------------------------------ 用户操作

        private async void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            var userId = TxtMyUserId.Text.Trim();
            if (string.IsNullOrEmpty(userId))
            {
                MessageBox.Show("请输入你的用户ID");
                return;
            }

            BtnLogin.IsEnabled = false;

            try
            {
                await _session.LoginAsync(userId);
                BtnConnect.IsEnabled = true;
            }
            catch (Exception ex)
            {
                BtnLogin.IsEnabled = true;
                MessageBox.Show($"登录失败：{ex.Message}");
            }
        }

        private async void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            var targetUserId = TxtTargetUserId.Text.Trim();
            if (string.IsNullOrEmpty(targetUserId))
            {
                MessageBox.Show("请输入对方用户ID");
                return;
            }

            BtnConnect.IsEnabled = false;

            try
            {
                await _session.ConnectToPeerAsync(targetUserId);
            }
            catch (Exception ex)
            {
                BtnConnect.IsEnabled = true;
                MessageBox.Show($"发起连接失败：{ex.Message}");
            }
        }

        private async void BtnSend_Click(object sender, RoutedEventArgs e)
        {
            var text = TxtMessage.Text.Trim();
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            TxtMessage.Clear();
            AddMessage($"📤 我：{text}");

            try
            {
                await _session.SendTextAsync(text);
            }
            catch (Exception ex)
            {
                AddMessage($"❌ 发送失败：{ex.Message}");
            }
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            // 关闭时释放通信资源（信令连接、PeerConnection、心跳定时器）
            _ = _session.DisposeAsync();
        }

        // ------------------------------------------------------------ 会话事件

        private void OnSessionNotice(object? sender, string message) => AddMessage(message);

        private void OnMessageReceived(object? sender, ChatMessage message)
            => AddMessage($"📥 对方：{message.GetText()}");

        private void OnSessionStatusChanged(object? sender, ChatSessionStatus status)
            => Dispatcher.InvokeAsync(() => ApplyStatus(status));

        private void ApplyStatus(ChatSessionStatus status)
        {
            switch (status)
            {
                case ChatSessionStatus.Offline:
                    TxtStatus.Text = "未连接";
                    TxtStatus.Foreground = Brushes.Gray;
                    break;

                case ChatSessionStatus.Online:
                    TxtStatus.Text = "在线";
                    TxtStatus.Foreground = Brushes.Green;
                    break;

                case ChatSessionStatus.Connecting:
                    TxtStatus.Text = "正在建立P2P...";
                    TxtStatus.Foreground = Brushes.Orange;
                    break;

                case ChatSessionStatus.P2PConnected:
                    TxtStatus.Text = "P2P已连接";
                    TxtStatus.Foreground = Brushes.Green;
                    break;

                case ChatSessionStatus.RelayOnly:
                    TxtStatus.Text = "已断开（离线模式）";
                    TxtStatus.Foreground = Brushes.Red;
                    break;

                case ChatSessionStatus.DirectConnectFailed:
                    TxtStatus.Text = "直连失败（走服务端转发）";
                    TxtStatus.Foreground = Brushes.Red;
                    break;
            }

            // P2P 和离线转发都能发消息，所以这几种状态下都放开发送按钮
            BtnSend.IsEnabled = status is ChatSessionStatus.P2PConnected
                                        or ChatSessionStatus.RelayOnly
                                        or ChatSessionStatus.DirectConnectFailed;

            // 停止自动重连后，把「发起连接」放开，方便用户修好网络后手动重试
            if (status is ChatSessionStatus.DirectConnectFailed)
            {
                BtnConnect.IsEnabled = true;
            }
        }

        // ------------------------------------------------------------ 界面辅助

        /// <summary>往消息列表追加一行（自动切回 UI 线程）</summary>
        private void AddMessage(string text)
        {
            Dispatcher.InvokeAsync(() =>
            {
                LstMessages.Items.Add($"[{DateTime.Now:HH:mm:ss}] {text}");
                LstMessages.ScrollIntoView(LstMessages.Items[LstMessages.Items.Count - 1]);
            });
        }
    }
}
