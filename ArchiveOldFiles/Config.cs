using System;
using System.Xml.Serialization;

namespace OldFileArchiver
{
    [Serializable]
    public class Config
    {
        [XmlElement("Entry")]
        public ConfigEntry[] Entries { get; set; }
    }

    [Serializable]
    public class ConfigEntry
    {
        [XmlAttribute]
        public string Directory { get; set; }
        [XmlAttribute]
        public string ArchiveDirectory { get; set; }
        [XmlAttribute]
        public int Days { get; set; }
        [XmlAttribute]
        public int DeleteDays { get; set; }
    }
}