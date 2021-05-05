using UnityEngine;
using UnityEngine.UI;
using VivoxUnity;
using TMPro;
using System;
using System.ComponentModel;
using System.Collections.Generic;

public class VivoxManager : MonoBehaviour
{

    #region UI variables

    [SerializeField] InputField inputUserName;
    [SerializeField] InputField inputChannelName;
    [SerializeField] InputField inputMessage;
    [SerializeField] Button sendMessageButton;
    [SerializeField] Button loginButton;
    [SerializeField] Button logoutButton;
    [SerializeField] Button joinButton;
    [SerializeField] Button leaveButton;
    [SerializeField] Button localMuteButton;
    [SerializeField] TMP_Dropdown userDropdown;

    public Text messagePrefab;
    public GameObject content;

    #endregion

    #region Vivox variables
    
    private readonly Uri server = new Uri("XXX"); // APIエンドポイント
    private readonly string issuer = "XXX"; //発行者
    private readonly string domain = "XXX"; //ドメイン
    public readonly string tokenkey = "XXX"; //シークレットキー
    public readonly TimeSpan timeSpan = TimeSpan.FromSeconds(90); //発行期限

    private Client client = null;
    private AccountId accountId = null;
    private ChannelId channelId = null;
    private ILoginSession loginSession = null;
    private IChannelSession channelSession = null;

    private Dictionary<string, string> attemptedDirectedMessages = new Dictionary<string, string>(); //試みたダイレクトメッセージ
    private List<string> userList = new List<string>();

    #endregion


    void Awake()
    {
        client = new Client();
        client.Uninitialize();
        client.Initialize();

        userDropdown.ClearOptions();
        userDropdown.options.Add(new TMP_Dropdown.OptionData("Everyone"));

        loginButton.onClick.AddListener(() => {
            LoginUser(inputUserName.text);
        });

        logoutButton.onClick.AddListener(Logout);

        joinButton.onClick.AddListener(() => {
            JoinChannel(inputChannelName.text, true, true, true, ChannelType.NonPositional);
        });

        leaveButton.onClick.AddListener(LeaveChannel);

        sendMessageButton.onClick.AddListener(() => {
            if (userDropdown.captionText.text == "Everyone")
            {
                SendGroupMessage(inputMessage.text);
                return;
            }
            if (userList.Contains(userDropdown.captionText.text))
            {
                SendDirectedMessage(userDropdown.captionText.text, inputMessage.text);
                return;
            }
        });

        localMuteButton.onClick.AddListener(LocalMute);
    }

    void OnApplicationQuit()
    {
        client.Uninitialize();
    }


    #region Login/Logout Methods

    private void LoginUser(string userName)
    {
        accountId = new AccountId(issuer, userName, domain, null, null);
        loginSession = client.GetLoginSession(accountId);

        BindLoginSessionHandlers(true, loginSession); //ハンドラーの登録

        loginSession.BeginLogin(server, loginSession.GetLoginToken(tokenkey, timeSpan), SubscriptionMode.Accept, null, null, null, ar =>
        {
            try
            {
                loginSession.EndLogin(ar);
            }
            catch(Exception e)
            {
                BindLoginSessionHandlers(false, loginSession); //ハンドラーの解除
                Debug.LogWarning(e.Message);
            }
        });
    }

    private void Logout()
    {
        if (loginSession == null) return;

        loginSession.Logout();
        BindLoginSessionHandlers(false, loginSession);
    }

    #endregion


    #region Join/Leave Channel Methods

    private void JoinChannel(string channelName, bool IsAudio, bool IsText, bool switchtransmission, ChannelType channelType)
    {
        channelId = new ChannelId(issuer, channelName, domain, channelType);
        channelSession = loginSession.GetChannelSession(channelId);

        BindChannelSessionHandlers(true, channelSession);

        channelSession.BeginConnect(IsAudio, IsText, switchtransmission, channelSession.GetConnectToken(tokenkey, timeSpan), ar =>
        {
            try
            {
                channelSession.EndConnect(ar);
            }
            catch (Exception e)
            {
                BindChannelSessionHandlers(false, channelSession);
                Debug.LogWarning(e.Message);
            }
        });
    }

    private void LeaveChannel()
    {
        if (channelSession == null) return;

        channelSession.Disconnect();
        loginSession.DeleteChannelSession(channelId);

        BindChannelSessionHandlers(false, channelSession);
    }

    #endregion


    #region Send/Receive Message Methods

    // チャンネルにいる全員にメッセージを送る
    private void SendGroupMessage(string message)
    {
        if (channelSession == null) return;

        channelSession.BeginSendText(message, ar =>
        {
            try
            {
                channelSession.EndSendText(ar);
            }
            catch (Exception e)
            {
                Debug.LogWarning(e.Message);
            }
        });
    }

    // 特定のユーザにメッセージを送る
    private void SendDirectedMessage(string username, string message)
    {
        if (loginSession == null) return;

        var targetId = new AccountId(issuer, username, domain);

        loginSession.BeginSendDirectedMessage(targetId, message, ar =>
        {
            try
            {
                loginSession.EndSendDirectedMessage(ar);
            }
            catch (Exception e)
            {
                Debug.LogWarning(e.Message);
                return;
            }
            
            attemptedDirectedMessages.Add(loginSession.DirectedMessageResult.RequestId, message);
            Debug.Log("To " + targetId.Name + ": " + message);
        });
    }

    #endregion


    #region Mute Methods

    private void LocalMute()
    {
        if (client == null) return;

        if (client.AudioInputDevices.Muted)
        {
            client.AudioInputDevices.Muted = false;
            localMuteButton.GetComponentInChildren<TMP_Text>().text = "Mute";
            Debug.Log("Unmuted");
        }
        else
        {
            client.AudioInputDevices.Muted = true;
            localMuteButton.GetComponentInChildren<TMP_Text>().text = "Unmute";
            Debug.Log("Muted");
        }
    }

    #endregion


    #region Bind Methods

    // ハンドラを登録/解除するメソッド

    private void BindLoginSessionHandlers(bool doBind, ILoginSession loginSession)
    {
        if(doBind)
        {
            loginSession.PropertyChanged += OnLoginSessionPropertyChanged;
            loginSession.DirectedMessages.AfterItemAdded += OnDirectedMessageReceived;
            loginSession.FailedDirectedMessages.AfterItemAdded += OnFailedDirectedMessageReceived;
        }
        else
        {
            loginSession.PropertyChanged -= OnLoginSessionPropertyChanged;
            loginSession.DirectedMessages.AfterItemAdded -= OnDirectedMessageReceived;
            loginSession.FailedDirectedMessages.AfterItemAdded -= OnFailedDirectedMessageReceived;
        }
    }

    private void BindChannelSessionHandlers(bool doBind, IChannelSession channelSession)
    {
        if (doBind)
        {
            channelSession.PropertyChanged += SourceOnChannelPropertyChanged;

            channelSession.Participants.AfterKeyAdded += OnParticipantAdded;
            channelSession.Participants.BeforeKeyRemoved += OnParticipantRemoved;
            channelSession.Participants.AfterValueUpdated += OnParticipantValueUpdated;

            channelSession.MessageLog.AfterItemAdded += OnChannelMessageReceived;
        }
        else
        {
            channelSession.PropertyChanged -= SourceOnChannelPropertyChanged;

            channelSession.Participants.AfterKeyAdded -= OnParticipantAdded;
            channelSession.Participants.BeforeKeyRemoved -= OnParticipantRemoved;
            channelSession.Participants.AfterValueUpdated -= OnParticipantValueUpdated;

            channelSession.MessageLog.AfterItemAdded -= OnChannelMessageReceived;
        }
    }

    #endregion


    #region Events Handler

    // ログインステータス
    private void OnLoginSessionPropertyChanged(object sender, PropertyChangedEventArgs propertyChangedEventArgs)
    {
        if (propertyChangedEventArgs.PropertyName == "State")
        {
            switch ((sender as ILoginSession).State)
            {
                case LoginState.LoggingIn:
                    Debug.Log("Logging In");
                    break;
                case LoginState.LoggedIn:
                    Debug.Log($"Logged In {loginSession.LoginSessionId.Name}");
                    break;
                case LoginState.LoggingOut:
                    Debug.Log("Logging Out");
                    break;
                case LoginState.LoggedOut:
                    Debug.Log($"Logged Out {loginSession.LoginSessionId.Name}");
                    break;
            }
        }
    }

    // チャンネルステータス
    private void SourceOnChannelPropertyChanged(object sender, PropertyChangedEventArgs propertyChangedEventArgs)
    {
        if (propertyChangedEventArgs.PropertyName == "TextState")
        {
            switch ((sender as IChannelSession).TextState)
            {
                case ConnectionState.Connecting:
                    Debug.Log("Text Channel Connecting");
                    break;
                case ConnectionState.Connected:
                    Debug.Log("Text Channel Connected");
                    break;
                case ConnectionState.Disconnecting:
                    Debug.Log("Text Channel Disconnecting");
                    break;
                case ConnectionState.Disconnected:
                    Debug.Log("Text Channel Disconnected");
                    break;
            }
            return;
        }

        if (propertyChangedEventArgs.PropertyName == "AudioState")
        {
            switch ((sender as IChannelSession).AudioState)
            {
                case ConnectionState.Connecting:
                    Debug.Log($"Audio Channel Connecting");
                    break;
                case ConnectionState.Connected:
                    Debug.Log($"Audio Channel Connected");
                    break;
                case ConnectionState.Disconnecting:
                    Debug.Log($"Audio Channel Disconnecting");
                    break;
                case ConnectionState.Disconnected:
                    Debug.Log($"Audio Channel Disconnected");
                    break;
            }
            return;
        }

        if (propertyChangedEventArgs.PropertyName == "ChannelState")
        {
            switch ((sender as IChannelSession).ChannelState)
            {
                case ConnectionState.Connecting:
                    Debug.Log("Channel Connecting");
                    break;
                case ConnectionState.Connected:
                    Debug.Log($"{(sender as IChannelSession).Channel.Name} Connected");
                    break;
                case ConnectionState.Disconnecting:
                    Debug.Log($"{(sender as IChannelSession).Channel.Name} Disconnecting");
                    break;
                case ConnectionState.Disconnected:
                    Debug.Log($"{(sender as IChannelSession).Channel.Name} Disconnected");
                    break;
            }
            return;
        }
    }

    // チャンネルメッセージを受け取った時
    private void OnChannelMessageReceived(object sender, QueueItemAddedEventArgs<IChannelTextMessage> queueItemAddedEventArgs)
    {
        var channelName = queueItemAddedEventArgs.Value.ChannelSession.Channel.Name;
        var senderName = queueItemAddedEventArgs.Value.Sender.Name;
        var message = queueItemAddedEventArgs.Value.Message;

        Debug.Log($"From {senderName} : {message}");

        var temp = Instantiate(messagePrefab, content.transform);
        temp.text = $"<color=#ff0000>{senderName}</color>\n{message}";
    }

    // ダイレクトメッセージを受け取った時
    private void OnDirectedMessageReceived(object sender, QueueItemAddedEventArgs<IDirectedTextMessage> queueItemAddedEventArgs)
    {
        var directedMessages = (IReadOnlyQueue<IDirectedTextMessage>)sender;
        while (directedMessages.Count > 0)
        {
            var message = directedMessages.Dequeue();
            var temp = Instantiate(messagePrefab, content.transform);
            temp.text = $"<color=#ff00cb>{message.Sender.Name}</color>\n{message.Message}";
        }
    }

    // ダイレクトメッセージが送れなかった時(例:相手がオフライン)
    private void OnFailedDirectedMessageReceived(object sender, QueueItemAddedEventArgs<IFailedDirectedTextMessage> failedMessage)
    {
        if (attemptedDirectedMessages.ContainsKey(failedMessage.Value.RequestId))
        {
            Debug.Log("Message Failed to Send: " + attemptedDirectedMessages[failedMessage.Value.RequestId]);
        }
    }

    // チャンネルにメンバーが追加された時
    private void OnParticipantAdded(object sender, KeyEventArg<string> keyEventArg)
    {
        ValidateArgs(new object[] { sender, keyEventArg });

        var source = (VivoxUnity.IReadOnlyDictionary<string, IParticipant>)sender;
        var participant = source[keyEventArg.Key];
        var username = participant.Account.Name;
        var channel = participant.ParentChannelSession.Key;
        var channelSession = participant.ParentChannelSession;

        Debug.Log($"{username} has joined the channel");

        var temp = Instantiate(messagePrefab, content.transform);
        temp.text = $"<color=#0000ff>{username}</color> has joined channel";

        if (username == inputUserName.text) return;
        userDropdown.options.Add(new TMP_Dropdown.OptionData(username));
        userList.Add(username);
    }

    // チャンネルからメンバーが抜けた時
    private void OnParticipantRemoved(object sender, KeyEventArg<string> keyEventArg)
    {
        ValidateArgs(new object[] { sender, keyEventArg });

        var source = (VivoxUnity.IReadOnlyDictionary<string, IParticipant>)sender;
        var participant = source[keyEventArg.Key];
        var username = participant.Account.Name;
        var channel = participant.ParentChannelSession.Key;
        var channelSession = participant.ParentChannelSession;

        Debug.Log($"{username} has left channel");

        var temp = Instantiate(messagePrefab, content.transform);
        temp.text = $"<color=#0000ff>{username}</color> has left channel";

        if (username == inputUserName.text) return;

        var option = userDropdown.options.Find(option => string.Equals(option.text, username));
        userDropdown.options.Remove(option);
        userList.Remove(username);
    }

    // メンバーのステータスを確認(例:メンバーがミュートしている)
    private void OnParticipantValueUpdated(object sender, ValueEventArg<string, IParticipant> valueEventArg)
    {
        ValidateArgs(new object[] { sender, valueEventArg });

        var source = (VivoxUnity.IReadOnlyDictionary<string, IParticipant>)sender;
        var participant = source[valueEventArg.Key];
        var username = valueEventArg.Value.Account.Name;
        var channel = valueEventArg.Value.ParentChannelSession.Key;
        var property = valueEventArg.PropertyName;

        //Debug.Log($"{username} : {property}");
    }

    private static void ValidateArgs(object[] objs)
    {
        foreach (var obj in objs)
        {
            if (obj == null)
                throw new ArgumentNullException(obj.GetType().ToString(), "Specify a non-null/non-empty argument.");
        }
    }

    #endregion

}
