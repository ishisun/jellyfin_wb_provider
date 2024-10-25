using MediaBrowser.Model.Plugins;

namespace JellyfinWbProvider.Configuration
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public PluginConfiguration()
        {
            ServerIp = "127.0.0.1";
            ServerPort = 8765;
        }

        public string ServerIp { get; set; }
        public int ServerPort { get; set; }
    }
}
