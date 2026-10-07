using BOTWM.Server.DTO;
using BOTWM.Server.HelperTypes;
using BOTWM.Server.ServerClasses;
using Newtonsoft.Json;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using BOTWM.Server.JSONBuilder;
using System.Diagnostics;
using BOTW.Logging;

namespace BOTWM.Server
{
    public class Server
    {
        bool serverOpen = false;

        public string Version = "0.20.0";

        public short SerializationRate = 60;
        public short TargetFPS = 60;
        public short SleepMultiplier = 1;
        public bool isLocalTest = false;
        public bool ischaracterSpawn = true;
        public bool DisplayNames = true;
        public short GlyphDistance = 250;
        public short GlyphTime = 60;
        public bool isQuestSync = false;
        public bool isEnemySync = false;
        public string Gamemode = "";

        public bool EnemyLog { get; set; }

        public int ClientLog { get; set; }
        public bool ServerLog { get; set; }

        Socket listen;
        Thread listenThread;
        List<Thread> clientThreads = new List<Thread>();

        public bool serverStart(string ip, int port, string password, string description, ServerSettings settings)
        {

            this.Gamemode = settings.SettingsName;

            Dictionary<string, string> ipAddresses = new Dictionary<string, string>();

            if (ip == "localhost")
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());

                foreach (NetworkInterface item in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (item.OperationalStatus == OperationalStatus.Up)
                    {
                        foreach (UnicastIPAddressInformation uip in item.GetIPProperties().UnicastAddresses)
                        {
                            if (uip.Address.AddressFamily == AddressFamily.InterNetwork && host.AddressList.Contains(uip.Address))
                            {
                                ipAddresses.Add(item.Name.ToString(), uip.Address.ToString());
                            }
                        }
                    }
                }
            }
            else
            {
                ipAddresses.Add("custom IP", ip);
            }

            foreach (var key in ipAddresses.Keys)
            {
                try
                {
                    listen = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);
                    listen.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);
                    string IP = ipAddresses[key].ToString();

                    IPEndPoint connect = new IPEndPoint(IPAddress.Parse(IP), port);
                    listen.Bind(connect);

                    Logger.LogInformation("Server opened on " + key + ".");

                    ServerData.Startup(ip, port, password, description, settings);

                    serverOpen = true;

                    return true;
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex.ToString());
                    continue;
                }
            }

            return false;
        }

        public void stopServer()
        {
            serverOpen = false;
            listen.Close();

            if (listenThread != null)
                listenThread.Abort();

            foreach (Thread clientThread in clientThreads)
                clientThread.Abort();
        }

        public void startListen()
        {
            listenThread = new Thread(serverListen);
            listenThread.IsBackground = true;
            listenThread.Start();
        }

        public void serverListen()
        {
            while(true)
            {
                Socket connection;

                listen.Listen(100);

                connection = listen.Accept();

                var clientThread = new Thread(() => handleClient(connection));
                clientThread.Start();
                clientThreads.Add(clientThread);
            }
        }

        public void handleClient(Socket connection)
        {
            int SIZE = 10240;

            byte[] info = new byte[SIZE];
            List<byte> data = new List<byte>();
            int totalLength = 0;
            int retries = 0;
            int zeroReads = 0;
            Stopwatch RetryWatch = new Stopwatch();

            bool ClientConnected = true;
            int PlayerNumber = -1;
            string PlayerName = "";
            
            while(serverOpen && ClientConnected)
            {
                try
                {
                    if (retries == 0)
                        RetryWatch.Restart();

                    // patched: append only the bytes actually received and cut exactly one message.
                    // Clients send fixed-size messages: ping (type 1) = 6144 bytes, everything else = 7168.
                    // The original appended the whole 10240-byte buffer, so a message split by TCP
                    // (high ping / relayed Hamachi) was corrupted ("Error receiving message", bad data).
                    int received = data.Count >= 6144 && data.Count >= (data[0] == 1 ? 6144 : 7168) ? 0 : connection.Receive(info, 0, info.Length, 0);
                    if (received > 0)
                    {
                        data.AddRange(new ArraySegment<byte>(info, 0, received));
                        zeroReads = 0;
                    }
                    totalLength = data.Count;
                    int expected = totalLength > 0 && data[0] == 1 ? 6144 : 7168;

                    if (totalLength < expected)
                    {
                        retries++;

                        if (received == 0 && ++zeroReads > 10)
                            throw new ApplicationException("Connection lost with player.");

                        continue;
                    }

                    byte[] message = new byte[Math.Max(SIZE, expected)];
                    data.CopyTo(0, message, 0, expected);
                    data.RemoveRange(0, expected);
                    totalLength = data.Count;

                    Tuple<MessageType, object> ClientMessage = new JSONBuilder.JSONBuilder().BuildFromBytes(message);

                    if(retries > 0)
                    {
                        Logger.LogInformation($"[{PlayerName}] Retried {retries} times and took {RetryWatch.ElapsedMilliseconds} milliseconds");
                        retries = 0;
                    }

                    if (ClientMessage.Item1 == MessageType.error)
                    {
                        throw new Exception($"[{PlayerName}] Error receiving message. Disconnecting player...");
                    }
                    else if(ClientMessage.Item1 == MessageType.ping)
                    {
                        var PingResult = new PingDTO();

                        if(ServerData.Configuration.PASSWORD != (string)ClientMessage.Item2)
                            PingResult = new PingDTO()
                            {
                                CorrectPassword = false,
                                Description = "",
                                PlayerList = new NamesDTO(),
                                GameMode = "",
                                PlayerLimit = 32
                            };
                        else
                            PingResult = new PingDTO()
                            {
                                CorrectPassword = true,
                                Description = ServerData.Configuration.DESCRIPTION,
                                PlayerList = ServerData.GetPlayers(),
                                GameMode = this.Gamemode,
                                PlayerLimit = 32
                            };

                        connection.Send(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(PingResult)));

                        connection.Close();
                        ClientConnected = false;

                    }
                    else if(ClientMessage.Item1 == MessageType.connect)
                    {
                        ConnectDTO UserConfiguration = (ConnectDTO)ClientMessage.Item2;
                        ConnectResponseDTO AssignationResult = ServerData.TryAssigning(UserConfiguration);

                        if(AssignationResult.Response != 1)
                        {
                            connection.Send(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(AssignationResult)));
                            connection.Close();
                            ClientConnected = false;
                            Logger.LogInformation($"Player {UserConfiguration.Name} tried to connect but failed with error {AssignationResult.Response}");
                            break;
                        }

                        PlayerNumber = AssignationResult.PlayerNumber;
                        PlayerName = ServerData.PlayerList[PlayerNumber].Name;
                        connection.Send(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(AssignationResult)));

                        Logger.LogInformation($"Player {UserConfiguration.Name} joined the server. Assigned to player {AssignationResult.PlayerNumber + 1}.");
                    }
                    else if(ClientMessage.Item1 == MessageType.update)
                    {
                        ServerData.SetConnection(PlayerNumber, true);

                        ClientDTO UserInformation = (ClientDTO)ClientMessage.Item2;

                        ServerData.UpdateWorldData(UserInformation.WorldData, PlayerNumber);
                        ServerData.UpdatePlayerData(UserInformation.PlayerData, PlayerNumber);
                        ServerData.UpdateEnemyData(UserInformation.EnemyData);
                        ServerData.UpdateQuestData(UserInformation.QuestData);

                        ServerDTO serverDTO = ServerData.GetData(PlayerNumber);
                        serverDTO.NetworkData.Map(this);

                        connection.Send(new JSONBuilder.JSONBuilder().BuildArrayOfBytes(serverDTO));

                        ServerData.ClearDeathSwap(PlayerNumber);
                    }
                    else if(ClientMessage.Item1 == MessageType.disconnect)
                    {
                        Logger.LogInformation($"Player {ServerData.GetPlayer(PlayerNumber).Name} disconnected. {(string)ClientMessage.Item2}");
                        connection.Close();
                        ClientConnected = false;
                        ServerData.SetConnection(PlayerNumber, false);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogInformation($"Player {ServerData.GetPlayer(PlayerNumber).Name} disconnected.", ex.Message);
                    Logger.LogDebug(ex.StackTrace);
                    ServerData.SetConnection(PlayerNumber, false);
                    connection.Close();
                    ClientConnected = false;
                }
            }
        }
    }
}
