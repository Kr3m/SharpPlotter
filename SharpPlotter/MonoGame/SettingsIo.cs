using System;
using System.IO;
using Newtonsoft.Json;

namespace SharpPlotter.MonoGame
{
    public static class SettingsIo
    {
        private const string SettingsFileName = "SharpPlotter.config";
        private static readonly string SettingsFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        private static readonly string FullSettingsPath = Path.Combine(SettingsFolder, SettingsFileName);

        public static AppSettings Load()
        {
            string json;
            
            try
            {
                json = File.ReadAllText(FullSettingsPath);
            }
            catch (IOException e)
            {
                // File most likely doesn't exist
                return null;
            }

            try
            {
                return JsonConvert.DeserializeObject<AppSettings>(json);
            }
            catch (JsonException)
            {
                // Invalid json
                return null;
            }
        }

        public static void Save(AppSettings settings)
        {
            var json = JsonConvert.SerializeObject(settings);
            var tempFile = Path.GetTempFileName();
            var backupFile = FullSettingsPath + ".bak";
            
            File.WriteAllText(tempFile, json);

            if (File.Exists(FullSettingsPath))
            {
                File.Replace(tempFile, FullSettingsPath, backupFile);
            }
            else
            {
                File.Move(tempFile, FullSettingsPath);
            }
            
        }
    }
}