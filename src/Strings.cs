namespace MonitorConfig
{
    // Every text the user sees, by key. The texts themselves live in src\Lang\<language>.txt (one file per language,
    // same keys); this class gives the code typed access to them. Placeholders {0}, {1}... are filled here.
    static class Strings
    {
        public const string AppName = "MonitorConfig";

        static string T(string key) { return Lang.Get(key); }

        static string F(string key, params object[] args)
        {
            string text = Lang.Get(key);
            try { return string.Format(text, args); }
            catch (System.FormatException) { return text; }
        }

        // ---------------------------------------------------------------- status
        public static string Searching { get { return T("Searching"); } }
        public static string Ready { get { return T("Ready"); } }
        public static string NoMonitors { get { return T("NoMonitors"); } }
        public static string NoMonitorAnswers { get { return T("NoMonitorAnswers"); } }
        public static string NoDdc { get { return T("NoDdc"); } }
        public static string Restoring { get { return T("Restoring"); } }
        public static string ReadingAll { get { return T("ReadingAll"); } }
        public static string ReadAllDone { get { return T("ReadAllDone"); } }
        public static string DdcError(string message) { return F("DdcError", message); }
        public static string WriteRejected(string control) { return F("WriteRejected", control); }
        public static string SaveRejected(string model) { return F("SaveRejected", model); }
        public static string SettingsReadError(string error) { return F("SettingsReadError", error); }
        public static string SettingsSaveError(string error) { return F("SettingsSaveError", error); }
        public static string InputBouncedRetry(string model, string from, string to) { return F("InputBouncedRetry", model, from, to); }
        public static string InputBouncedGiveUp(string model, string from, string to) { return F("InputBouncedGiveUp", model, from, to); }
        public static string CrashMessage(string logPath, string message) { return F("CrashMessage", logPath, message); }

        // ---------------------------------------------------------------- monitors
        public static string GenericMonitor { get { return T("GenericMonitor"); } }
        public static string NoDdcSuffix { get { return T("NoDdcSuffix"); } }
        public static string LeftScreen { get { return T("LeftScreen"); } }
        public static string RightScreen { get { return T("RightScreen"); } }
        public static string ThisPc { get { return T("ThisPc"); } }

        // ---------------------------------------------------------------- controls (shared)
        public static string Brightness { get { return T("Brightness"); } }
        public static string Contrast { get { return T("Contrast"); } }
        public static string Sharpness { get { return T("Sharpness"); } }
        public static string ColorPreset { get { return T("ColorPreset"); } }
        public static string Red { get { return T("Red"); } }
        public static string Green { get { return T("Green"); } }
        public static string Blue { get { return T("Blue"); } }
        public static string Volume { get { return T("Volume"); } }
        public static string Input { get { return T("Input"); } }
        public static string Profiles { get { return T("Profiles"); } }
        public static string MuteButton { get { return T("MuteButton"); } }
        public static string MuteMenu { get { return T("MuteMenu"); } }
        public static string MutedSuffix { get { return T("MutedSuffix"); } }
        public static string Ok { get { return T("Ok"); } }
        public static string Cancel { get { return T("Cancel"); } }
        public static string Copy { get { return T("Copy"); } }
        public static string BrightnessTooltip(int percent) { return F("BrightnessTooltip", percent); }

        // ---------------------------------------------------------------- tray
        public static string OpenFullPanel { get { return T("OpenFullPanel"); } }
        public static string OpenApp { get { return T("OpenApp"); } }
        public static string MenuSettingsShort { get { return T("MenuSettingsShort"); } }
        public static string RedetectMonitors { get { return T("RedetectMonitors"); } }
        public static string Exit { get { return T("Exit"); } }
        public static string ChangeInputTitle { get { return T("ChangeInputTitle"); } }
        public static string InputMenu(string input) { return F("InputMenu", input); }
        public static string ProfileMenu(string name, string model) { return model != null ? F("ProfileMenuOn", name, model) : F("ProfileMenu", name); }

        public static string ChangeInputQuestion(string model, string target, string current)
        {
            return F("ChangeInputQuestion", model, target) + "\n\n" + (current != null ? F("ChangeInputNow", current) + " " : "") + T("ChangeInputWarning");
        }

        // ---------------------------------------------------------------- main window
        public static string MenuFile { get { return T("MenuFile"); } }
        public static string MenuRedetect { get { return T("MenuRedetect"); } }
        public static string MenuHide { get { return T("MenuHide"); } }
        public static string MenuExit { get { return T("MenuExit"); } }
        public static string MenuOptions { get { return T("MenuOptions"); } }
        public static string MenuSettings { get { return T("MenuSettings"); } }
        public static string MenuHelp { get { return T("MenuHelp"); } }
        public static string MenuAbout { get { return T("MenuAbout"); } }
        public static string MenuAboutTitle { get { return T("MenuAboutTitle"); } }
        public static string MonitorLabel { get { return T("MonitorLabel"); } }
        public static string Redetect { get { return T("Redetect"); } }
        public static string NoDdcHelp { get { return T("NoDdcHelp"); } }
        public static string AboutText(string version, string settingsPath) { return F("AboutText", version, settingsPath); }
        public static string ResetTitle { get { return T("ResetTitle"); } }
        public static string ResetQuestion(string what, string model) { return F("ResetQuestion", what, model); }

        // Imagen
        public static string TabImage { get { return T("TabImage"); } }
        public static string RgbNeedsUser { get { return T("RgbNeedsUser"); } }
        public static string SwitchToUser { get { return T("SwitchToUser"); } }
        public static string ResetLuminance { get { return T("ResetLuminance"); } }
        public static string ResetLuminanceWhat { get { return T("ResetLuminanceWhat"); } }
        public static string ResetColor { get { return T("ResetColor"); } }
        public static string ResetColorWhat { get { return T("ResetColorWhat"); } }

        // Entrada y audio
        public static string TabInput { get { return T("TabInput"); } }
        public static string NameInputs { get { return T("NameInputs"); } }
        public static string MuteSpeakers { get { return T("MuteSpeakers"); } }
        public static string ShowingInput(string input) { return F("ShowingInput", input); }
        public static string UnknownInput(uint code) { return F("UnknownInput", code.ToString("X2")); }

        // Perfiles
        public static string ProfileColumn { get { return T("ProfileColumn"); } }
        public static string SettingsColumn { get { return T("SettingsColumn"); } }
        public static string Apply { get { return T("Apply"); } }
        public static string NewProfile { get { return T("NewProfile"); } }
        public static string EditProfile { get { return T("EditProfile"); } }
        public static string DeleteProfile { get { return T("DeleteProfile"); } }
        public static string DeleteProfileTitle { get { return T("DeleteProfileTitle"); } }
        public static string ProfilesNote { get { return T("ProfilesNote"); } }
        public static string DeleteProfileQuestion(string name) { return F("DeleteProfileQuestion", name); }
        public static string ProfileDefaultName(int n) { return F("ProfileDefaultName", n); }
        public static string ProfileMonitorMissing(string name) { return F("ProfileMonitorMissing", name); }
        public static string ProfileApplied(string name, string model) { return F("ProfileApplied", name, model); }
        public static string ProfileIndicator(string name) { return F("ProfileIndicator", name); }

        // Sistema
        public static string TabSystem { get { return T("TabSystem"); } }
        public static string MenuLanguage { get { return T("MenuLanguage"); } }
        public static string OsdEnabled { get { return T("OsdEnabled"); } }
        public static string OsdTitle { get { return T("OsdTitle"); } }
        public static string OsdOffQuestion { get { return T("OsdOffQuestion"); } }
        public static string Power { get { return T("Power"); } }
        public static string PowerOff { get { return T("PowerOff"); } }
        public static string PowerOffHint { get { return T("PowerOffHint"); } }
        public static string Factory { get { return T("Factory"); } }
        public static string FactoryReset { get { return T("FactoryReset"); } }
        public static string FactoryResetWhat { get { return T("FactoryResetWhat"); } }
        public static string FactoryResetHint { get { return T("FactoryResetHint"); } }
        public static string PowerOffQuestion(string model) { return F("PowerOffQuestion", model); }

        // Información
        public static string TabInfo { get { return T("TabInfo"); } }
        public static string InfoModel { get { return T("InfoModel"); } }
        public static string InfoVendor { get { return T("InfoVendor"); } }
        public static string InfoSerial { get { return T("InfoSerial"); } }
        public static string InfoId { get { return T("InfoId"); } }
        public static string InfoWindowsDisplay { get { return T("InfoWindowsDisplay"); } }
        public static string InfoVideoMode { get { return T("InfoVideoMode"); } }
        public static string InfoVFreq { get { return T("InfoVFreq"); } }
        public static string InfoHFreq { get { return T("InfoHFreq"); } }
        public static string InfoColorTemp { get { return T("InfoColorTemp"); } }
        public static string InfoTechnology { get { return T("InfoTechnology"); } }
        public static string InfoController { get { return T("InfoController"); } }
        public static string InfoFirmware { get { return T("InfoFirmware"); } }
        public static string InfoMccs { get { return T("InfoMccs"); } }
        public static string InfoCaps { get { return T("InfoCaps"); } }
        public static string CapsString { get { return T("CapsString"); } }
        public static string CapsNone { get { return T("CapsNone"); } }
        public static string NoSerial { get { return T("NoSerial"); } }
        public static string PrimarySuffix { get { return T("PrimarySuffix"); } }
        public static string VideoMode(int width, int height, int hz) { return F("VideoMode", width, height, hz); }
        public static string CapsSummary(bool fromCache, int codes) { return F(fromCache ? "CapsFromCache" : "CapsFromMonitor", codes); }

        // Avanzado
        public static string TabAdvanced { get { return T("TabAdvanced"); } }
        public static string AdvancedWarning { get { return T("AdvancedWarning"); } }
        public static string CodeColumn { get { return T("CodeColumn"); } }
        public static string NameColumn { get { return T("NameColumn"); } }
        public static string ValueColumn { get { return T("ValueColumn"); } }
        public static string MaxColumn { get { return T("MaxColumn"); } }
        public static string OptionsColumn { get { return T("OptionsColumn"); } }
        public static string ReadAll { get { return T("ReadAll"); } }
        public static string CodeHex { get { return T("CodeHex"); } }
        public static string ValueLabel { get { return T("ValueLabel"); } }
        public static string Read { get { return T("Read"); } }
        public static string Write { get { return T("Write"); } }
        public static string ReadError { get { return T("ReadError"); } }
        public static string InvalidCodeTitle { get { return T("InvalidCodeTitle"); } }
        public static string InvalidCodeText { get { return T("InvalidCodeText"); } }
        public static string WriteVcpTitle { get { return T("WriteVcpTitle"); } }
        public static string WriteVcpQuestion(uint value, byte code, string name) { return F("WriteVcpQuestion", value, value.ToString("X2"), code.ToString("X2"), name); }

        // ---------------------------------------------------------------- dialogs
        public static string SettingsTitle { get { return T("SettingsTitle"); } }
        public static string SettingsLanguage { get { return T("SettingsLanguage"); } }
        public static string LanguageAuto(string systemLanguage) { return F("LanguageAuto", systemLanguage); }
        public static string SettingsStart { get { return T("SettingsStart"); } }
        public static string SettingsAutostart { get { return T("SettingsAutostart"); } }
        public static string SettingsStartMinimized { get { return T("SettingsStartMinimized"); } }
        public static string SettingsCloseToTray { get { return T("SettingsCloseToTray"); } }
        public static string SettingsProfileOsd { get { return T("SettingsProfileOsd"); } }
        public static string SettingsSafety { get { return T("SettingsSafety"); } }
        public static string SettingsConfirmInput { get { return T("SettingsConfirmInput"); } }
        public static string SettingsKeepInput { get { return T("SettingsKeepInput"); } }
        public static string SettingsFile { get { return T("SettingsFile"); } }
        public static string AutostartFailed { get { return T("AutostartFailed"); } }
        public static string SettingsKeepInputHint(int retries) { return F("SettingsKeepInputHint", retries); }

        public static string InputNamesTitle { get { return T("InputNamesTitle"); } }
        public static string InputNamesHint { get { return T("InputNamesHint"); } }
        public static string InputsOf(string model, string position) { return position.Length > 0 ? F("InputsOfAt", model, position) : F("InputsOf", model); }
        public static string InputNamesPcHint(string input) { return F("InputNamesPcHint", input); }

        public static string ProfileTitle { get { return T("ProfileTitle"); } }
        public static string ProfileName { get { return T("ProfileName"); } }
        public static string ProfileSettings { get { return T("ProfileSettings"); } }
        public static string ProfileTakeCurrent { get { return T("ProfileTakeCurrent"); } }
        public static string ProfileRgbHint { get { return T("ProfileRgbHint"); } }
        public static string ProfileNeedsName { get { return T("ProfileNeedsName"); } }
        public static string ProfileNeedsSetting { get { return T("ProfileNeedsSetting"); } }
        public static string ProfileFor(string model) { return F("ProfileFor", model); }
        public static string ProfileExists(string name) { return F("ProfileExists", name); }

        // ---------------------------------------------------------------- VCP (names of codes and values: vcp.XX, vcp.XX.YY)
        public static string VcpManufacturer { get { return T("VcpManufacturer"); } }
        public static string VcpCode(byte code) { return F("VcpCode", code.ToString("X2")); }
        public static string VcpValueUnknown(uint value) { return F("VcpValueUnknown", value.ToString("X2")); }
    }
}
