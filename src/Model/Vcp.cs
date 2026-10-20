using System;
using System.Collections.Generic;

namespace MonitorConfig
{
    // VCP codes (VESA MCCS 2.1/2.2). Translatable names come from the language files (keys vcp.XX and vcp.XX.YY);
    // names that are the same in every language (connectors, chip makers, the monitor's menu languages) live here.
    static class Vcp
    {
        public const byte NewControlValue = 0x02, FactoryReset = 0x04, ResetLuminance = 0x05, ResetColor = 0x08,
            ColorTempIncrement = 0x0B, ColorTemp = 0x0C, Brightness = 0x10, Contrast = 0x12, ColorPreset = 0x14,
            RedGain = 0x16, GreenGain = 0x18, BlueGain = 0x1A, Input = 0x60, Volume = 0x62, Sharpness = 0x87,
            Mute = 0x8D, HFreq = 0xAC, VFreq = 0xAE, DisplayTech = 0xB6, Controller = 0xC8, Firmware = 0xC9,
            Osd = 0xCA, OsdLanguage = 0xCC, Power = 0xD6, DisplayMode = 0xDC, Version = 0xDF;

        public const byte PresetSrgb = 0x01, PresetUser1 = 0x0B, MuteOn = 0x01, MuteOff = 0x02, OsdOff = 0x01, OsdOn = 0x02,
            PowerOn = 0x01, PowerOffDpm = 0x04, PowerOffButton = 0x05;

        // Codes a profile can store, in the order they are applied (preset before RGB gains).
        public static readonly byte[] ProfileCodes = { ColorPreset, RedGain, GreenGain, BlueGain, Brightness, Contrast, Sharpness, Volume, Mute };

        // Codes whose values have names (the rest are numbers).
        static readonly HashSet<byte> Named = new HashSet<byte> { ColorPreset, Input, Mute, Osd, Power, DisplayMode, DisplayTech, Controller, OsdLanguage };

        // Value as a percentage of its maximum (monitors that report no maximum use 0..100).
        public static int Percent(uint value, uint max)
        {
            return (int)Math.Round(value * 100.0 / (max > 0 ? max : 100));
        }

        // Write-only commands (restore defaults): no value to remember and nothing to store afterwards.
        public static bool IsAction(byte code)
        {
            return code == FactoryReset || code == ResetLuminance || code == ResetColor || code == 0x06 || code == 0x0A;
        }

        public static bool IsContinuous(byte code)
        {
            switch (code)
            {
                case Brightness: case Contrast: case RedGain: case GreenGain: case BlueGain:
                case Volume: case Sharpness: case ColorTemp:
                    return true;
            }
            return false;
        }

        // Value names that do not depend on the interface language.
        static readonly Dictionary<byte, Dictionary<byte, string>> fixedValues = new Dictionary<byte, Dictionary<byte, string>>
        {
            { Input, new Dictionary<byte, string> {
                { 0x01, "VGA 1" }, { 0x02, "VGA 2" }, { 0x03, "DVI 1" }, { 0x04, "DVI 2" }, { 0x07, "S-Video 1" }, { 0x08, "S-Video 2" },
                { 0x0F, "DisplayPort" }, { 0x10, "DisplayPort 2" }, { 0x11, "HDMI 1" }, { 0x12, "HDMI 2" }, { 0x1B, "USB-C" } } },
            { DisplayTech, new Dictionary<byte, string> { { 0x04, "LCoS" }, { 0x05, "Plasma" }, { 0x06, "OLED" }, { 0x07, "EL" } } },
            { Controller, new Dictionary<byte, string> {
                { 0x01, "Conexant" }, { 0x02, "Genesis" }, { 0x03, "Macronix" }, { 0x04, "IDT" }, { 0x05, "MStar" },
                { 0x06, "Myson" }, { 0x07, "Philips" }, { 0x08, "PixelWorks" }, { 0x09, "Realtek" }, { 0x0A, "Sage" },
                { 0x0B, "Silicon Image" }, { 0x0C, "SmartASIC" }, { 0x0D, "STMicroelectronics" }, { 0x0E, "Topro" },
                { 0x0F, "Trumpion" }, { 0x10, "Welltrend" }, { 0x11, "Samsung" }, { 0x12, "Novatek" }, { 0x13, "STK" } } },
            // The monitor's menu languages, each written in itself (as language pickers usually do).
            { OsdLanguage, new Dictionary<byte, string> {
                { 0x01, "繁體中文" }, { 0x02, "English" }, { 0x03, "Français" }, { 0x04, "Deutsch" }, { 0x05, "Italiano" },
                { 0x06, "日本語" }, { 0x07, "한국어" }, { 0x08, "Português (Portugal)" }, { 0x09, "Русский" }, { 0x0A, "Español" },
                { 0x0B, "Svenska" }, { 0x0C, "Türkçe" }, { 0x0D, "简体中文" }, { 0x0E, "Português (Brasil)" }, { 0x0F, "العربية" },
                { 0x10, "Български" }, { 0x11, "Hrvatski" }, { 0x12, "Čeština" }, { 0x13, "Dansk" }, { 0x14, "Nederlands" },
                { 0x15, "Eesti" }, { 0x16, "Suomi" }, { 0x17, "Français (Canada)" }, { 0x18, "Ελληνικά" }, { 0x19, "עברית" },
                { 0x1A, "हिन्दी" }, { 0x1B, "Magyar" }, { 0x1C, "Íslenska" }, { 0x1D, "Bahasa Indonesia" }, { 0x1E, "Latviešu" },
                { 0x1F, "Lietuvių" }, { 0x20, "Norsk" }, { 0x21, "Polski" }, { 0x22, "Română" }, { 0x23, "Српски" },
                { 0x24, "Slovenčina" }, { 0x25, "Slovenščina" }, { 0x26, "ไทย" }, { 0x27, "Українська" }, { 0x28, "Tiếng Việt" } } },
        };

        public static string Name(byte code)
        {
            string key = "vcp." + code.ToString("X2");
            if (Lang.Has(key)) return Lang.Get(key);
            return code >= 0xE0 ? Strings.VcpManufacturer : Strings.VcpCode(code);
        }

        public static string ValueName(byte code, uint value)
        {
            if (value <= 0xFF)
            {
                Dictionary<byte, string> map;
                string name;
                if (fixedValues.TryGetValue(code, out map) && map.TryGetValue((byte)value, out name)) return name;
                string key = "vcp." + code.ToString("X2") + "." + value.ToString("X2");
                if (Lang.Has(key)) return Lang.Get(key);
            }
            return Strings.VcpValueUnknown(value);
        }

        public static bool HasValueNames(byte code)
        {
            return Named.Contains(code);
        }

        // Human-readable value for the info and advanced views.
        public static string Format(byte code, VcpValue v)
        {
            if (v == null) return "—";
            uint wide = ((v.Max & 0xFF) << 16) | v.Current;
            switch (code)
            {
                case VFreq: return (wide / 100.0).ToString("0.00") + " Hz";
                case HFreq: return (wide / 1000.0).ToString("0.0") + " kHz";
                case Version: return (v.Current >> 8) + "." + (v.Current & 0xFF);
                case Firmware: return (v.Current >> 8) + "." + (v.Current & 0xFF).ToString("00");
                case Controller: return ValueName(code, v.Current & 0xFF);
            }
            if (IsContinuous(code)) return v.Current + " / " + v.Max;
            if (HasValueNames(code)) return ValueName(code, v.Current) + " (" + v.Current.ToString("X2") + ")";
            return v.Current.ToString();
        }
    }
}
