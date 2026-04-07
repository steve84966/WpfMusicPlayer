using System.Xml.Serialization;

namespace WpfMusicPlayer.Models
{
    [XmlRoot("settings")]
    public class ConfigData
    {
        [XmlElement("audio-settings")]
        public AudioSettings Audio { get; set; } = new();

        public class AudioSettings
        {
            [XmlElement("sample-rate")] public int SampleRate { get; set; } = 44100;

            public enum ChannelType
            {
                [XmlEnum("mono")] Mono,
                [XmlEnum("stereo")] Stereo,
                [XmlEnum("surround_5_1")] Surround51,
                [XmlEnum("surround_7_1")] Surround71
            }
            
            [XmlElement("channel-type")] public ChannelType Channel { get; set; }
        }
        
        [XmlElement("ui-settings")]
        public UISettings UI { get; set; } = new();

        public class UISettings
        {
            public enum ThemeMode
            {
                [XmlEnum("light")] Light,
                [XmlEnum("dark")] Dark,
                [XmlEnum("system")] System
            }

            [XmlElement("theme")] public ThemeMode Theme { get; set; }

            public enum BackgroundMode
            {
                [XmlEnum("solid")] Solid,
                [XmlEnum("acrylic")] Acrylic,
                [XmlEnum("image-blur")] ImageBlur
            }

            [XmlElement("background")]
            public BackgroundMode Background { get; set; }
        }

        [XmlElement("desktop-lyric")]
        public DesktopLyricSettings DesktopLyric { get; set; } = new();

        public class DesktopLyricSettings
        {
            [XmlElement("desktop-lyric-font-size")]
            public int DesktopLyricFontSize { get; set; }
            
            [XmlElement("desktop-lyric-aux-follow-main-line-size")]
            public bool IsDesktopLyricAuxFollowMainLineSize { get; set; }          
            
            [XmlElement("desktop-lyric-aux-font-size")]
            public int DesktopLyricAuxFontSize { get; set; }
        }
    }
}
