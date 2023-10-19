using System;
using System.IO;
using System.Timers;
using System.Windows;
using System.Threading;
using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Input;
using System.ComponentModel;
using System.Windows.Interop;
using System.Threading.Tasks;
using System.Windows.Threading;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using FMOD;
using BeatDetectorCSharp;

namespace SekiroFpsUnlockAndMore
{
    public partial class MainWindow
    {
        internal Process _gameProc;
        internal IntPtr _gameHwnd = IntPtr.Zero;
        internal IntPtr _gameAccessHwnd = IntPtr.Zero;
        internal static IntPtr _gameAccessHwndStatic;
        internal long _offset_framelock = 0x0;
        internal long _offset_resolution = 0x0;
        internal long _offset_resolution_default = 0x0;
        internal long _offset_resolution_scaling_fix = 0x0;
        internal long _offset_player_deaths = 0x0;
        internal long _offset_total_kills = 0x0;
        internal long _offset_camera_reset = 0x0;
        internal long _offset_autoloot = 0x0;
        internal long _offset_dragonrot_routine = 0x0;
        internal long _offset_deathpenalties1 = 0x0;
        internal long _offset_deathpenalties2 = 0x0;
        internal long _offset_deathpenalties3 = 0x0;
        internal long _offset_deathscounter_routine = 0x0;
        internal long _offset_timescale = 0x0;
        internal long _offset_timescale_player = 0x0;
        internal long _offset_timescale_player_pointer_start = 0x0;

        internal byte[] _patch_deathpenalties1_enable;
        internal byte[] _patch_deathpenalties2_enable;
        internal byte[] _patch_deathpenalties3_enable;

        internal MemoryCaveGenerator _memoryCaveGenerator;
        internal SettingsService _settingsService;
        internal StatusViewModel _statusViewModel = new StatusViewModel();

        internal readonly DispatcherTimer _dispatcherTimerGameCheck = new DispatcherTimer();
        internal readonly DispatcherTimer _dispatcherTimerFreezeMem = new DispatcherTimer();
        internal readonly BackgroundWorker _bgwScanGame = new BackgroundWorker();
        internal readonly System.Timers.Timer _timerStatsCheck = new System.Timers.Timer();
        internal bool _running = false;
        internal bool _gameInitializing = false;
        internal bool _use_resolution_720 = false;
        internal bool _dataCave_speedfix = false;
        internal bool _dataCave_fovsetting = false;
        internal bool _codeCave_camadjust = false;
        internal bool _codeCave_emblemupgrade = false;
        internal bool _retryAccess = true;
        internal bool _statLoggingEnabled = false;
        internal bool _initialStartup = true;
        internal bool _debugMode = false;
        internal static string _path_logs;
        internal string _path_deathsLog;
        internal string _path_killsLog;
        internal RECT _windowRect;
        internal Size _screenSize;
        internal bool _isLegacyVersion = false;

        internal const string _DATACAVE_SPEEDFIX_POINTER = "speedfixPointer";
        internal const string _DATACAVE_FOV_POINTER = "fovPointer";
        internal const string _CODECAVE_CAMADJUST_PITCH = "camAdjustPitch";
        internal const string _CODECAVE_CAMADJUST_YAW_Z = "camAdjustYawZ";
        internal const string _CODECAVE_CAMADJUST_PITCH_XY = "camAdjustPitchXY";
        internal const string _CODECAVE_CAMADJUST_YAW_XY = "camAdjustYawXY";
        internal const string _CODECAVE_EMBLEM_UPGRADE = "emblemCapUpgrade";


        public MainWindow()
        {
            Window_Loaded();
        }

        /// <summary>
        /// On window loaded.
        /// </summary>
        private void Window_Loaded()
        {
            var mutex = new Mutex(true, "sekiroFpsUnlockAndMore", out bool isNewInstance);
            if (!isNewInstance)
            {
                Environment.Exit(0);
            }
            GC.KeepAlive(mutex);

            try
            {
                HIGHCONTRAST highContrastInfo = new HIGHCONTRAST();
                highContrastInfo.cbSize = Marshal.SizeOf(typeof(HIGHCONTRAST));
                if (SystemParametersInfo(SPI_GETHIGHCONTRAST, (uint)highContrastInfo.cbSize, ref highContrastInfo, 0))
                {
                    if ((highContrastInfo.dwFlags & HCF_HIGHCONTRASTON) == 1)
                    {
                        // high contrast mode is active, remove grid background color and let the OS handle it
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Could not fetch SystemParameters: " + ex.Message);
            }

            _path_logs = Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location) + @"\SekiroFpsUnlockAndMore.log";
            _path_deathsLog = Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location) + @"\DeathCounter.txt";
            _path_killsLog = Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location) + @"\TotalKillsCounter.txt";

            LoadConfiguration();


            bool result = CheckGame();
            if (result)
            {
                Console.WriteLine("scanning game...");
                _bgwScanGame.RunWorkerAsync();
                _dispatcherTimerGameCheck.Stop();
            }

            ReadGame();
            OnReadGameFinish();


            
        }

        public void reloadSelf() {
            bool result = CheckGame();
            if (result)
            {
                Console.WriteLine("scanning game...");
            }

            ReadGame();
            OnReadGameFinish();
            Console.WriteLine("scanned game");

        }


        /// <summary>
        /// Load all saved settings from previous run.
        /// </summary>
        private void LoadConfiguration()
        {
            _settingsService = new SettingsService(Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location) + @"\SekiroFpsUnlockAndMore.xml");
            if (!_settingsService.Load()) return;
        }

        /// <summary>
        /// Save all settings to configuration file.
        /// </summary>
        private void SaveConfiguration()
        {
        }

        /// <summary>
        /// Resets GUI and clears configuration file.
        /// </summary>
        private void ClearConfiguration()
        {
        }

        /// <summary>
        /// Checks if game is running and initializes further functionality.
        /// </summary>
        private bool CheckGame()
        {
            // game process have been found last check and can be read now, aborting
            if (_gameInitializing)
                return true;

            Process[] procList = Process.GetProcessesByName(GameData.PROCESS_NAME);
            if (procList.Length < 1)
                return false;

            if (_running || _offset_framelock != 0x0)
                return false;

            int gameIndex = -1;
            for (int i = 0; i < procList.Length; i++)
            {
                if (procList[i].MainWindowTitle != GameData.PROCESS_TITLE || !procList[i].MainModule.FileVersionInfo.FileDescription.Contains(GameData.PROCESS_DESCRIPTION))
                    continue;
                gameIndex = i;
                break;
            }
            if (gameIndex < 0)
            {
                Console.WriteLine("no valid game process found...");
                Console.WriteLine("no valid game process found...");
                for (int j = 0; j < procList.Length; j++)
                {
                    Console.WriteLine(string.Format("\tProcess #{0}: '{1}' | ({2})", j, procList[j].MainModule.FileName, procList[j].MainModule.FileVersionInfo.FileName));
                    Console.WriteLine(string.Format("\tDescription #{0}: {1} | {2} | {3}", j, procList[j].MainWindowTitle, procList[j].MainModule.FileVersionInfo.CompanyName, procList[j].MainModule.FileVersionInfo.FileDescription));
                    Console.WriteLine(string.Format("\tData #{0}: {1} | {2} | {3} | {4} | {5}", j, procList[j].MainModule.FileVersionInfo.FileVersion, procList[j].MainModule.ModuleMemorySize, procList[j].StartTime, procList[j].Responding, procList[j].HasExited));
                }
                return false;
            }

            _gameProc = procList[gameIndex];
            _gameHwnd = procList[gameIndex].MainWindowHandle;
            _gameAccessHwnd = OpenProcess(PROCESS_ALL_ACCESS, false, (uint)procList[gameIndex].Id);
            _gameAccessHwndStatic = _gameAccessHwnd;
            if (_gameHwnd == IntPtr.Zero || _gameAccessHwnd == IntPtr.Zero || _gameProc.MainModule.BaseAddress == IntPtr.Zero)
            {
                Console.WriteLine("no access to game...");
                Console.WriteLine("hWnd: " + _gameHwnd.ToString("X"));
                Console.WriteLine("Access hWnd: " + _gameAccessHwnd.ToString("X"));
                Console.WriteLine("BaseAddress: " + procList[gameIndex].MainModule.BaseAddress.ToString("X"));
                if (!_retryAccess)
                {
                    Console.WriteLine("no access to game...");
                    _dispatcherTimerGameCheck.Stop();
                    return false;
                }
                _gameHwnd = IntPtr.Zero;
                if (_gameAccessHwnd != IntPtr.Zero)
                {
                    CloseHandle(_gameAccessHwnd);
                    _gameAccessHwnd = IntPtr.Zero;
                    _gameAccessHwndStatic = IntPtr.Zero;
                }
                Console.WriteLine("retrying...");
                _retryAccess = false;
                return false;
            }

            string gameFileVersion = FileVersionInfo.GetVersionInfo(procList[0].MainModule.FileName).FileVersion;
            if (gameFileVersion != GameData.PROCESS_EXE_VERSION)
            {
                if (Array.IndexOf(GameData.PROCESS_EXE_VERSION_SUPPORTED_LEGACY, gameFileVersion) < 0)
                {
                    if (!_settingsService.ApplicationSettings.gameVersionNotify)
                    {
                        MessageBox.Show(string.Format("Unknown game version '{0}'.\nSome functions might not work properly or even crash the game. " +
                                    "Check for updates on this utility regularly following the link at the bottom.", gameFileVersion), "Sekiro FPS Unlocker and more", MessageBoxButton.OK, MessageBoxImage.Warning);
                        ClearConfiguration();
                        _settingsService.ApplicationSettings.gameVersionNotify = true;
                        SaveConfiguration();
                    }
                }
                else
                {
                    _isLegacyVersion = true;
                    _settingsService.ApplicationSettings.gameVersionNotify = false;
                }
            }
            else
                _settingsService.ApplicationSettings.gameVersionNotify = false;

            // give the game some time to initialize
            _gameInitializing = true;
            Console.WriteLine("game initializing...");
            return false;
        }

        /// <summary>
        /// Read all game offsets and pointer (external).
        /// </summary>
        private void ReadGame()
        {
            PatternScan patternScan = new PatternScan(_gameAccessHwnd, _gameProc.MainModule);
            _memoryCaveGenerator = new MemoryCaveGenerator(_gameAccessHwnd, _gameProc.MainModule.BaseAddress.ToInt64());

            _offset_framelock = patternScan.FindPattern(GameData.PATTERN_FRAMELOCK) + GameData.PATTERN_FRAMELOCK_OFFSET;
            Console.WriteLine("fFrameTick found at: 0x" + _offset_framelock.ToString("X"));
            if (!IsValidAddress(_offset_framelock))
            {
                _offset_framelock = patternScan.FindPattern(GameData.PATTERN_FRAMELOCK_FUZZY) + GameData.PATTERN_FRAMELOCK_FUZZY_OFFSET;
                Console.WriteLine("2. fFrameTick found at: 0x" + _offset_framelock.ToString("X"));
            }
            if (!IsValidAddress(_offset_framelock))
                _offset_framelock = 0x0;

            long lpSpeedFixPointer = patternScan.FindPattern(GameData.PATTERN_FRAMELOCK_SPEED_FIX) + GameData.PATTERN_FRAMELOCK_SPEED_FIX_OFFSET;
            Console.WriteLine("lpSpeedFixPointer at: 0x" + lpSpeedFixPointer.ToString("X"));
            if (IsValidAddress(lpSpeedFixPointer))
            {
                if (_memoryCaveGenerator.CreateNewDataCave(_DATACAVE_SPEEDFIX_POINTER, lpSpeedFixPointer, BitConverter.GetBytes(GameData.PATCH_FRAMELOCK_SPEED_FIX_DEFAULT_VALUE), PointerStyle.dwRelative))
                    _dataCave_speedfix = true;
                Console.WriteLine("lpSpeedFixPointer data cave at: 0x" + _memoryCaveGenerator.GetDataCaveAddressByName(_DATACAVE_SPEEDFIX_POINTER).ToString("X"));
            }

            _offset_resolution_default = patternScan.FindPattern(_use_resolution_720 ? GameData.PATTERN_RESOLUTION_DEFAULT_720 : GameData.PATTERN_RESOLUTION_DEFAULT);
            Console.WriteLine("default resolution found at: 0x" + _offset_resolution_default.ToString("X"));
            if (!IsValidAddress(_offset_resolution_default))
                _offset_resolution_default = 0x0;

            _offset_resolution_scaling_fix = patternScan.FindPattern(GameData.PATTERN_RESOLUTION_SCALING_FIX);
            Console.WriteLine("scaling fix found at: 0x" + _offset_resolution_scaling_fix.ToString("X"));
            if (!IsValidAddress(_offset_resolution_scaling_fix))
                _offset_resolution_scaling_fix = 0x0;

            long ref_lpCurrentResolutionWidth = patternScan.FindPattern(GameData.PATTERN_RESOLUTION_POINTER) + GameData.PATTERN_RESOLUTION_POINTER_OFFSET;
            Console.WriteLine("ref_lpCurrentResolutionWidth found at: 0x" + ref_lpCurrentResolutionWidth.ToString("X"));
            if (IsValidAddress(ref_lpCurrentResolutionWidth))
            {
                _offset_resolution = DereferenceStaticX64Pointer(_gameAccessHwnd, ref_lpCurrentResolutionWidth, GameData.PATTERN_RESOLUTION_POINTER_INSTRUCTION_LENGTH);
                Console.WriteLine("lpCurrentResolutionWidth at: 0x" + _offset_resolution.ToString("X"));
                if (!IsValidAddress(_offset_resolution))
                    _offset_resolution = 0x0;
            }

            long lpFovPointer = patternScan.FindPattern(GameData.PATTERN_FOVSETTING) + GameData.PATTERN_FOVSETTING_OFFSET;
            Console.WriteLine("lpFovPointer found at: 0x" + lpFovPointer.ToString("X"));
            if (IsValidAddress(lpFovPointer))
            {
                if (_memoryCaveGenerator.CreateNewDataCave(_DATACAVE_FOV_POINTER, lpFovPointer, BitConverter.GetBytes(GameData.PATCH_FOVSETTING_DISABLE), PointerStyle.dwRelative))
                    _dataCave_fovsetting = true;
                Console.WriteLine("lpFovPointer data cave at: 0x" + _memoryCaveGenerator.GetDataCaveAddressByName(_DATACAVE_FOV_POINTER).ToString("X"));
            }

            long ref_lpPlayerStatsRelated = patternScan.FindPattern(GameData.PATTERN_PLAYER_DEATHS) + GameData.PATTERN_PLAYER_DEATHS_OFFSET;
            Console.WriteLine("ref_lpPlayerStatsRelated found at: 0x" + ref_lpPlayerStatsRelated.ToString("X"));
            if (IsValidAddress(ref_lpPlayerStatsRelated))
            {
                long lpPlayerStatsRelated = DereferenceStaticX64Pointer(_gameAccessHwndStatic, ref_lpPlayerStatsRelated, GameData.PATTERN_PLAYER_DEATHS_INSTRUCTION_LENGTH);
                Console.WriteLine("lpPlayerStatsRelated found at: 0x" + lpPlayerStatsRelated.ToString("X"));
                if (IsValidAddress(lpPlayerStatsRelated))
                {
                    int dwPlayerStatsToDeathsOffset = Read<Int32>(_gameAccessHwndStatic, ref_lpPlayerStatsRelated + GameData.PATTERN_PLAYER_DEATHS_POINTER_OFFSET_OFFSET);
                    Console.WriteLine("offset pPlayerStats->iPlayerDeaths found : 0x" + dwPlayerStatsToDeathsOffset.ToString("X"));

                    if (dwPlayerStatsToDeathsOffset > 0)
                        _offset_player_deaths = Read<Int64>(_gameAccessHwndStatic, lpPlayerStatsRelated) + dwPlayerStatsToDeathsOffset;
                    Console.WriteLine("iPlayerDeaths found at: 0x" + _offset_player_deaths.ToString("X"));
                }
            }
            if (!IsValidAddress(_offset_player_deaths))
                _offset_player_deaths = 0x0;

            long ref_lpTotalKills = patternScan.FindPattern(GameData.PATTERN_TOTAL_KILLS) + GameData.PATTERN_TOTAL_KILLS_OFFSET;
            Console.WriteLine("ref_lpTotalKills found at: 0x" + ref_lpTotalKills.ToString("X"));
            if (IsValidAddress(ref_lpTotalKills))
            {
                long lpPlayerStatsRelatedKills1 = DereferenceStaticX64Pointer(_gameAccessHwndStatic, ref_lpTotalKills, GameData.PATTERN_TOTAL_KILLS_INSTRUCTION_LENGTH);
                Console.WriteLine("lpPlayerStatsRelatedKills found at: 0x" + lpPlayerStatsRelatedKills1.ToString("X"));
                if (IsValidAddress(lpPlayerStatsRelatedKills1))
                {
                    long lpPlayerStructRelatedKills2 = Read<Int64>(_gameAccessHwndStatic, lpPlayerStatsRelatedKills1) + GameData.PATTERN_TOTAL_KILLS_POINTER1_OFFSET;
                    Console.WriteLine("lpPlayerStructRelatedKills2 found at: 0x" + lpPlayerStructRelatedKills2.ToString("X"));
                    if (IsValidAddress(lpPlayerStructRelatedKills2))
                    {
                        _offset_total_kills = Read<Int64>(_gameAccessHwndStatic, lpPlayerStructRelatedKills2) + GameData.PATTERN_TOTAL_KILLS_POINTER2_OFFSET;
                        Console.WriteLine("iTotalKills found at: 0x" + _offset_total_kills.ToString("X"));
                    }
                }
            }
            if (!IsValidAddress(_offset_total_kills))
                _offset_total_kills = 0x0;

            _offset_autoloot = patternScan.FindPattern(GameData.PATTERN_AUTOLOOT) + GameData.PATTERN_AUTOLOOT_OFFSET;
            Console.WriteLine("lpAutoLoot found at: 0x" + _offset_autoloot.ToString("X"));
            if (!IsValidAddress(_offset_autoloot))
                _offset_autoloot = 0x0;

            long lpCamAdjustPitch = patternScan.FindPattern(GameData.PATTERN_CAMADJUST_PITCH);
            long lpCamAdjustYawZ = patternScan.FindPattern(GameData.PATTERN_CAMADJUST_YAW_Z) + GameData.PATTERN_CAMADJUST_YAW_Z_OFFSET;
            long lpCamAdjustPitchXY = patternScan.FindPattern(GameData.PATTERN_CAMADJUST_PITCH_XY);
            long lpCamAdjustYawXY = patternScan.FindPattern(GameData.PATTERN_CAMADJUST_YAW_XY) + GameData.PATTERN_CAMADJUST_YAW_XY_OFFSET;
            Console.WriteLine("lpCamAdjustPitch found at: 0x" + lpCamAdjustPitch.ToString("X"));
            Console.WriteLine("lpCamAdjustYawZ found at: 0x" + lpCamAdjustYawZ.ToString("X"));
            Console.WriteLine("lpCamAdjustPitchXY found at: 0x" + lpCamAdjustPitchXY.ToString("X"));
            Console.WriteLine("lpCamAdjustYawXY found at: 0x" + lpCamAdjustYawXY.ToString("X"));
            if (IsValidAddress(lpCamAdjustPitch) && IsValidAddress(lpCamAdjustYawZ) && IsValidAddress(lpCamAdjustPitchXY) && IsValidAddress(lpCamAdjustYawXY))
            {
                List<bool> results = new List<bool>
                {
                    _memoryCaveGenerator.CreateNewCodeCave(_CODECAVE_CAMADJUST_PITCH, lpCamAdjustPitch, GameData.INJECT_CAMADJUST_PITCH_OVERWRITE_LENGTH, GameData.INJECT_CAMADJUST_PITCH_SHELLCODE),
                    _memoryCaveGenerator.CreateNewCodeCave(_CODECAVE_CAMADJUST_YAW_Z, lpCamAdjustYawZ, GameData.INJECT_CAMADJUST_YAW_Z_OVERWRITE_LENGTH, GameData.INJECT_CAMADJUST_YAW_Z_SHELLCODE),
                    _memoryCaveGenerator.CreateNewCodeCave(_CODECAVE_CAMADJUST_PITCH_XY, lpCamAdjustPitchXY, GameData.INJECT_CAMADJUST_PITCH_XY_OVERWRITE_LENGTH, GameData.INJECT_CAMADJUST_PITCH_XY_SHELLCODE),
                    _memoryCaveGenerator.CreateNewCodeCave(_CODECAVE_CAMADJUST_YAW_XY, lpCamAdjustYawXY, GameData.INJECT_CAMADJUST_YAW_XY_OVERWRITE_LENGTH, GameData.INJECT_CAMADJUST_YAW_XY_SHELLCODE)
                };
                Console.WriteLine("lpCamAdjustPitch code cave at: 0x" + _memoryCaveGenerator.GetCodeCaveAddressByName(_CODECAVE_CAMADJUST_PITCH).ToString("X"));
                Console.WriteLine("lpCamAdjustYawZ code cave at: 0x" + _memoryCaveGenerator.GetCodeCaveAddressByName(_CODECAVE_CAMADJUST_YAW_Z).ToString("X"));
                Console.WriteLine("lpCamAdjustPitchXY code cave at: 0x" + _memoryCaveGenerator.GetCodeCaveAddressByName(_CODECAVE_CAMADJUST_PITCH_XY).ToString("X"));
                Console.WriteLine("lpCamAdjustYawXY code cave at: 0x" + _memoryCaveGenerator.GetCodeCaveAddressByName(_CODECAVE_CAMADJUST_YAW_XY).ToString("X"));
                if (results.IndexOf(false) < 0)
                    _codeCave_camadjust = true;
            }

            _offset_camera_reset = patternScan.FindPattern(GameData.PATTERN_CAMRESET_LOCKON) + GameData.PATTERN_CAMRESET_LOCKON_OFFSET;
            Console.WriteLine("lpCameraReset found at: 0x" + _offset_camera_reset.ToString("X"));
            if (!IsValidAddress(_offset_camera_reset))
                _offset_camera_reset = 0x0;

            _offset_dragonrot_routine = patternScan.FindPattern(GameData.PATTERN_DRAGONROT_EFFECT) + GameData.PATTERN_DRAGONROT_EFFECT_OFFSET;
            Console.WriteLine("lpDragonRot found at: 0x" + _offset_dragonrot_routine.ToString("X"));
            if (!IsValidAddress(_offset_dragonrot_routine))
                _offset_dragonrot_routine = 0x0;

            _offset_deathpenalties1 = patternScan.FindPattern(GameData.PATTERN_DEATHPENALTIES1) + GameData.PATTERN_DEATHPENALTIES1_OFFSET;
            Console.WriteLine("lpDeathPenalties1 found at: 0x" + _offset_deathpenalties1.ToString("X"));
            if (IsValidAddress(_offset_deathpenalties1))
            {
                _patch_deathpenalties1_enable = new byte[GameData.PATCH_DEATHPENALTIES1_INSTRUCTION_LENGTH];
                if (!ReadProcessMemory(_gameAccessHwnd, _offset_deathpenalties1, _patch_deathpenalties1_enable, (ulong)GameData.PATCH_DEATHPENALTIES1_INSTRUCTION_LENGTH, out IntPtr lpNumberOfBytesRead) || lpNumberOfBytesRead.ToInt32() != GameData.PATCH_DEATHPENALTIES1_INSTRUCTION_LENGTH)
                    _patch_deathpenalties1_enable = null;
                else
                    Console.WriteLine("deathPenalties1 original instruction set: " + BitConverter.ToString(_patch_deathpenalties1_enable).Replace('-', ' '));
                if (_patch_deathpenalties1_enable != null)
                {
                    if (!_isLegacyVersion)
                        _offset_deathpenalties2 = patternScan.FindPattern(GameData.PATTERN_DEATHPENALTIES2) + GameData.PATTERN_DEATHPENALTIES2_OFFSET;
                    else
                        _offset_deathpenalties2 = patternScan.FindPattern(GameData.PATTERN_DEATHPENALTIES2_LEGACY) + GameData.PATTERN_DEATHPENALTIES2_OFFSET_LEGACY;
                    Console.WriteLine("lpDeathPenalties2 found at: 0x" + _offset_deathpenalties2.ToString("X"));
                    if (IsValidAddress(_offset_deathpenalties2))
                    {
                        ulong instrLength = (ulong)GameData.PATCH_DEATHPENALTIES2_INSTRUCTION_LENGTH;
                        if (!_isLegacyVersion)
                            _patch_deathpenalties2_enable = new byte[GameData.PATCH_DEATHPENALTIES2_INSTRUCTION_LENGTH];
                        else
                        {
                            _patch_deathpenalties2_enable = new byte[GameData.PATCH_DEATHPENALTIES2_INSTRUCTION_LENGTH_LEGACY];
                            instrLength = (ulong)GameData.PATCH_DEATHPENALTIES2_INSTRUCTION_LENGTH_LEGACY;
                        }
                        if (!ReadProcessMemory(_gameAccessHwnd, _offset_deathpenalties2, _patch_deathpenalties2_enable, instrLength, out lpNumberOfBytesRead) || lpNumberOfBytesRead.ToInt32() != (long)instrLength)
                            _patch_deathpenalties2_enable = null;
                        else
                        {
                            Console.WriteLine("deathPenalties2 original instruction set: " + BitConverter.ToString(_patch_deathpenalties2_enable).Replace('-', ' '));
                            if (!_isLegacyVersion)
                            {
                                _offset_deathpenalties3 = _offset_deathpenalties2 + GameData.PATTERN_DEATHPENALTIES3_OFFSET;
                                _patch_deathpenalties3_enable = new byte[GameData.PATCH_DEATHPENALTIES3_INSTRUCTION_LENGTH];
                                if (!ReadProcessMemory(_gameAccessHwnd, _offset_deathpenalties3, _patch_deathpenalties3_enable, (ulong)GameData.PATCH_DEATHPENALTIES3_INSTRUCTION_LENGTH, out lpNumberOfBytesRead) || lpNumberOfBytesRead.ToInt32() != GameData.PATCH_DEATHPENALTIES3_INSTRUCTION_LENGTH)
                                    _patch_deathpenalties2_enable = null;
                                else
                                    Console.WriteLine("deathPenalties3 original instruction set: " + BitConverter.ToString(_patch_deathpenalties3_enable).Replace('-', ' '));
                            }
                        }
                    }
                    else
                        _offset_deathpenalties2 = 0x0;
                }
            }
            if (_offset_deathpenalties2 == 0x0 || _patch_deathpenalties2_enable == null)
            {
                _offset_deathpenalties1 = 0x0;
                _offset_deathpenalties2 = 0x0;
                _offset_deathpenalties3 = 0x0;
                _patch_deathpenalties1_enable = null;
                _patch_deathpenalties2_enable = null;
                _patch_deathpenalties3_enable = null;
            }

            if (_settingsService.ApplicationSettings.hiddenDPs == ZUH_HIDDEN_DP)
            {
                _offset_deathscounter_routine = patternScan.FindPattern(GameData.PATTERN_DEATHSCOUNTER) + GameData.PATTERN_DEATHSCOUNTER_OFFSET;
                Console.WriteLine("lpDeathsCounter found at: 0x" + _offset_deathscounter_routine.ToString("X"));
                if (!IsValidAddress(_offset_deathscounter_routine))
                    _offset_deathscounter_routine = 0x0;
            }

            long lpSkill4OnUpgrade = patternScan.FindPattern(GameData.PATTERN_EMBLEMUPGRADE) + GameData.PATTERN_EMBLEMUPGRADE_OFFSET;
            Console.WriteLine("lpSkill4OnUpgrade found at: 0x" + lpSkill4OnUpgrade.ToString("X"));
            if (IsValidAddress(lpSkill4OnUpgrade))
            {
                if (_memoryCaveGenerator.CreateNewCodeCave(_CODECAVE_EMBLEM_UPGRADE, lpSkill4OnUpgrade, GameData.INJECT_EMBLEMUPGRADE_OVERWRITE_LENGTH, GameData.INJECT_EMBLEMUPGRADE_SHELLCODE))
                    _codeCave_emblemupgrade = true;
                Console.WriteLine("lpSkill4OnUpgrade code cave at: 0x" + _memoryCaveGenerator.GetCodeCaveAddressByName(_CODECAVE_EMBLEM_UPGRADE).ToString("X"));
            }

            long ref_lpTimeRelated = patternScan.FindPattern(GameData.PATTERN_TIMESCALE);
            Console.WriteLine("ref_lpTimeRelated found at: 0x" + ref_lpTimeRelated.ToString("X"));
            if (IsValidAddress(ref_lpTimeRelated))
            {
                long lpTimescaleManager = DereferenceStaticX64Pointer(_gameAccessHwndStatic, ref_lpTimeRelated, GameData.PATTERN_TIMESCALE_INSTRUCTION_LENGTH);
                Console.WriteLine("lpTimescaleManager found at: 0x" + lpTimescaleManager.ToString("X"));
                if (IsValidAddress(lpTimescaleManager))
                {
                    _offset_timescale = Read<Int64>(_gameAccessHwndStatic, lpTimescaleManager) + Read<Int32>(_gameAccessHwndStatic, ref_lpTimeRelated + GameData.PATTERN_TIMESCALE_POINTER_OFFSET_OFFSET);
                    Console.WriteLine("fTimescale found at: 0x" + _offset_timescale.ToString("X"));
                    if (!IsValidAddress(_offset_timescale))
                        _offset_timescale = 0x0;
                }
            }

            long lpPlayerStructRelated1 = patternScan.FindPattern(GameData.PATTERN_TIMESCALE_PLAYER);
            Console.WriteLine("lpPlayerStructRelated1 found at: 0x" + lpPlayerStructRelated1.ToString("X"));

            if (IsValidAddress(lpPlayerStructRelated1))
            {
                long lpPlayerStructRelated2 = DereferenceStaticX64Pointer(_gameAccessHwndStatic, lpPlayerStructRelated1, GameData.PATTERN_TIMESCALE_PLAYER_INSTRUCTION_LENGTH);
                Console.WriteLine("lpPlayerStructRelated2 found at: 0x" + lpPlayerStructRelated2.ToString("X"));
                if (IsValidAddress(lpPlayerStructRelated2))
                {
                    _offset_timescale_player_pointer_start = lpPlayerStructRelated2;
                    long lpPlayerStructRelated3 = Read<Int64>(_gameAccessHwndStatic, lpPlayerStructRelated2) + GameData.PATTERN_TIMESCALE_POINTER2_OFFSET;
                    Console.WriteLine("lpPlayerStructRelated3 found at: 0x" + lpPlayerStructRelated3.ToString("X"));
                    if (IsValidAddress(lpPlayerStructRelated3))
                    {
                        long lpPlayerStructRelated4 = Read<Int64>(_gameAccessHwndStatic, lpPlayerStructRelated3) + GameData.PATTERN_TIMESCALE_POINTER3_OFFSET;
                        Console.WriteLine("lpPlayerStructRelated4 found at: 0x" + lpPlayerStructRelated4.ToString("X"));
                        if (IsValidAddress(lpPlayerStructRelated4))
                        {
                            long lpPlayerStructRelated5 = Read<Int64>(_gameAccessHwndStatic, lpPlayerStructRelated4) + GameData.PATTERN_TIMESCALE_POINTER4_OFFSET;
                            Console.WriteLine("lpPlayerStructRelated5 found at: 0x" + lpPlayerStructRelated5.ToString("X"));
                            if (IsValidAddress(lpPlayerStructRelated5))
                            {
                                _offset_timescale_player = Read<Int64>(_gameAccessHwndStatic, lpPlayerStructRelated5) + GameData.PATTERN_TIMESCALE_POINTER5_OFFSET;
                                Console.WriteLine("fTimescalePlayer found at: 0x" + _offset_timescale_player.ToString("X"));
                                if (!IsValidAddress(_offset_timescale_player))
                                    _offset_timescale_player = 0x0;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// All game data has been read.
        /// </summary>
        private void OnReadGameFinish()
        {
            if (_offset_framelock == 0x0)
            {
                Console.WriteLine("frame tick not found...");
                Console.WriteLine("frame tick not found...");
            }

            if (!_dataCave_speedfix)
            {
                Console.WriteLine("could not create speed fix table...");
                Console.WriteLine("could not create speed fix table...");
            }

            if (_offset_resolution_default == 0x0)
            {
                Console.WriteLine("default resolution not found...");
                Console.WriteLine("default resolution not found...");
            }
            if (_offset_resolution_scaling_fix == 0x0)
            {
                Console.WriteLine("scaling fix not found...");
                Console.WriteLine("scaling fix not found...");
            }
            if (_offset_resolution == 0x0)
            {
                Console.WriteLine("current resolution not found...");
                Console.WriteLine("current resolution not found...");
            }

            if (!_dataCave_fovsetting)
            {
                Console.WriteLine("could not create FOV table...");
                Console.WriteLine("could not create FOV table...");
            }


            if (_offset_player_deaths == 0x0)
            {
                Console.WriteLine("player deaths not found...");
                Console.WriteLine("player deaths not found...");
            }
            if (_offset_total_kills == 0x0)
            {
                Console.WriteLine("player kills not found...");
                Console.WriteLine("player kills not found...");  
            }
            if (_offset_player_deaths > 0x0 && _offset_total_kills > 0x0)
                _timerStatsCheck.Start();

            if (!_codeCave_camadjust)
            {
                Console.WriteLine("cam adjust not found...");
                Console.WriteLine("cam adjust not found...");
            }

            if (_offset_camera_reset == 0x0)
            {
                Console.WriteLine("camera reset not found...");
                Console.WriteLine("camera reset not found...");
            }

            if (_offset_autoloot == 0x0)
            {
                Console.WriteLine("auto loot not found...");
                Console.WriteLine("auto loot not found...");
            }

            if (_offset_dragonrot_routine == 0x0)
            {
                Console.WriteLine("dragonrot not found...");
                Console.WriteLine("dragonrot not found...");
            }

            if (_offset_deathpenalties2 == 0x0)
            {
                Console.WriteLine("death penalties not found...");
                Console.WriteLine("death penalties not found...");
            }

            if (_offset_deathscounter_routine == 0x0) ;

            if (!_codeCave_emblemupgrade)
            {
                Console.WriteLine("emblem upgrade not found...");
                Console.WriteLine("emblem upgrade not found...");
            }

            if (_offset_timescale == 0x0)
            {
                Console.WriteLine("timescale not found...");
                Console.WriteLine("timescale not found...");
            }
            if (_offset_timescale_player_pointer_start == 0x0)
            {
                Console.WriteLine("player timescale not found...");
                //Console.WriteLine("player timescale not found...");
            }

            _running = true;
            //PatchGame();
            //InjectCamAdjust();
            //InjectEmblemUpgrades();
        }

        /// <summary>
        /// Read and refresh the player speed offset that can change on quick travel or save game loading.
        /// </summary>
        private void ReadPlayerTimescaleOffsets()
        {
            bool valid = false;
            if (_offset_timescale_player_pointer_start > 0)
            {
                long lpPlayerStructRelated3 = Read<Int64>(_gameAccessHwndStatic, _offset_timescale_player_pointer_start) + GameData.PATTERN_TIMESCALE_POINTER2_OFFSET;
                if (IsValidAddress(lpPlayerStructRelated3))
                {
                    long lpPlayerStructRelated4 = Read<Int64>(_gameAccessHwndStatic, lpPlayerStructRelated3) + GameData.PATTERN_TIMESCALE_POINTER3_OFFSET;
                    if (IsValidAddress(lpPlayerStructRelated4))
                    {
                        long lpPlayerStructRelated5 = Read<Int64>(_gameAccessHwndStatic, lpPlayerStructRelated4) + GameData.PATTERN_TIMESCALE_POINTER4_OFFSET;
                        if (IsValidAddress(lpPlayerStructRelated5))
                        {
                            _offset_timescale_player = Read<Int64>(_gameAccessHwndStatic, lpPlayerStructRelated5) + GameData.PATTERN_TIMESCALE_POINTER5_OFFSET;
                            if (IsValidAddress(_offset_timescale_player))
                                valid = true;
                        }
                    }
                }
            }
            if (!valid) _offset_timescale_player = 0x0;
        }

        /// <summary>
        /// Determines whether everything is ready for patching.
        /// </summary>
        /// <returns>True if we can patch game, false otherwise.</returns>
        private bool CanPatchGame()
        {
            if (!_running) return false;
            if (!_gameProc.HasExited) return true;

            _running = false;
            if (_gameAccessHwnd != IntPtr.Zero)
                CloseHandle(_gameAccessHwnd);
            _dispatcherTimerFreezeMem.Stop();
            _timerStatsCheck.Stop();
            _gameProc = null;
            _gameHwnd = IntPtr.Zero;
            _gameAccessHwnd = IntPtr.Zero;
            _gameAccessHwndStatic = IntPtr.Zero;
            _gameInitializing = false;
            _initialStartup = true;
            _offset_framelock = 0x0;
            _dataCave_speedfix = false;
            _offset_resolution = 0x0;
            _offset_resolution_default = 0x0;
            _offset_resolution_scaling_fix = 0x0;
            _dataCave_fovsetting = false;
            _offset_player_deaths = 0x0;
            _offset_total_kills = 0x0;
            _codeCave_camadjust = false;
            _offset_camera_reset = 0x0;
            _offset_dragonrot_routine = 0x0;
            _offset_autoloot = 0x0;
            _offset_deathpenalties1 = 0x0;
            _offset_deathpenalties2 = 0x0;
            _offset_deathscounter_routine = 0x0;
            _codeCave_emblemupgrade = false;
            _offset_timescale = 0x0;
            _offset_timescale_player = 0x0;
            _offset_timescale_player_pointer_start = 0x0;
            _patch_deathpenalties1_enable = null;
            _patch_deathpenalties2_enable = null;
            _memoryCaveGenerator.ClearCaves();
            _memoryCaveGenerator = null;
            Console.WriteLine("waiting for game...");
            _dispatcherTimerGameCheck.Start();

            return false;
        }

        /// <summary>
        /// Patch the game's frame rate lock.
        /// </summary>
        /// <param name="showStatus">Determines if status should be updated from within method, default is true.</param>
        private bool PatchFramelock(bool showStatus = true)
        {
            return true;
        }

        /// <summary>
        /// Patches the game's default resolution.
        /// </summary>
        /// <param name="showStatus">Determines if status should be updated from within method, default is true.</param>
        private bool PatchResolution(bool showStatus = true)
        {
            return true;
        }

        /// <summary>
        /// Patches the game's field of view.
        /// </summary>
        /// <param name="showStatus">Determines if status should be updated from within method, default is true.</param>
        private bool PatchFov(bool showStatus = true)
        {
            return true;
        }

        /// <summary>
        /// Patches the game's window.
        /// </summary>
        /// <param name="showStatus">Determines if status should be updated from within method, default is true.</param>
        private bool PatchWindow(bool showStatus = true)
        {
            return true;
        }

        /// <summary>
        /// Patches the game's camera centering on lock-on.
        /// </summary>
        /// <param name="showStatus">Determines if status should be updated from within method, default is true.</param>
        private bool PatchCamReset(bool showStatus = true)
        {
            return true;
        }

        /// <summary>
        /// Patches the game's auto loot.
        /// </summary>
        /// <param name="showStatus">Determines if status should be updated from within method, default is true.</param>
        private bool PatchAutoloot(bool showStatus = true)
        {
            return true;
        }

        /// <summary>
        /// Patches the game's dragonrot effect on NPCs.
        /// </summary>
        /// <param name="showStatus">Determines if status should be updated from within method, default is true.</param>
        private bool PatchDragonrot(bool showStatus = true)
        {
            return true;
        }

        /// <summary>
        /// Patches the game's death penalties.
        /// </summary>
        /// <param name="showStatus">Determines if status should be updated from within method, default is true.</param>
        private bool PatchDeathPenalty(bool showStatus = true)
        {
            return true;
        }

        /// <summary>
        /// Patches the game's hidden death penalties.
        /// </summary>
        /// <param name="showStatus">Determines if status should be updated from within method, default is true.</param>
        private bool PatchDeathPenaltyHidden(bool showStatus = true)
        {
            return true;
        }

        /// <summary>
        /// Patches game's global speed.
        /// </summary>
        /// <param name="showStatus">Determines if status should be updated from within method, default is true.</param>
        public bool PatchGameSpeed(float speed)
        {
            if (_offset_timescale == 0x0 || !CanPatchGame()) {
                Console.WriteLine("Can't patch!");
                reloadSelf();
                return false; 
            }
            if (true)
            {
                float timeScale = speed;
                if (timeScale < 0.01f)
                    timeScale = 0.0001f;
                WriteBytes(_gameAccessHwndStatic, _offset_timescale, BitConverter.GetBytes(timeScale));
            }
            Console.WriteLine("SekiWorldSpeed Changed!");

            return true;
        }

        /// <summary>
        /// Patches game's player speed.
        /// </summary>
        /// <param name="showStatus">Determines if status should be updated from within method, default is true.</param>
        public bool PatchPlayerSpeed(float speed)
        {
            if (!CanPatchGame())
            {
                Console.WriteLine("Can't patch!");
                reloadSelf();
                return false;
            }
            if (true)
            {
                if (_offset_timescale_player_pointer_start > 0x0) ReadPlayerTimescaleOffsets();
                if (_offset_timescale_player == 0x0)
                {
                    return false;
                }
            }
            if (_offset_timescale_player == 0x0) return false;
            if (true)
            {
                float timeScalePlayer = speed;
                if (timeScalePlayer < 0.01f)
                    timeScalePlayer = 0.0001f;
                WriteBytes(_gameAccessHwndStatic, _offset_timescale_player, BitConverter.GetBytes(timeScalePlayer));
                if (!_dispatcherTimerFreezeMem.IsEnabled) _dispatcherTimerFreezeMem.Start();
                SetModeTag();
            }
            Console.WriteLine("SekiSpeed Changed!");
            return true;
        }

        /// <summary>
        /// Patch up this broken port of a game.
        /// </summary>
        private void PatchGame()
        {
            if (!CanPatchGame()) return;

            List<bool> results = new List<bool>
            {
                PatchGameSpeed(1),
                PatchPlayerSpeed(1)
            };
            if (results.IndexOf(true) > -1)
                Console.WriteLine(DateTime.Now.ToString("HH:mm:ss") + " Game patched!");
            else
                Console.WriteLine(DateTime.Now.ToString("HH:mm:ss") + " Game unpatched!");
            _initialStartup = false;
        }

        /// <summary>
        /// Inject or eject code to control cam adjustment.
        /// </summary>
        private void InjectCamAdjust()
        {
        }

        /// <summary>
        /// Inject or eject code to control emblem upgrades.
        /// </summary>
        private void InjectEmblemUpgrades()
        {
        }

        /// <summary>
        /// Freeze values in memory that can't be patched to require no freezing easily.
        /// </summary>
        private void FreezeMemory(object sender, EventArgs e)
        {
            if (true)
            {
                _dispatcherTimerFreezeMem.Stop();
                return;
            }
        }

        /// <summary>
        /// Reads some hidden stats and outputs them to text files and status bar. Use to display counters on Twitch stream or just look at them and get disappointed.
        /// </summary>
        private void StatsReadTimer(object sender, EventArgs e)
        {
            if (!_running || _gameAccessHwndStatic == IntPtr.Zero || _offset_player_deaths == 0x0 || _offset_total_kills == 0x0) return;
            int playerDeaths = Read<Int32>(_gameAccessHwndStatic, _offset_player_deaths);
            _statusViewModel.Deaths = playerDeaths;
            if (_statLoggingEnabled) LogStatsFile(_path_deathsLog, playerDeaths.ToString());
            int totalKills = Read<Int32>(_gameAccessHwndStatic, _offset_total_kills);
            totalKills -= playerDeaths; // Since this value seems to track every death, including the player
            if (totalKills < 0) totalKills = 0;
            _statusViewModel.Kills = totalKills;
            if (_statLoggingEnabled) LogStatsFile(_path_killsLog, totalKills.ToString());
        }

        /// <summary>
        /// Sets mode according to user settings.
        /// </summary>
        private void SetModeTag()
        {
        }

        /// <summary>
        /// Returns the hexadecimal representation of an IEEE-754 floating point number
        /// </summary>
        /// <param name="input">The floating point number.</param>
        /// <returns>The hexadecimal representation of the input.</returns>
        private static string GetHexRepresentationFromFloat(float input)
        {
            uint f = BitConverter.ToUInt32(BitConverter.GetBytes(input), 0);
            return "0x" + f.ToString("X8");
        }


        /// <summary>
        /// Checks if window is minimized.
        /// </summary>
        /// <param name="hWnd">The main window handle of the window.</param>
        /// <remarks>
        /// Even minimized fullscreen windows have WS_MINIMIZED normal borders and caption set.
        /// </remarks>
        /// <returns>True if window is minimized.</returns>
        private static bool IsMinimized(IntPtr hWnd)
        {
            long wndStyle = GetWindowLongPtr(hWnd, GWL_STYLE).ToInt64();
            if (wndStyle == 0)
                return false;

            return (wndStyle & WS_MINIMIZE) != 0;
        }

        /// <summary>
        /// Checks if window is in fullscreen mode.
        /// </summary>
        /// <param name="hWnd">The main window handle of the window.</param>
        /// <remarks>
        /// Fullscreen windows have WS_EX_TOPMOST flag set.
        /// </remarks>
        /// <returns>True if window is run in fullscreen mode.</returns>
        private static bool IsFullscreen(IntPtr hWnd)
        {
            long wndStyle = GetWindowLongPtr(hWnd, GWL_STYLE).ToInt64();
            long wndExStyle = GetWindowLongPtr(hWnd, GWL_EXSTYLE).ToInt64();
            if (wndStyle == 0 || wndExStyle == 0)
                return false;

            if ((wndExStyle & WS_EX_TOPMOST) == 0)
                return false;
            if ((wndStyle & WS_POPUP) != 0)
                return false;
            if ((wndStyle & WS_CAPTION) != 0)
                return false;
            if ((wndStyle & WS_BORDER) != 0)
                return false;

            return true;
        }

        /// <summary>
        /// Checks if window is in borderless window mode.
        /// </summary>
        /// <param name="hWnd">The main window handle of the window.</param>
        /// <remarks>
        /// Borderless windows have WS_POPUP flag set.
        /// </remarks>
        /// <returns>True if window is run in borderless window mode.</returns>
        private static bool IsBorderless(IntPtr hWnd)
        {
            long wndStyle = GetWindowLongPtr(hWnd, GWL_STYLE).ToInt64();
            if (wndStyle == 0)
                return false;

            if ((wndStyle & WS_POPUP) == 0)
                return false;
            if ((wndStyle & WS_CAPTION) != 0)
                return false;
            if ((wndStyle & WS_BORDER) != 0)
                return false;

            return true;
        }

        /// <summary>
        /// Sets a window to ordinary windowed mode
        /// </summary>
        /// <param name="hWnd">The handle to the window.</param>
        /// <param name="width">The desired window width.</param>
        /// <param name="height">The desired window height.</param>
        /// <param name="posX">The desired X position of the window.</param>
        /// <param name="posY">The desired Y position of the window.</param>
        /// <param name="demoMode">Execute functionality without stealing focus, does not retain client size scaling. FOR DEMONSTRATION ONLY.</param>
        private static void SetWindowWindowed(IntPtr hWnd, int width, int height, int posX, int posY, bool demoMode = false)
        {
            SetWindowLongPtr(hWnd, GWL_STYLE, WS_VISIBLE | WS_CAPTION | WS_BORDER | WS_CLIPSIBLINGS | WS_DLGFRAME | WS_SYSMENU | WS_GROUP | WS_MINIMIZEBOX);
            SetWindowPos(hWnd, HWND_NOTOPMOST, posX, posY, width, height, !demoMode ? SWP_FRAMECHANGED | SWP_SHOWWINDOW : SWP_SHOWWINDOW | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        /// <summary>
        /// Sets a window to borderless windowed mode and moves it to position 0x0.
        /// </summary>
        /// <param name="hWnd">The handle to the window.</param>
        /// <param name="width">The desired window width.</param>
        /// <param name="height">The desired window height.</param>
        /// <param name="posX">The desired X position of the window.</param>
        /// <param name="posY">The desired Y position of the window.</param>
        /// <param name="demoMode">Execute functionality without stealing focus, does not retain client size scaling. FOR DEMONSTRATION ONLY.</param>
        private static void SetWindowBorderless(IntPtr hWnd, int width, int height, int posX, int posY, bool demoMode = false)
        {
            SetWindowLongPtr(hWnd, GWL_STYLE, WS_VISIBLE | WS_POPUP);
            SetWindowPos(hWnd, !demoMode ? HWND_TOP : HWND_NOTOPMOST, posX, posY, width, height, !demoMode ? SWP_FRAMECHANGED | SWP_SHOWWINDOW : SWP_SHOWWINDOW | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        /// <summary>
        /// Checks if an address is valid.
        /// </summary>
        /// <param name="address">The address (the pointer points to).</param>
        /// <returns>True if (pointer points to a) valid address.</returns>
        private static bool IsValidAddress(Int64 address)
        {
            return (address >= 0x10000 && address < 0x000F000000000000);
        }

        /// <summary>
        /// Reads a given type from processes memory using a generic method.
        /// </summary>
        /// <typeparam name="T">The base type to read.</typeparam>
        /// <param name="hProcess">The process handle to read from.</param>
        /// <param name="lpBaseAddress">The address to read from.</param>
        /// <returns>The given base type read from memory.</returns>
        /// <remarks>GCHandle and Marshal are costy.</remarks>
        private static T Read<T>(IntPtr hProcess, Int64 lpBaseAddress)
        {
            byte[] lpBuffer = new byte[Marshal.SizeOf(typeof(T))];
            ReadProcessMemory(hProcess, lpBaseAddress, lpBuffer, (ulong)lpBuffer.Length, out _);
            GCHandle gcHandle = GCHandle.Alloc(lpBuffer, GCHandleType.Pinned);
            T structure = (T)Marshal.PtrToStructure(gcHandle.AddrOfPinnedObject(), typeof(T));
            gcHandle.Free();
            return structure;
        }

        /// <summary>
        /// Writes a given type and value to processes memory using a generic method.
        /// </summary>
        /// <param name="hProcess">The process handle to read from.</param>
        /// <param name="lpBaseAddress">The address to write from.</param>
        /// <param name="bytes">The byte array to write.</param>
        /// <returns>True if successful, false otherwise.</returns>
        private static bool WriteBytes(IntPtr hProcess, Int64 lpBaseAddress, byte[] bytes)
        {
            return WriteProcessMemory(hProcess, lpBaseAddress, bytes, (ulong)bytes.Length, out _);
        }

        /// <summary>
        /// Gets the static offset to the referenced object instead of the offset from the instruction.
        /// </summary>
        /// <param name="hProcess">Handle to the process.</param>
        /// <param name="lpInstructionAddress">The address of the instruction.</param>
        /// <param name="instructionLength">The length of the instruction including the 4 bytes offset.</param>
        /// <remarks>Static pointers in x86-64 are relative offsets from their instruction address.</remarks>
        /// <returns>The static offset from the process to the referenced object.</returns>
        private static Int64 DereferenceStaticX64Pointer(IntPtr hProcess, Int64 lpInstructionAddress, int instructionLength)
        {
            return lpInstructionAddress + Read<Int32>(hProcess, lpInstructionAddress + (instructionLength - 0x04)) + instructionLength;
        }

        /// <summary>
        /// Check whether input is numeric only.
        /// </summary>
        /// <param name="text">The text to check.</param>
        /// <returns>True if input is numeric only.</returns>
        private static bool IsNumericInput(string text)
        {
            return Regex.IsMatch(text, "[^0-9]+");
        }


        /// <summary>
        /// Logs stats values to separate files for use in OBS or similar.
        /// </summary>
        /// <param name="filename">The filepath to the status file.</param>
        /// <param name="msg">The value to write to the text file.</param>
        private void LogStatsFile(string filename, string msg)
        {
            try
            {
                using (StreamWriter writer = new StreamWriter(filename, false))
                {
                    writer.Write(msg);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed writing stats file: " + ex.Message);
                // don't show a messagebox as this will potentially steal focus from game
                //MessageBox.Show("Failed writing stats file: " + ex.Message, "Sekiro Fps Unlock And More");
            }
        }


        private void Numeric_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = IsNumericInput(e.Text);
        }

        private void Numeric_PastingHandler(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(typeof(string)))
            {
                string text = (string)e.DataObject.GetData(typeof(string));
                if (IsNumericInput(text)) e.CancelCommand();
            }
            else e.CancelCommand();
        }


        #region WINAPI

        private const int WM_HOTKEY_MSG_ID = 0x0312;
        private const int MOD_CONTROL = 0x0002;
        private const uint VK_M = 0x004D;
        private const uint VK_P = 0x0050;
        private const uint PROCESS_ALL_ACCESS = 0x001F0FFF;
        private const int GWL_STYLE = -16;
        private const int GWL_EXSTYLE = -20;
        private const uint WS_GROUP = 0x00020000;
        private const uint WS_MINIMIZEBOX = 0x00020000;
        private const uint WS_SYSMENU = 0x00080000;
        private const uint WS_DLGFRAME = 0x00400000;
        private const uint WS_BORDER = 0x00800000;
        private const uint WS_CAPTION = 0x00C00000;
        private const uint WS_CLIPSIBLINGS = 0x04000000;
        private const uint WS_VISIBLE = 0x10000000;
        private const uint WS_MINIMIZE = 0x20000000;
        private const uint WS_POPUP = 0x80000000;
        private const uint WS_EX_TOPMOST = 0x00000008;
        private const int HWND_TOP = 0;
        private const int HWND_NOTOPMOST = -2;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const int ZUH_HIDDEN_DP = 0x7;
        private const uint SPI_GETHIGHCONTRAST = 0x0042;
        private const int HCF_HIGHCONTRASTON = 0x00000001;

        [DllImport("user32.dll")]
        public static extern Boolean RegisterHotKey(IntPtr hWnd, Int32 id, UInt32 fsModifiers, UInt32 vlc);

        [DllImport("user32.dll")]
        public static extern Boolean UnregisterHotKey(IntPtr hWnd, Int32 id);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(
            UInt32 dwDesiredAccess,
            Boolean bInheritHandle,
            UInt32 dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern Boolean CloseHandle(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct HIGHCONTRAST
        {
            public int cbSize;
            public int dwFlags;
            public IntPtr lpszDefaultScheme;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SystemParametersInfo(UInt32 uiAction, UInt32 uiParam, ref HIGHCONTRAST pvParam, UInt32 fWinIni);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, Int32 nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, Int32 nIndex, Int64 dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowPos")]
        public static extern IntPtr SetWindowPos(IntPtr hWnd, Int32 hWndInsertAfter, Int32 X, Int32 Y, Int32 cx, Int32 cy, UInt32 uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;        // x position of upper-left corner
            public int Top;         // y position of upper-left corner
            public int Right;       // x position of lower-right corner
            public int Bottom;      // y position of lower-right corner
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern Boolean ReadProcessMemory(
            IntPtr hProcess,
            Int64 lpBaseAddress,
            [Out] Byte[] lpBuffer,
            UInt64 dwSize,
            out IntPtr lpNumberOfBytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool WriteProcessMemory(
            IntPtr hProcess,
            Int64 lpBaseAddress,
            [In, Out] Byte[] lpBuffer,
            UInt64 dwSize,
            out IntPtr lpNumberOfBytesWritten);

        #endregion
    }
}
