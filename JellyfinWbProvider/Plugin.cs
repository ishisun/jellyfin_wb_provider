using System;
using System.Collections.Generic;
using System.Globalization;
using JellyfinWbProvider.Configuration;
using JellyfinWbProvider.Providers;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.DependencyInjection;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Entities.Movies;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace JellyfinWbProvider
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        private readonly IApplicationPaths _applicationPaths;
        private readonly IXmlSerializer _xmlSerializer;
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger<Plugin> _logger;

        public Plugin(
            IApplicationPaths applicationPaths,
            IXmlSerializer xmlSerializer,
            ILibraryManager libraryManager,
            ILogger<Plugin>? logger = null)
            : base(applicationPaths, xmlSerializer)
        {
            _applicationPaths = applicationPaths;
            _xmlSerializer = xmlSerializer;
            _libraryManager = libraryManager;
            _logger = logger ?? new NullLogger<Plugin>();
            
            // 設定の初期化
            Instance = this;

            // 設定ファイルが存在しない場合は新規作成
            if (Configuration == null)
            {
                Configuration = new PluginConfiguration
                {
                    ServerIp = "127.0.0.1",
                    ServerPort = 8765
                };
                SaveConfiguration();
            }
            // 既存の設定をチェックして必要に応じて更新
            else if (string.IsNullOrEmpty(Configuration.ServerIp) || Configuration.ServerPort == 0)
            {
                Configuration.ServerIp = "127.0.0.1";
                Configuration.ServerPort = 8765;
                SaveConfiguration();
            }

            _logger.LogInformation($"プラグイン設定を読み込みました: ServerIp={Configuration.ServerIp}, ServerPort={Configuration.ServerPort}");

            // メタデータプロバイダーを登録
            var providerLogger = new NullLogger<WbMetadataProvider>();  // 新しいロガーインスタンスを作成
            var provider = new WbMetadataProvider(
                applicationPaths,
                xmlSerializer,
                providerLogger,  // 型が一致するロガーを渡す
                Configuration    // 設定を直接渡す
            );

            // プロバイダーをサービスコレクションに登録
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddSingleton<IRemoteImageProvider>(provider);
            serviceCollection.AddSingleton<IMetadataProvider>(provider);
            serviceCollection.AddSingleton<IRemoteMetadataProvider<Movie, MovieInfo>>(
                sp => provider
            );

            var serviceProvider = serviceCollection.BuildServiceProvider();
        }

        public override string Name => "WbProvider";
        public override Guid Id => Guid.Parse("08767783-874a-452e-8332-8059e7c6262d");
        public static Plugin? Instance { get; private set; }

        public IEnumerable<PluginPageInfo> GetPages()
        {
            var assembly = GetType().Assembly;
            var resourceNames = assembly.GetManifestResourceNames();
            
            foreach (var name in resourceNames)
            {
                Console.WriteLine($"Found resource: {name}");
            }

            var resourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace);
            Console.WriteLine($"Looking for resource: {resourcePath}");

            return new[]
            {
                new PluginPageInfo
                {
                    Name = Name,
                    EmbeddedResourcePath = resourcePath
                }
            };
        }
    }
}