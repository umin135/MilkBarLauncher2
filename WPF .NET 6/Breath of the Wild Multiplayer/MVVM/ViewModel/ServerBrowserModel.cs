using Breath_of_the_Wild_Multiplayer.Source_files;
using Breath_of_the_Wild_Multiplayer.MVVM.Model;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using System.Diagnostics;
using Breath_of_the_Wild_Multiplayer.MVVM.Model.DTO;
using System.Linq;
using System.Threading.Tasks;
using System.Text;
using System.Xml;

namespace Breath_of_the_Wild_Multiplayer.MVVM.ViewModel
{
    public class ServerBrowserModel : ObservableObject
    {
        private ObservableCollection<serverDataModel> _serversToShow = new ObservableCollection<serverDataModel>();

        public ObservableCollection<serverDataModel> serversToShow
        {
            get { return _serversToShow; }
            set
            {
                _serversToShow = value;
                OnPropertyChanged();
            }
        }

        private bool _isMaxOffset;

        public bool isMaxOffset
        {
            get { return _isMaxOffset; }
            set
            {
                _isMaxOffset = value;
                OnPropertyChanged();
            }
        }

        private int _scrollState;

        public int scrollState
        {
            get { return _scrollState; }
            set { 
                _scrollState = value;
                OnPropertyChanged();
            }
        }

        private ObservableCollection<serverDataModel> _selectedServer;

        public ObservableCollection<serverDataModel> selectedServer
        {
            get { return _selectedServer; }
            set { 
                _selectedServer = value;
                OnPropertyChanged();
            }
        }

        public bool IsModInstalled
        {
            get
            {
                string modActorsPack = $@"{BcmlSettings.MergedDir}\Actor\Pack";

                // If the directory for the directory doesn't even exist. That is a clear indicator that the mod is not installed
                if (!Directory.Exists(modActorsPack))
                    return false;
                // One of the unique assets created by the mod are the actors called JugadorXX.sbactorpack.
                // If we don't have any Jugador, that means the mod is not installed.
                return Directory.GetFiles(modActorsPack).Any(actor => actor.Contains("Jugador") && Path.GetExtension(actor) == ".sbactorpack");
            }
        }

        private Dictionary<int, LocalServerDTO> ServerMapping;

        public List<LocalServerDTO> ServerList;

        public RelayCommand serverButtonClick { get; set; }
        public AsyncRelayCommand connectClick { get; set; }
        public RelayCommand refreshClick { get; set; }
        public RelayCommand addServerClick { get; set; }
        public RelayCommand directConnectClick { get; set; }

        public int lastSelected = -1;

        public ServerBrowserModel()
        {
            SharedData.ServerBrowser = this;

            //findCemuData();
            isMaxOffset = true;
            scrollState = 0;

            LoadServers();

            serverButtonClick = new RelayCommand(o => this.changeSelected(o));
            connectClick = new AsyncRelayCommand(o => this.connectToServer(null));
            refreshClick = new RelayCommand(o =>
            {
                foreach (var server in this._serversToShow)
                    _ = Task.Run(() => server.pingServer());
            });

            addServerClick = new RelayCommand(o =>
            {
                SharedData.MainView.updateTopView(new ServerEditorModel());
            });

            directConnectClick = new RelayCommand(o =>
            {
                var DirectConnectEditor = new ServerEditorModel();
                DirectConnectEditor.Setup(-2);
                SharedData.MainView.updateTopView(DirectConnectEditor);
            });
        }

        void changeSelected(object newSelection)
        {
            if(lastSelected != -1)
            {
                _serversToShow[lastSelected].selected = false;
            }

            _serversToShow[(int)newSelection].selected = true;
            _selectedServer[0] = _serversToShow[(int)newSelection];
            lastSelected = (int)newSelection;
        }

        void LoadServers()
        {
            serverDataModel.serversAdded = 0;
            serversToShow.Clear();

            this.ServerList = JsonConvert.DeserializeObject<List<LocalServerDTO>>(Properties.Settings.Default.serversAdded)!;

            ServerMapping = new Dictionary<int, LocalServerDTO>();

            foreach (var server in this.ServerList.Where(sv => sv.Favorite))
            {
                serversToShow.Add(new serverDataModel(!string.IsNullOrEmpty(BcmlSettings.GameDir) && !string.IsNullOrEmpty(BcmlSettings.CemuDir), name: server.Name, ip: server.IP, port: server.Port, favorite: server.Favorite, password: server.Password));
                ServerMapping[serverDataModel.serversAdded - 1] = server;
            }

            foreach (var server in this.ServerList.Where(sv => !sv.Favorite))
            {
                serversToShow.Add(new serverDataModel(!string.IsNullOrEmpty(BcmlSettings.GameDir) && !string.IsNullOrEmpty(BcmlSettings.CemuDir), name: server.Name, ip: server.IP, port: server.Port, favorite: server.Favorite, password: server.Password));
                ServerMapping[serverDataModel.serversAdded - 1] = server;
            }

            selectedServer = new ObservableCollection<serverDataModel>();

            if (_serversToShow.Count == 0)
            {
                selectedServer.Add(new serverDataModel(!string.IsNullOrEmpty(BcmlSettings.GameDir) && !string.IsNullOrEmpty(BcmlSettings.CemuDir), name: "", ip: "", port: 0, favorite: false, password: ""));
            }
            else
            {
                selectedServer.Add(_serversToShow[0]);
                serversToShow[0].selected = true;
                lastSelected = 0;
            }
        }

        //void findCemuData()
        //{
        //    try
        //    {
        //        string CemuSettings = File.ReadAllText(Properties.Settings.Default.bcmlLocation);
        //        Dictionary<string, string> settings = JsonConvert.DeserializeObject<Dictionary<string, string>>(CemuSettings)!;
        //        CemuDir = settings["cemu_dir"];
        //        GameDir = settings["game_dir"];
        //    }
        //    catch(Exception ex)
        //    {
        //        CemuDir = "";
        //        GameDir = "";
        //    }
        //}

        async Task connectToServer(serverDataModel serverToJoin = null)
        {
            bool directConnection = serverToJoin != null;

            if (serverToJoin == null)
                serverToJoin = _selectedServer[0];

            if (string.IsNullOrEmpty(BcmlSettings.GameDir) || string.IsNullOrEmpty(BcmlSettings.CemuDir))
                throw new ApplicationException("Bcml not setup.");

            if (!IsModInstalled)
                throw new ApplicationException("Mod is not setup on BCML. Make sure to follow the instructions to install the mod.");

            string cemuSettingsPath = Path.Combine(BcmlSettings.CemuDir, "settings.xml");

            // Does the settings file for cemu exist?
            if (!File.Exists(cemuSettingsPath))
                throw new ApplicationException("Cemu settings file doesn't exist. Make sure to have open cemu and setup the graphic packs");

            XmlDocument doc = new XmlDocument();
            doc.Load(cemuSettingsPath);

            XmlNode GraphicPacksNode = doc.DocumentElement.SelectSingleNode("/content/GraphicPack");

            Dictionary<string, string> nodesToValidate = new Dictionary<string, string>() {
                {"  · Utilities", "bcmlPatches\\MilkBarLauncher\\rules.txt"},
                {"  · Extended Memory", "BreathOfTheWild\\Mods\\ExtendedMemory\\rules.txt" }
            };

            List<string> missingGraphicPacks = new List<string>();

            try
            {
                missingGraphicPacks = nodesToValidate
                                        .Where(sNode => !GraphicPacksNode.ChildNodes.Cast<XmlNode>().Any(node => node.Attributes.Cast<XmlAttribute>().Any(attribute => attribute.Value.Contains(sNode.Value)))).Select(sNode => sNode.Key).ToList();
            }
            catch(Exception ex)
            {
                // If any issue happened while reading settings xml.
                throw new ApplicationException("Failed to read cemu graphic packs settings. Please make sure that your cemu is setup correctly.");
            }

            if(missingGraphicPacks.Any())
                throw new ApplicationException("The following needed graphic packs are not selected on Cemu\n\n" + string.Join("\n", missingGraphicPacks) + "\n\nPlease make sure to select them before joining a server.");

            if (string.IsNullOrEmpty(serverToJoin.IP) || !serverToJoin.open)
                return;

            serverToJoin.pingServer();

            if (!serverToJoin.open)
            {
                if (directConnection)
                    throw new Exception("Could not connect to server. The server may not be open.");
                else
                    return;
            }

            CharacterModel charModel = JsonConvert.DeserializeObject<CharacterModel>(Properties.Settings.Default.playerModel);

            SharedData.SetLoadingMessage("Loading player data...");

            await Task.Run(() => GameFilesModifier.CreateModifiedModel());

            await Task.Run(() => GameFilesModifier.ChangeAttentionForJugadores(serverToJoin.playStyle != "Prop hunt"));

            await Task.Run(() => GameFilesModifier.CleanAnimations());

            if (!Properties.Settings.Default.playAsModel)
            {
                await Task.Run(() => GameFilesModifier.ModifyGameROMPlayerModel("Link", "Link", false));
            }
            else
            {
                switch (charModel.Type)
                {
                    case CharacterModel.ModelType.ArmorSync:
                        // TODO: Add implementation for other armors with armor sync
                        await Task.Run(() => GameFilesModifier.ModifyGameROMPlayerModel(charModel.Name, charModel.Name, false));
                        break;
                    case CharacterModel.ModelType.CustomModel:
                        // We found that the game is only capable of having NPCs instead of Link. Therefore, we only change Link's model if it is an NPC
                        if (charModel.Model.StartsWith("Npc_"))
                            await Task.Run(() => GameFilesModifier.ModifyGameROMPlayerModel(charModel.Model, charModel.Model, true));
                        // MilkBar2: "MP_<name>" player models (Jugador-style folder, see MILKBAR2.md) replace only the body
                        // and keep Link's armor setup; aapmLib maps each armor piece to the "MP_<name>_MP" folder.
                        else if (charModel.Model.StartsWith("MP_"))
                            await Task.Run(() => GameFilesModifier.ModifyGameROMPlayerModel(charModel.Model, charModel.Model, false));
                        else
                            await Task.Run(() => GameFilesModifier.ModifyGameROMPlayerModel("Link", "Link", false));
                        break;
                    case CharacterModel.ModelType.Mii:
                        // TODO: Miis are not implemented. We found that trying to change Link to a Mii, you get issues when trying to find certain things in memory.
                        // If you want to work on this, go to the InjectDLL project, into the file MemoryAccess/LocalInstance.h
                        // There you can find the scan method. There we are looking for all the necessary addresses in memory. The specific memories that we found have issues are Animation and IsEquipped.
                        await Task.Run(() => GameFilesModifier.ModifyGameROMPlayerModel("Link", "Link", false));
                        break;
                }
            }

            await Task.Delay(2000);

            List<Process> ProcessesToFilter = Injector.GetProcesses("Cemu");

            SharedData.SetLoadingMessage("Starting Cemu...");

            await Task.Run(() => Process.Start($"{BcmlSettings.CemuDir}/cemu.exe", $"-g \"{BcmlSettings.GameDir.Replace("content", "code")}/U-King.rpx\""));

            await Task.Delay(500);

            SharedData.SetLoadingMessage("Injecting Cemu...");

            Process CemuProcess = null;

            await Task.Run(() => {
                CemuProcess = Injector.Inject("Cemu", Directory.GetCurrentDirectory() + "\\Resources\\InjectDLL.dll", ProcessesToFilter);
            });

            SharedData.SetLoadingMessage("Starting pipe...");

            try
            {
                await Task.Run(NamedPipes.StartServer);
            }
            catch (Exception ex)
            {
                CemuProcess.Kill();
                throw ex;
            }

            SharedData.SetLoadingMessage("Connecting to server...");

            List<byte> instruction = Encoding.UTF8.GetBytes($"!connect;{serverToJoin.IP};{serverToJoin.Port};{serverToJoin.Password};{Properties.Settings.Default.playerName};{serverToJoin.Name};{(int)charModel.Type};").ToList();

            if ((int)charModel.Type < 2)
            {
                if(charModel.Model.StartsWith("Npc_"))
                {
                    instruction.AddRange(Encoding.UTF8.GetBytes($"{charModel.Model}:{charModel.Model}"));
                } 
                else
                {
                    instruction.AddRange(Encoding.UTF8.GetBytes($"{charModel.Model}:{charModel.Name}"));
                }
            }
            else
                instruction.AddRange(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(charModel.BumiiData)));

            instruction.AddRange(Encoding.UTF8.GetBytes(";[END]"));

            await Task.Run(() => {
                if (!NamedPipes.sendInstruction(instruction.ToArray()))
                {
                    CemuProcess.Kill();
                    throw new ApplicationException("Could not connect to server. Internal connection failed.");
                }

                Task.Delay(50);

                if (!NamedPipes.sendInstruction($"!startServerLoop"))
                {
                    CemuProcess.Kill();
                    throw new ApplicationException("Could not start server loop.");
                }
            });

            SharedData.SetLoadingMessage("Starting overlay...");

            await Task.Delay(2000);

            SharedData.MainView.currentView = SharedData.MainView.IngameMenuVM;
            SharedData.MainView.disableBackground = true;
            SharedData.MainView.Window.Topmost = true;

            CemuFollower.Setup(CemuProcess);
            NamedPipes.StartListenThread();

            SharedData.SetLoadingMessage();
        }

        void SaveServerSettings()
        {
            Properties.Settings.Default.serversAdded = JsonConvert.SerializeObject(this.ServerList);
            Properties.Settings.Default.Save();
        }

        public void ModifyServer(ServerInfoModel serverinfo)
        {
            if(serverinfo.serverIndex == -1)
            {
                var serverToAdd = new LocalServerDTO()
                {
                    Name = serverinfo.name,
                    IP = serverinfo.ip,
                    Port = Int32.Parse(serverinfo.port),
                    Password = serverinfo.password,
                    Favorite = false
                };

                this.ServerList.Add(serverToAdd);

                this.serversToShow.Add(new serverDataModel(!string.IsNullOrEmpty(BcmlSettings.GameDir) && !string.IsNullOrEmpty(BcmlSettings.CemuDir), name: serverinfo.name, ip: serverinfo.ip, port: Int32.Parse(serverinfo.port), favorite: false, password: serverinfo.password));
                this.ServerMapping[serverDataModel.serversAdded - 1] = serverToAdd;
                changeSelected(this._serversToShow.Count - 1);

                SaveServerSettings();
            }
            else if(serverinfo.serverIndex == -2)
            {
                serverDataModel serverToconnect = new serverDataModel(!string.IsNullOrEmpty(BcmlSettings.GameDir) && !string.IsNullOrEmpty(BcmlSettings.CemuDir), name: serverinfo.name, ip: serverinfo.ip, port: Int32.Parse(serverinfo.port), favorite: false, password: serverinfo.password, async: false);

                if (!serverToconnect.open)
                    throw new Exception("Could not connect to server. Check if the server is open.");

                this.connectToServer(serverToconnect);
            }
            else
            {
                this.ServerMapping[serverinfo.serverIndex].Name = serverinfo.name;
                this.ServerMapping[serverinfo.serverIndex].IP = serverinfo.ip;
                this.ServerMapping[serverinfo.serverIndex].Port = Int32.Parse(serverinfo.port);
                this.ServerMapping[serverinfo.serverIndex].Password = serverinfo.password;

                this.serversToShow[serverinfo.serverIndex].Name = serverinfo.name;
                this.serversToShow[serverinfo.serverIndex].IP = serverinfo.ip;
                this.serversToShow[serverinfo.serverIndex].Port = Int32.Parse(serverinfo.port);
                this.serversToShow[serverinfo.serverIndex].Password = serverinfo.password;

                SaveServerSettings();

                this.selectedServer[0].pingServer();
            }
        }

        public void EditServer()
        {
            var DirectConnectEditor = new ServerEditorModel();
            DirectConnectEditor.Setup(this.selectedServer[0].serverIndex, this.selectedServer[0].Name, this.selectedServer[0].IP, this.selectedServer[0].Port.ToString(), this.selectedServer[0].Password.ToString());
            SharedData.MainView.updateTopView(DirectConnectEditor);
        }

        public void RemoveServer()
        {
            this.ServerList.Remove(this.ServerMapping[this.selectedServer[0].serverIndex]);
            SaveServerSettings();
            LoadServers();
        }

        public void ChangeFavorite(int serverIndex)
        {
            this.ServerMapping[serverIndex].Favorite = this.serversToShow[serverIndex].favorite;

            SaveServerSettings();
        }
    }
}
