using System.IO;
using Newtonsoft.Json;
using Vintagestory.API.Config;

namespace instruments
{
    public class SettingsFile<TSettings> where TSettings : new()
    {
        private readonly FileInfo _file;

        public SettingsFile(string filePath)
        {
            _file = new FileInfo(filePath);

            if (!Directory.Exists(GamePaths.ModConfig))
            {
                Directory.CreateDirectory(GamePaths.ModConfig);
                Save();
                return;
            }

            if (!_file.Exists)
            {
                Save();
                return;
            }
            var jsonText = File.ReadAllText(_file.FullName);
            Settings = JsonConvert.DeserializeObject<TSettings>(jsonText);
        }

        public TSettings Settings { get; private set; } = new TSettings();

        public void Save()
        {
            var jsonText = JsonConvert.SerializeObject(Settings, Formatting.Indented);
            File.WriteAllText(_file.FullName, jsonText);
        }
    }
}