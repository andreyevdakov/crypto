/*
 * DELL PROPRIETARY INFORMATION
 *
 * This software is confidential.  Dell Inc., or one of its subsidiaries, has
 * supplied this software to you under the terms of a license agreement,
 * nondisclosure agreement or both.  You may not copy, disclose, or use this 
 * software except in accordance with those terms.
 *
 * Copyright 2015 Dell Inc.  
 * ALL RIGHTS RESERVED.
 *
 * DELL INC. MAKES NO REPRESENTATIONS OR WARRANTIES
 * ABOUT THE SUITABILITY OF THE SOFTWARE, EITHER EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY, FITNESS FOR A
 * PARTICULAR PURPOSE, OR NON-INFRINGEMENT. DELL SHALL
 * NOT BE LIABLE FOR ANY DAMAGES SUFFERED BY LICENSEE
 * AS A RESULT OF USING, MODIFYING OR DISTRIBUTING
 * THIS SOFTWARE OR ITS DERIVATIVES.
 *
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Globalization;
using System.Threading;
using System.Xml.Serialization;
using DAConsole.Common;
using ScriptLogic.DAConsole.Common;
using ScriptLogic.DAConsole.Common.OdsReplication;
using ScriptLogic.DAConsole.DAComponentBusinessLogic;
using ScriptLogic.DAConsole.DAComponentBusinessLogic.DOProviders;
using ScriptLogic.DAConsole.DAComponentCore;
using ScriptLogic.DAConsole.DAComponentCore.SingleUseThreadPool;
using ScriptLogic.DAConsole.DomainModel;
using ScriptLogic.DAConsole.DomainModel.ServerManager;
using ScriptLogic.DAConsole.DomainModel.SystemSecurity;
using ScriptLogic.DAConsole.DAComponentBusinessLogic.Settings;
using SMWinService.ClientDeployment;
using SMWinService.DynamicVariables;
using SMWinService.PathManagement;
using SMWinService.ServersManagement;
using ThreadPool = ScriptLogic.DAConsole.DAComponentCore.SingleUseThreadPool.ThreadPool;
using System.Data;

namespace SMWinService.Replication
{
    public class Replicator
    {
        private static OdsReplicationInfo _activeOdsReplicationInfo = new OdsReplicationInfo();
        private static ReplicationInfo _activeReplicationInfo = new ReplicationInfo();
        private static ReplicationLog _log;
        private DateTime _startedTime;
        private readonly string _configurationPath;
        private readonly List<WorkItem> _workUnits = new List<WorkItem>();
        private readonly ServerManager _serverManager;
        private bool _isGpoDeploymentEnabled;
        private static SessionId _sessionId;
        private List<ServerInfo> _replicateServers = new List<ServerInfo>();

        private readonly List<string> _replicatedFilesUbm = new List<string>(); 
        private readonly List<string> _replicatedFilesCbm = new List<string>();
        private Dictionary<string, string> _settings;
        private StorageType _odsStorageType;

        public ServerManagerOptionsInfo ServerOptions { get; set; }

        public string UserReplicationSourcePath
        {
            get
            {
                return (ServerOptions != null) ?
                    ServerOptions.UserReplicationSourcePath : string.Empty;
            }
        }

        public string ComputerReplicationSourcePath
        {
            get
            {
                return (ServerOptions != null) ?
                    ServerOptions.ComputerReplicationSourcePath : string.Empty;
            }
        }

        public ReplicationInfo ActiveReplicationInfo
        {
            get
            {
                return _activeReplicationInfo;
            }
        }

        public OdsReplicationInfo ActiveOdsReplicationInfo
        {
            get
            {
                return _activeOdsReplicationInfo;
            }
        }

        public List<ServerInfo> ReplicationServers
        {
            set { _replicateServers = value.ToList(); }
        }

        public UserOrGroup User { get; set; }

        public bool ForceUpdate { get; set; }

        public bool ShouldCheckGpo { get; set; }

        private int ExecutionTime { get; set; }

        private static List<string> Messages
        {
            get { return _log.Messages; }
        }

        public Replicator(string configurationPath, ServerManager serverManager)
        {
            _configurationPath = configurationPath;
            _activeReplicationInfo.StateString = Constants.Ready;
            _serverManager = serverManager;
        }

        public void ReplicateTo(SessionId sessionId)
        {
            _sessionId = sessionId;

            var odsReplicationEnabled = AppSettingsHelper.GetValue("ODSReplicationEnabled", false);
            if (odsReplicationEnabled)
            {
                InitializeOdsReplicationSettings();
            }

            _startedTime = DateTime.Now;
            Program.Log.Info(string.Format("Replication. Started. User: {0} ({1})", User.FullName, _startedTime));

            DomainReplication();

            Program.Log.Info("Replication. Finished");

            if (!_activeReplicationInfo.IsSuccessful)
                return;

            if (!odsReplicationEnabled)
            {
                Program.Log.Info(
                    "ODS replication was not started. To enable it set parameter ODSReplicationEnabled to TRUE in the config file");
                Program.Log.Info("To enable it set parameter ODSReplicationEnabled to TRUE in the config file");
                return;
            }

            Program.Log.Info("Start Off Domain replication");

            Exception exception = null;

            var thread = new Thread(() =>
                                    {
                                        try
                                        {
                                            var replicator = new OdsReplicator
                                                             {
                                                                  DomainReplicator = this
                                                             };

                                            replicator.ReplicateTo(_sessionId);
                                        }
                                        catch (Exception ex)
                                        {
                                            exception = ex;
                                        }
                                    })
                         {
                             IsBackground = true
                         };

            thread.Start();

            if (exception != null)
                Program.Log.Error("Ods replication exception.", exception);

            Program.Log.Info("End Off Domain replication");
        }


        private void DomainReplication()
        {
            _replicatedFilesCbm.Clear();
            _replicatedFilesUbm.Clear();

            _activeReplicationInfo = new ReplicationInfo
                                     {
                                         StartTime = DateTime.Now,
                                         Status = ReplicationInfo.ReplicationStatus.InProgress,
                                         IsSuccessful = true,
                                         User = User
                                     };

            PrepareReplicationServerList();

            try
            {
                if (!_activeReplicationInfo.IsCancelled)
                {
                    Program.Log.Info(string.Format("Replication. Check if GPO enabled"));
                    _activeReplicationInfo.StateString = Constants.CheckGPO;

                    _isGpoDeploymentEnabled = ServerOptions.ClientFilesLocation == ClientFilesLocation.NETLOGON
                        ? GetIsGpoDeploymentEnabled()
                        : !ShouldCheckGpo || GetIsGpoDeploymentEnabled();
                }

                InitializeLog();

                if (!_activeReplicationInfo.IsCancelled)
                    _serverManager.DataStorage.SuspendReplicationFilesCache();

                if (!_activeReplicationInfo.IsCancelled)
                    PreReplicate();

                if (!_activeReplicationInfo.IsCancelled)
                    PerformReplication();

                _activeReplicationInfo.IsSuccessful =
                    _activeReplicationInfo.ProgressInfo.All(
                        info => info.ExecutionState != ExecutionInfo.ExecutionInfoState.Failed);

                if (!_activeReplicationInfo.IsCancelled)
                {
                    if (_activeReplicationInfo.IsSuccessful)
                    {
                        Program.Log.Info("Replication. ActualizeClientFilesVersion()");
                        _activeReplicationInfo.StateString = Constants.ActualizeClientFilesVersion;

                        ClientFilesHelper.ActualizeClientFilesVersion();
                        Program.Log.Info("Replication. End - ActualizeClientFilesVersion()");
                    }
                }
            }
            catch (Exception ex)
            {
                _serverManager.DataStorage.ResetReplicationFilesCache();

                Program.Log.Error("Replication.", ex);

                Messages.Add(string.Format("Replication failed: preparation error occured: {0}", ex.Message));

                _activeReplicationInfo.StateString = Constants.FinishErrorProcess;

                _activeReplicationInfo.IsSuccessful = false;
                _activeReplicationInfo.ProgressInfo.ForEach(
                    progessInfo => progessInfo.ExecutionState = ExecutionInfo.ExecutionInfoState.Failed);
            }
            finally
            {
                if (_activeReplicationInfo.IsCancelled)
                    Messages.Add(string.Format("Replication was cancelled"));

                _activeReplicationInfo.FinishTime = DateTime.Now;

                FinalizeReplication();
            }
        }


        private void InitializeOdsReplicationSettings()
        {
            var settingStr = GetPublicationSettingsString();

            if (null == settingStr || string.IsNullOrEmpty(settingStr.Value))
            {
                Program.Log.Info("Settings are not selected");
                return;
            }

            _odsStorageType = settingStr.StorageType;

            _settings = Decode(settingStr.Value);

            try
            {
                StoreSettingsToClientFile();
            }
            catch (Exception ex)
            {
                Program.Log.Error("OdsReplication error", ex);
            }
        }

        private const string OdsSettingsClientFile = "OdsSettings.cfg";

        private void StoreSettingsToClientFile()
        {
            var settings = new OdsReplicationSettings();

            var listSettings = _settings.Select(s => new OdsSetting {Key = s.Key, Value = s.Value}).ToList();

             
            listSettings.Add(new OdsSetting { Key = OdsConstants.ClientCbmFolder, Value = @"C:\CBM_Files"});
            listSettings.Add(new OdsSetting { Key = OdsConstants.ClientUbmFolder, Value = @"C:\UBM_Files" });


            settings.Settings = listSettings;
            settings.UserReplicationSourcePath = UserReplicationSourcePath;
            settings.ComputerReplicationSourcePath = ComputerReplicationSourcePath;
            settings.StorageType = _odsStorageType;

            var writer = new XmlSerializer(typeof(OdsReplicationSettings));
            var path = Path.Combine(UserReplicationSourcePath, OdsSettingsClientFile);
            if (File.Exists(path))
                File.Delete(path);

            using (var file = File.Create(path))
            {
                writer.Serialize(file, settings);
                file.Close();
            }

            path = Path.Combine(ComputerReplicationSourcePath, OdsSettingsClientFile);
            if (File.Exists(path))
                File.Delete(path);

            using (var file = File.Create(path))
            {
                writer.Serialize(file, settings);
                file.Close();
            }

        }

        public static Dictionary<string, string> Decode(string currentParams)
        {
            if (string.IsNullOrEmpty(currentParams))
                return new Dictionary<string, string>();

            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(currentParams)))
            {
                try
                {
                    var serializer =
                            new DataContractJsonSerializer(typeof(Dictionary<string, string>));

                    return (Dictionary<string, string>)serializer.ReadObject(ms);
                }
                catch
                {
                    return new Dictionary<string, string>();
                }
            }
        }


        private void PrepareReplicationServerList()
        {
            var serverNames = new HashSet<string>(_replicateServers.Select(s => s.FullName.ToUpperInvariant()));

            while (true)
            {
                if (_activeReplicationInfo.IsCancelled)
                    break;

                var servers = _serverManager.GetServersInfo()
                    .Where(s => serverNames.Contains(s.FullName.ToUpperInvariant()))
                    .ToList();

                var count = servers.Count(s => ServiceStatus.IsIngStatus(s.SLStatus));
                if (count > 0)
                {
                    _activeReplicationInfo.StateString = Constants.WaitAdminServices + " " + count;
                    continue;
                }

                _replicateServers = servers.Where(s => s.IsAnyReplicationTarget).ToList();

                break;
            }

            if (!_activeReplicationInfo.IsCancelled)
                _replicateServers.ForEach(s => ServerManager.StopReportingFromServer(s.ServerName));
        }

        private void FinalizeReplication()
        {
            try
            {
                ServerManager.UpdateStatusRequest(false);

                if (!_activeReplicationInfo.IsCancelled)
                UpdateReplicationState();
            }
            catch (Exception ex)
            {
                    _activeReplicationInfo.StateString = Constants.FinishErrorProcess;
                _activeReplicationInfo.IsSuccessful = false;
                Messages.Add(string.Format("ERROR: following exception occurred during finalizing replication: {0}\n", ex.Message));
            }
            finally
            {
                Program.Log.Info("FinalizeLog()");
                FinalizeLog();

                Program.Log.Info("SaveLog();");
                SaveLog();

                Program.Log.Info("UnlockCustomObjects() - 1");
                BusinessLogicScripts.UnlockCustomObjects(_sessionId,
                    Constants.ServerManagerLock, new List<string> { "ReplicationLockId" });

                Program.Log.Info("UnlockCustomObjects() - 2");
                BusinessLogicScripts.UnlockCustomObjects(_sessionId,
                    Constants.ServerManagerLock, _replicateServers.Select(s => s.FullName).ToList());

                _activeReplicationInfo.StateString = _activeReplicationInfo.IsSuccessful ?
                    Constants.FinishProcess : Constants.FinishErrorProcess;

                _activeReplicationInfo.Status = ReplicationInfo.ReplicationStatus.Finish;

                Program.Log.Info("Replication End");
            }
        }

        private bool GetIsGpoDeploymentEnabled()
        {
            try
            {
                return ServerOptions.ClientFilesLocation == ClientFilesLocation.SYSVOL ||
                    new GPOManager().GetClientDeploymentGpoObjects().Count > 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void PreReplicate()
        {
            _activeReplicationInfo.StateString = Constants.CreationExportFiles;

            Program.Log.Info("Replication.");

            var isAnyUbmReplicationTargets = _replicateServers.Any(s => s.IsUbmReplicationTarget == true);
            var isAnyCbmReplicationTargets = _replicateServers.Any(s => s.IsCbmReplicationTarget == true);

            try
            {
                if (!_activeReplicationInfo.IsCancelled)
                {
                    if (isAnyUbmReplicationTargets)
                    {
                        _replicatedFilesUbm.AddRange(
                            new ExportProfileData(ServerOptions.UserReplicationSourcePath, ForceUpdate)
                                .ExportReplicationData());
                    }
                    if (isAnyCbmReplicationTargets)
                    {
                        new ExportCBMData(ServerOptions.ComputerReplicationSourcePath,
                                          ServerManager.DaPublicationExecutionTimeout).Export();
                        _replicatedFilesCbm.Add(Path.Combine(ServerOptions.ComputerReplicationSourcePath, "CBMConfig.xml.gzip"));
                        _replicatedFilesCbm.Add(Path.Combine(ServerOptions.ComputerReplicationSourcePath, "ComputerConfiguration.ini"));
                    }
                }

                if (!_activeReplicationInfo.IsCancelled && isAnyUbmReplicationTargets)
                {
                    Program.Log.Info("Replication. UpdateSLogicBat()");
                    string slogicPath = Path.Combine(UserReplicationSourcePath, "SLogic.bat");
                    UpdateSLogicBat(slogicPath);
                    _replicatedFilesUbm.Add(slogicPath);
                }

                if (!_activeReplicationInfo.IsCancelled)
                {
                    Program.Log.Info("Replication. UpdateSLStartIni()");
                    string slStartIniPath = Path.Combine(UserReplicationSourcePath, "SLStart.ini");
                    UpdateSlStartIni(slStartIniPath);
                    _replicatedFilesUbm.Add(slStartIniPath);
                }

                if (!_activeReplicationInfo.IsCancelled)
                {
                    Program.Log.Info("Replication. UpdateSLMgrIni()");
                    string slMgrIniPath = PathManager.Create().SlMrgIniPath;
                    UpdateSlMgrIni(slMgrIniPath);
                    _replicatedFilesUbm.Add(slMgrIniPath);
                }

                if (!_activeReplicationInfo.IsCancelled)
                {
                    Program.Log.Info("Replication. UpdateSlBoostIni()");
                    var slBoostIniPath = Path.Combine(UserReplicationSourcePath, "SLBoost.ini");
                    UpdateSlBoostIni(slBoostIniPath);
                    _replicatedFilesUbm.Add(slBoostIniPath);
                }

                if (!_activeReplicationInfo.IsCancelled)
                {
                    Program.Log.Info("Replication. SiteMapUpdater.UpdateFiles(...)");
                    var siteFilePath = Path.Combine(UserReplicationSourcePath, "DASiteMap.ini");
                    var locFilePath = Path.Combine(UserReplicationSourcePath, "DaLocMap.ini");
                    SiteMapUpdater.UpdateFiles(_serverManager, siteFilePath, locFilePath);
                    _replicatedFilesUbm.Add(siteFilePath);
                    _replicatedFilesUbm.Add(locFilePath);
                }

                if (!_activeReplicationInfo.IsCancelled)
                {
                    Program.Log.Info("Replication. CreateSignatureFile()");
                    var signaturesFile = Path.Combine(UserReplicationSourcePath, "slSigs.ini");
                    CreateSignatureFile(signaturesFile, isAnyUbmReplicationTargets, isAnyCbmReplicationTargets);
                    _replicatedFilesUbm.Add(signaturesFile);
                }

                if (!_activeReplicationInfo.IsCancelled && isAnyCbmReplicationTargets)
                {
                    Program.Log.Info("Replication. CopyCBMFiles()");
                    CopyCbmFiles();
                }

                if (!_activeReplicationInfo.IsCancelled && isAnyUbmReplicationTargets)
                {
                    Program.Log.Info("Replication. CopyProvisioningFiles()");
                    CopyProvisioningFiles();
                }

                Program.Log.Info("Replication. PreReplicate finished");
            }
            catch (Exception ex)
            {
                Program.Log.Error("Replication", ex);
                throw;
            }
        }

        private void CopyCbmFiles()
        {
            var files = new List<string> { "slstart.ini", "dasitemap.ini", "dalocmap.ini", "slsigs.ini" };
            foreach (var file in files)
            {
                CopyFile(Path.Combine(UserReplicationSourcePath, file),
                    Path.Combine(ComputerReplicationSourcePath, file));
            }

            _replicatedFilesCbm.AddRange(files);
        }

        private void CopyProvisioningFiles()
        {
            var files = new List<string>
                 {
                     "WindowsXP-KB898715-x64-enu.exe", "WindowsInstaller-KB893803-v2-x86.exe",
                     "netfx64.exe", "dotnetfx.exe", "DAClientInstall.msi"
                 };

            if (ServerOptions.ClientFilesLocation == ClientFilesLocation.NETLOGON)
            {
                foreach (var file in files)
                {
                    CopyFileIfNewer(Path.Combine(ComputerReplicationSourcePath, file),
                                    Path.Combine(UserReplicationSourcePath, file));
                }
            }
            else if (_isGpoDeploymentEnabled)
            {
                Messages.Add("SYSVOL Based logon-script deployment");
                string server = ServerOptions.SysVolPublicationServer;
                if (string.IsNullOrEmpty(server))
                {
                    string defaultDomainController =
                        _replicateServers.FirstOrDefault(s => s.IsDomainController == true).ServerName;
                    if (string.IsNullOrEmpty(defaultDomainController))
                    {
                        Messages.Add("ERROR: Cannot publish client files to SYSVOL, Publication Target Server is blank!");
                        return;
                    }
                    Messages.Add(string.Format("Using first server: {0}", defaultDomainController));
                    server = defaultDomainController;
                }

                server = server.TrimStart('\\');
                Messages.Add(string.Format("Server after trim: {0}", server));

                string domainName = DomainControllerCache.GetCurrentDomainFullName();
                string destinationPath = GetAgentDestinationPath(server, domainName);
                Messages.Add(string.Format("Destination path: {0}", destinationPath));

                CreateDirectory(GetDesktopAuthorityPoliciesPath(server, domainName));
                CreateDirectory(GetAgentDestinationPath(server, domainName));

                foreach (var file in files)
                {
                    string source = Path.Combine(ComputerReplicationSourcePath, file);
                    string target = Path.Combine(destinationPath, file);
                    Messages.Add(string.Format("Copy: {0} -> {1}", source, target));
                    CopyFileIfNewer(source, target);
                }
            }
        }

        private static void CreateDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                Messages.Add(string.Format("Directory {0} already exists", path));
            }
            else
            {
                try
                {
                    Directory.CreateDirectory(path);
                }
                catch (Exception)
                {
                    Messages.Add(string.Format("ERROR: Cannot create subdirectory {0}", path));
                }
            }
        }

        private static void CopyFile(string source, string target)
        {
            try
            {
                if (File.Exists(target))
                {
                    File.SetAttributes(target, FileAttributes.Normal);
                }
                File.Copy(source, target, true);
            }
            catch (Exception ex)
            {
                Messages.Add(string.Format("Unable copy {0} to {1}: {2}", source, target, ex.Message));
            }
        }

        private static void CopyFileIfNewer(string source, string target)
        {
            try
            {
                if (File.Exists(source))
                {
                    if (File.Exists(target))
                    {
                        if (File.GetLastWriteTime(target) == File.GetLastWriteTime(source))
                        {
                            return;
                        }
                        File.SetAttributes(target, FileAttributes.Normal);
                    }
                    File.Copy(source, target, true);
                }
            }
            catch (Exception ex)
            {
                Program.Log.Error(string.Format(
                    "Unable to copy provisioning file '{0}' to '{1}'", source, target), ex);
                Messages.Add(string.Format("Unable copy {0} to {1}: {2}", source, target, ex.Message));
                throw;
            }
        }

        private void UpdateSLogicBat(string slogicPath)
        {
            _activeReplicationInfo.StateString = Constants.UpdateSlogicBat;

            const string replicateDateComment = "REM Last replicated";

            Program.Log.Info(string.Format("Replication. Update file: \"{0}\"", slogicPath));

            string[] batLines = File.ReadAllLines(slogicPath);

            int rdLine; // Index of replicatation date line.
            for (rdLine = batLines.Length; rdLine-- > 0; )
            {
                if (batLines[rdLine].Contains(replicateDateComment))
                    break;
            }

            string updatedReplicateDateComment =
                replicateDateComment + " " + GetCurrentDateTime();

            if (rdLine >= 0)
                batLines[rdLine] = updatedReplicateDateComment;
            else
            {
                const string copyrightComment = "Rem Copyright";
                for (int i = batLines.Length; i-- > 0; )
                {
                    if (batLines[rdLine].Contains(copyrightComment))
                    {
                        var temp = new List<string>(batLines);
                        temp.Insert(i + 1, updatedReplicateDateComment);
                        batLines = temp.ToArray();
                        break;
                    }
                }
            }

            File.WriteAllLines(slogicPath, batLines);
        }

        private void UpdateSlStartIni(string slStartIniPath)
        {
            _activeReplicationInfo.StateString = Constants.UpdateSlstartIni;

            Program.Log.Info(string.Format("Replication. Update file: \"{0}\"", slStartIniPath));

            var profilerIni = new ProfilerINI(slStartIniPath, "Status");
            profilerIni.WriteString("Published", GetCurrentDateTime());
            Program.Log.Info("Replication. UpdateSLStartIni() - 1");
            profilerIni = new ProfilerINI(slStartIniPath, "Run Settings");

            profilerIni.WriteString("DAClientLocation", string.Format(@"\\{0}\SLDAClient$", ServerOptions.SourceServer));
            Program.Log.Info("Replication. UpdateSLStartIni() - 2");
            UpdateAsyncVersions(profilerIni, "sldatacollection.dll");
            Program.Log.Info("Replication. UpdateSLStartIni() - 3");
            UpdateAsyncVersions(profilerIni, "wkix32.exe");
            Program.Log.Info("Replication. UpdateSLStartIni() - 4");
            UpdateAsyncVersions(profilerIni, "slapieng*.dll");
            Program.Log.Info("Replication. UpdateSLStartIni() - 5");

            UpdatePmSection(slStartIniPath);
            Program.Log.Info("Replication. UpdateSLStartIni() - 6");
            UpdateRunSettingsSection(slStartIniPath);
            Program.Log.Info("Replication. UpdateSLStartIni() - 7");
        }

        private void UpdateAsyncVersions(ProfilerINI profilerIni, string fileMask)
        {
            const string keyNameFormat = "Async_Version_{0}";
            var keyWildcardPattern = new WildcardPattern(string.Format(keyNameFormat, fileMask).ToLower());
            var keys = profilerIni.ReadSection()
                .Where(p => keyWildcardPattern.IsMatch(p.Key.ToLower())).
                Select(p => p.Key).ToList();
            keys.ForEach(key => profilerIni.DeleteKey(key));
            foreach (var file in Directory.GetFiles(UserReplicationSourcePath, fileMask).Select(f => new FileInfo(f)))
            {
                profilerIni.WriteString(string.Format(keyNameFormat, file.Name.ToUpper()),
                                        file.LastWriteTime.ToString("yyyyMMdd"));
            }
        }

        private void UpdateSlMgrIni(string slMgrIniPath)
        {
            _activeReplicationInfo.StateString = Constants.UpdateSlmgrIni;
            Program.Log.Info(string.Format("Replication. Update file: \"{0}\"", slMgrIniPath));
            UpdatePmSection(slMgrIniPath);
        }

        private void UpdateSlBoostIni(string slBoostIniPath)
        {
            var profiler = new ProfilerINI(slBoostIniPath, "Status");// "%A, %B %d, %Y - %H:%M:%S"            
            profiler.WriteString("Published", DateTime.Now.ToString("dddd, MMMM dd, yyyy - HH:mm:ss", new CultureInfo("en-US")));

            profiler = new ProfilerINI(slBoostIniPath, "SLBOOST");
            profiler.WriteString("SourceMode", ServerOptions.ClientFilesLocation.ToString());
            profiler.WriteBool("UseSCM", true);
            profiler.WriteBool("UseRemoteWMI", true);
            profiler.WriteBool("UseTokenElevation", true);
            profiler.WriteBool("AllowUACPrompt", ServerOptions.AllowUacDialog);
            profiler.WriteBool("AllowFailureDialog", ServerOptions.DisplayFailureDialog);

            string destinationPath;
            if (ServerOptions.ClientFilesLocation == ClientFilesLocation.NETLOGON)
            {
                destinationPath = ".\\";
            }
            else
            {
                string domainName = DomainControllerCache.GetCurrentDomainFullName();
                destinationPath = GetAgentDestinationPath(domainName, domainName);
            }
            profiler = new ProfilerINI(slBoostIniPath, "SLINSTALL");
            profiler.WriteString("MsiAction", "Install");
            profiler.WriteString("WindowsInstaller31_64", string.Format("\"{0}\"",
                Path.Combine(destinationPath, "WindowsXP-KB898715-x64-enu.exe")));
            profiler.WriteString("WindowsInstaller31_32", string.Format("\"{0}\"",
                Path.Combine(destinationPath, "WindowsInstaller-KB893803-v2-x86.exe")));
            profiler.WriteString("NetFramework20_64", string.Format("\"{0}\"",
                Path.Combine(destinationPath, "netfx64.exe")));
            profiler.WriteString("NetFramework20_32", string.Format("\"{0}\"",
                Path.Combine(destinationPath, "dotnetfx.exe")));
            profiler.WriteString("MsiFilename", string.Format("\"{0}\"",
                Path.Combine(destinationPath, "DAClientInstall.msi")));

            File.SetLastWriteTime(slBoostIniPath, DateTime.Now);
        }

        private static string GetDesktopAuthorityPoliciesPath(string server, string domainName)
        {
            return string.Format("\\\\{0}\\SYSVOL\\{1}\\Policies\\Desktop Authority", server, domainName);
        }

        private static string GetAgentDestinationPath(string server, string domainName)
        {
            return Path.Combine(GetDesktopAuthorityPoliciesPath(server, domainName), "Desktop Authority Agent 8.0");
        }

        private void UpdatePmSection(string iniPath)
        {
            var pmSect = new ProfilerINI(iniPath, "Patch Management");
            var pmServersListBuilder = new StringBuilder();

            foreach (ServerInfo info in _serverManager.GetServersInfo())
            {
                if (info.IsUpdateSvcInstalled && info.UpdateService.ResolvedStatus != "Unknown")
                {
                    pmServersListBuilder.Append(info.ServerName);
                    pmServersListBuilder.Append(",");

                    string details = info.Site + "," + (info.UpdateService.IsDownloadServer.Value ? "1" : "0") +
                                     "," + info.UpdateService.DownloadServer;
                    pmSect.WriteString(info.ServerName, details);
                }
                else
                    pmSect.WriteString(info.ServerName, null); // delete string
            }

            if (pmServersListBuilder.Length > 0) // remove last ","
                pmServersListBuilder.Remove(pmServersListBuilder.Length - 1, 1);

            pmSect.WriteString("DAPatchManagementServers", pmServersListBuilder.ToString());
        }

        private void UpdateRunSettingsSection(string iniPath)
        {
            // Create list of sl servers.
            var slServers = _replicateServers.Where(info => info.ScriptLogicService != null).ToList();

            var rsSection = new ProfilerINI(iniPath, "Run Settings");

            // Create string with list of all SL servers.
            var slServersList = new StringBuilder();
            foreach (ServerInfo info in slServers)
            {
                slServersList.Append(@"\\");
                slServersList.Append(info.ServerName);
                slServersList.Append(",");
            }
            rsSection.WriteString("SLRPC", slServersList.ToString());

            var siteServerMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (ServerInfo info in slServers)
            {
                string site = info.Site;
                string addValue = info.ServerName + ",";
                if (!siteServerMap.ContainsKey(site))
                    siteServerMap.Add(site, addValue);
                else
                    siteServerMap[site] = siteServerMap[site] + addValue;
            }

            foreach (var siteServers in siteServerMap)
            {
                string servers = siteServers.Value.TrimEnd(',');
                rsSection.WriteString(siteServers.Key, servers);
            }

            using (var helper = DbHelperFactory.CreateReportingDbHelper())
            {
                var parameters = new Dictionary<string, object> { { "DaysSinceLastLogin", 3 } };
                rsSection.WriteInt("SeatsUsed", helper.ExecStorProcedureInt("LicenseCheck", parameters));
            }

            DataSet superUsersDataSet;
            using (var helper = DbHelperFactory.Create())
            {
                superUsersDataSet = helper.ExecStorProcedureDS("SystemSuperUsersSelect");
            }

            List<string> superUserSids = superUsersDataSet.Tables[0].Rows.Cast<DataRow>()
                .Select(dr => new RowReader(dr)).Select(r => r.Data<string>("SystemUserSID")).ToList();

            string[] superUserNames = superUserSids.Select(sid => UserSID.GetFullNameBySid(sid)).ToArray();
            rsSection.WriteString("SuperUsers", string.Join(";", superUserNames));

            SetScriptsFileVersion(rsSection, "EmbargoMSIVersion", "USBPort Security.exe", false);
            SetScriptsFileVersion(rsSection, "SLAgentVersion", "SlAgent.exe", false);
            SetScriptsFileVersion(rsSection, "DAUSLocVersion", "DAUSLoc.dll", false);
            SetScriptsFileVersion(rsSection, "DAUSLocCOMVersion", "DAUSLocCOM.dll", false);
            SetScriptsFileVersion(rsSection, "SLClientVersion", ServiceManager.GetSlServiceExePath(), true);
            SetScriptsFileVersion(rsSection, "DACIVersion", "daci2_x86.dll", false);
        }

        private static void SetScriptsFileVersion(ProfilerINI profilerIni, string key,
            string filename, bool isFullFilename)
        {
            string path = isFullFilename ?
                filename : Path.Combine(PathManager.Create().ScriptsLocation, filename);

            string version = "1.0.0.0";
            if (File.Exists(path))
            {
                var fileVersion = FileVersionInfo.GetVersionInfo(path);

                if (fileVersion.FileVersion != null)
                    version = fileVersion.FileVersion.Trim();
            }
            profilerIni.WriteString(key, version);
        }

        private static string GetCurrentDateTime()
        {
            return DateTime.Now.ToString("dddd, MMMM dd, yyyy - HH:mm:ss");
        }

        private static byte[] GeyKeyData(string keyFilePath)
        {
            using (var keyFile = File.Open(keyFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var length = (int)keyFile.Length;
                var bytes = new byte[length];
                var byteRead = keyFile.Read(bytes, 0, length);
                var encodeKey = Encoding.ASCII.GetString(bytes, 0, byteRead);
                return Convert.FromBase64String(encodeKey);
            }
        }

        private void CreateSignatureFile(string signFilePath, bool isUbmReplication, bool isCbmReplication)
        {
            _activeReplicationInfo.StateString = Constants.SigningScriptFiles;

            var provider = IntPtr.Zero;
            var keyFilePath = Path.Combine(_configurationPath, "slsrvmgr.ske");
            Program.Log.Info(string.Format("Replication. Key file: \"{0}\"", keyFilePath));

            byte[] key = GeyKeyData(keyFilePath);

            try
            {
                if (!Crypto.CryptAcquireContext(ref provider, null, Crypto.Provider, Crypto.Type,
                                                Crypto.CRYPT_VERIFYCONTEXT))
                    throw new Exception("CryptAcquireContext");

                var sigKey = IntPtr.Zero;

                if (!Crypto.CryptImportKey(provider, key, key.Length, IntPtr.Zero, Crypto.CRYPT_EXPORTABLE, ref sigKey))
                    throw new Exception("CryptImportKey");
                
                FillSignFile(provider, signFilePath, isUbmReplication, isCbmReplication);
            }
            finally
            {
                Crypto.CryptReleaseContext(provider, 0);
            }
        }

        private void FillSignFile(IntPtr provider, string signaturesFilePath, bool isUbmReplication, bool isCbmReplication)
        {
            File.WriteAllText(signaturesFilePath, "");

            var signFiles = new List<FileInfo>();

            if (isUbmReplication)
            {
            var srcDir = new DirectoryInfo(UserReplicationSourcePath);

            signFiles.AddRange(srcDir.GetFiles("*.slp"));
            signFiles.AddRange(srcDir.GetFiles("*.sl"));
            signFiles.AddRange(srcDir.GetFiles("*.sld"));

            var ubmFilesToSign = new[]
                                     {
                                         "slagent.exe",
                                         "slboost.exe",
                                         "slboost.ini",
                                         "slinstall.exe"
                                     }.Select(
                                             ubmFile => new FileInfo(Path.Combine(UserReplicationSourcePath, ubmFile)));

            signFiles.AddRange(ubmFilesToSign);
            }

            if (isCbmReplication)
            {
            var cbmFilesToSign = new[]
                                     {
                                         PathManager.CbmConfigFilename,
                                         "SLLicense.ini",
                                         "daclientinstall.msi",
                                         "dotnetfx.exe",
                                         "netfx64.exe",
                                         "windowsinstaller-kb893803-v2-x86.exe",
                                         "windowsxp-kb898715-x64-enu.exe"
                                         }.Select(
                                             cbmFile =>
                                             new FileInfo(Path.Combine(ComputerReplicationSourcePath, cbmFile)));

            signFiles.AddRange(cbmFilesToSign);
            }

            foreach (var file in signFiles)
            {
                Program.Log.Info(string.Format("Replication. Update sign files \"{0}\"", file.FullName));
                if (!file.Exists)
                    continue;

                IntPtr refHash = IntPtr.Zero;

                if (!Crypto.CryptCreateHash(provider, Crypto.HashAlgorithm, IntPtr.Zero, 0, ref refHash))
                    throw new Exception("CryptCreateHash");

                byte[] buf = ReadAllBytes(file.FullName);
                if (buf.Length > 0)
                {
                    if (!Crypto.CryptHashData(refHash, buf, buf.Length, 0))
                        throw new Exception("CryptHashData");
                }

                byte[] lpSignature = null;
                int dwSignatureSize = 0;
                if (!Crypto.CryptSignHash(refHash, Crypto.AT_SIGNATURE, null, 0, lpSignature, ref dwSignatureSize))
                    throw new Exception("CryptSignHash");

                lpSignature = new byte[dwSignatureSize];

                if (!Crypto.CryptSignHash(refHash, Crypto.AT_SIGNATURE, null, 0, lpSignature, ref dwSignatureSize))
                    throw new Exception("CryptSignHash");

                string encodedSignature = Convert.ToBase64String(lpSignature);

                var signsSect = new ProfilerINI(signaturesFilePath, "ScriptLogic\\DesktopAuthority Signatures");

                signsSect.WriteString(file.Name, encodedSignature);

                if (StringComparer.OrdinalIgnoreCase.Equals(file.Name, PathManager.CbmConfigFilename))
                {
                    ProceccCbmConfigSignature(signaturesFilePath, encodedSignature);
                }
            }
        }

        public static byte[] ReadAllBytes(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var buffer = new byte[8192];

                using (var tmpStream = new MemoryStream())
                {
                    int bytesRead;
                    while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        tmpStream.Write(buffer, 0, bytesRead);
                    }

                    return tmpStream.ToArray();
                }
            }
        }

        private void ProceccCbmConfigSignature(string signaturesFile, string signature)
        {
            Program.Log.Info(string.Format("Replication. Update signature file: \"{0}\"", signaturesFile));
            const string cbmSectionName = @"ScriptLogic\DesktopAuthority Cached CBM Signatures";
            var profilerIni = new ProfilerINI(signaturesFile, cbmSectionName);
            profilerIni.WriteString(GetCbmConfigVersion(), signature);
            List<string[]> sectionValues;
            profilerIni.ReadAllSectionLineValues(cbmSectionName, out sectionValues, new[] { '=' });
            if (sectionValues.Count > 1 && sectionValues[0].Length > 0)
            {
                profilerIni.WriteString(sectionValues[0][0], null);
            }
        }

        private string GetCbmConfigVersion()
        {
            var profilerIni = new ProfilerINI(Path.Combine(ComputerReplicationSourcePath,
                "ComputerConfiguration.ini"), "Versions");
            return profilerIni.ReadString("CBMConfigVersion");
        }

        private void PerformReplication()
        {
            Program.Log.Info("Replication. PerformReplication()");

            _activeReplicationInfo.StateString = Constants.PerformingReplication;

            _workUnits.Clear();

            if (_replicateServers.Any(
                s => s.IsCbmReplicationTarget.HasValue && s.IsCbmReplicationTarget.Value))
            {
                CreateDaciReplicateThread();
            }

            _serverManager.DataStorage.ResetReplicationFilesCache();

            var files = new Dictionary<bool, List<ReplicationFileBase>>
                            {
                                {false, _serverManager.DataStorage.GetReplicationFiles(false)},
                                {true, _serverManager.DataStorage.GetReplicationFiles(true)}
                            };

            CreateReplicateDirectoryThreads(files);

            var sw = new Stopwatch();
            sw.Start();
            
            using (var threadPool = new ThreadPool(_workUnits))
            {
                threadPool.PerformWork();
            }
            sw.Stop();

            ExecutionTime = (int) sw.ElapsedMilliseconds/1000;
        }

        private void CreateReplicateDirectoryThreads(Dictionary<bool, List<ReplicationFileBase>> replicationFiles)
        {
            Program.Log.Info("Replication. CreateReplicateDirectoryThreads()");

            foreach (var target in GetReplicationTargets(replicationFiles[false].Count, replicationFiles[true].Count))
            {
                var work = new ReplicationWork(
                    replicationFiles[target.IsCbm],
                    target.IsCbm, target.Server, target.Source, target.Target,
                    ForceUpdate, ServerOptions.IsOverwriteReadOnlyFiles);

                work.ExecInfo.Position = 0;
                work.ExecInfo.FullServerName = string.Concat(target.Server.DomainName, "\\", target.Server.ServerName);
                work.ExecInfo.Target = target.Target;
                work.ExecInfo.State = "Verification...";
                work.ExecInfo.ExecutionState = ExecutionInfo.ExecutionInfoState.InProgress;

                _workUnits.Add(work);
                _activeReplicationInfo.ProgressInfo.Add(work.ExecInfo);

                Program.Log.Info(string.Format("Replication. Preparing data before coping files (\"{0}\")", target.Target));
            }
        }

        private IEnumerable<ReplicationTarget> GetReplicationTargets(int userFilesCount, int computerFilesCount)
        {
            Program.Log.Info("Replication. GetReplicationTargets");

            var result = new List<ReplicationTarget>();

            _serverManager.FillReplicationInfo(_replicateServers);

            foreach (var info in _replicateServers)
            {
                if (info.IsUbmReplicationTarget == true)
                {
                    result.Add(new ReplicationTarget(false, info,
                                   ServerOptions.UserReplicationSourcePath,
                                   string.Format("\\\\{0}\\{1}", info.ServerName, info.UBMReplicationFolder),
                                   userFilesCount, false));
                }
                if (info.IsCbmReplicationTarget == true)
                {
                    result.Add(new ReplicationTarget(true, info,
                                   ServerOptions.ComputerReplicationSourcePath,
                                   string.Format("\\\\{0}\\{1}", info.ServerName, info.CBMReplicationFolder),
                                   computerFilesCount, true));
                }
            }

            result.ForEach(t => t.Target = new ValueProcessor(PathManager.Create().SlMacrosIniPath, null,
                DomainControllerCache.Instance.GetDomainDnsName(t.Server.DomainName)).Process(t.Target));

            return result;
        }

        internal void ClearReplicationInfo()
        {
            _activeReplicationInfo = new ReplicationInfo();
        }

        internal void ClearOdsReplicationInfo()
        {
            _activeOdsReplicationInfo = new OdsReplicationInfo();
        }

        #region Daci Replication

        private void CreateDaciReplicateThread()
        {
            if (_isGpoDeploymentEnabled)
            {
                _workUnits.Add(new WorkItem { Action = ReplcateDaci });
            }
        }

        private void ReplcateDaci()
        {
            if (DacSettings.GetPerformFullDaciReplication())
            {
                Program.Log.Info("DACI replicate: Start GPOManager().Replicate(...)");
                new GPOManager().Replicate(ComputerReplicationSourcePath);
                DacSettings.SetPerformFullDaciReplication(false);
                Program.Log.Info("DACI replicate: End GPOManager().Replicate(...)");
            }
            else
            {
                Program.Log.Info("DACI replicate: Start GPOManager().CheckReplication()");
                new GPOManager().CheckReplication();
                Program.Log.Info("DACI replicate: End  GPOManager().CheckReplication()");
            }
        }

        #endregion

        #region Logging

        private void InitializeLog()
        {
            _log = new ReplicationLog
            {
                Initiator = _activeReplicationInfo.User,
                StartTime = DateTime.Now
            };

            Messages.Add(string.Format("{0} Replication Summary", ProductInfo.ProductName));
            Messages.Add(DateTime.Now.ToString("dddd, MMMM dd, yyyy - HH:mm:ss"));
            Messages.Add(string.Format("Username: {0}", _activeReplicationInfo.User.FullName));
            Messages.Add(string.Empty);

            Messages.Add(string.Format("UBM Replication Source: {0}", UserReplicationSourcePath));
            Messages.Add(string.Format("CBM Replication Source: {0}", ComputerReplicationSourcePath));
            Messages.Add(string.Empty);
        }

        private void FinalizeLog()
        {
            foreach (ExecutionInfo info in _activeReplicationInfo.ProgressInfo)
            {
                Messages.AddRange(info.Messages);
                Messages.Add(string.Empty);
            }

            Messages.Add(string.Format("Total time to replicate all files to all targets: {0}",
                ExecutionTime > 1 ? string.Format("{0} seconds.", ExecutionTime) : "< 1 second."));

            int errorCount = _activeReplicationInfo.ProgressInfo.Sum(i => i.ErrorCount);
            if (errorCount == 0)
            {
                Messages.Add("The replication process completed successfully!");
            }
            else
            {
                Messages.Add(string.Format("The replication process encountered {0}",
                    errorCount == 1 ? "1 error." : string.Format("{0} errors.", errorCount)));
            }
        }

        private void SaveLog()
        {
            _log.EndTime = DateTime.Now;
            try
            {
                SaveLogToFile();
                SaveLogToDb();
            }
            catch (Exception ex)
            {
                Program.Log.Error("Unable to save replication log", ex);
            }
        }

        private static void SaveLogToDb()
        {
            Program.Log.Info("Replication. Save Log to DB");
            var provider = new ReplicationLogProvider();
            provider.SaveLog(_log);
        }

        private void SaveLogToFile()
        {
            string logFilename = Path.Combine(_configurationPath, "SLRepl.log");
            File.WriteAllLines(logFilename, Messages.ToArray());
        }

        #endregion

        #region Updating replication state

        private void UpdateReplicationState()
        {
            Program.Log.Info("Replication. UpdateReplicationState()");
            var state = _activeReplicationInfo.IsSuccessful ?
                ReplicationState.Replicated : ReplicationState.NotReplicated;

            _activeReplicationInfo.StateString = Constants.UpdateReplicationState;

            UpdateReplicationStatus(state, _startedTime);
        }

        public static void UpdateReplicationStatus(ReplicationState state, DateTime time)
        {
            using (var helper = DbHelperFactory.Create())
            {
                var parameters = new Dictionary<string, object>();
                parameters["State"] = state;
                parameters["ReplicationTime"] = time;
                helper.ExecStorProcedureInt("DAC_UpdateReplicationState", parameters);
            }
        }

        public static OdsStore GetPublicationSettingsString()
        {
            var strOptions = new OdsDoProvider(_sessionId).GetOdsOptions();

            return string.IsNullOrEmpty(strOptions.CurrentStoreName) 
                ? null 
                : strOptions.Stores.FirstOrDefault(s => s.Name == strOptions.CurrentStoreName);
        }

        #endregion
    }
}
