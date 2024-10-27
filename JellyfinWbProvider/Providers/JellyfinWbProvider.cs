using System;
using System.IO;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using System.Linq;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using JellyfinWbProvider.Models;
using JellyfinWbProvider.Configuration;
using MediaBrowser.Model.Providers;
using Jellyfin.Data.Enums;
using MediaBrowser.Model.Entities;

namespace JellyfinWbProvider.Providers
{
    public class WbMetadataProvider : 
        IRemoteMetadataProvider<Movie, MovieInfo>,
        IHasOrder,
        IRemoteImageProvider
    {
        public static WbMetadataProvider? Current { get; set; }

        private readonly IApplicationPaths _applicationPaths;
        private readonly IXmlSerializer _xmlSerializer;
        private readonly ILogger<WbMetadataProvider> _logger;
        private readonly HttpClient _httpClient;
        private readonly PluginConfiguration _config;

        public string Name => "WbProvider";
        public string Description => "カスタムビデオメタデータプロバイダー";
        public bool IsEnabled => true;
        public bool IsEnabledByDefault => true;
        public int Order => 1;

        public WbMetadataProvider(
            IApplicationPaths applicationPaths,
            IXmlSerializer xmlSerializer,
            ILogger<WbMetadataProvider>? logger = null,
            PluginConfiguration? configuration = null)
        {
            _applicationPaths = applicationPaths;
            _xmlSerializer = xmlSerializer;
            _logger = logger ?? new NullLogger<WbMetadataProvider>();
            _httpClient = new HttpClient();
            
            _config = configuration ?? new PluginConfiguration();
            
            _logger.LogInformation($"設定を読み込みました: ServerIp={_config.ServerIp}, ServerPort={_config.ServerPort}");
        }

        public async Task<MetadataResult<Movie>> GetMetadata(MovieInfo info, CancellationToken cancellationToken)
        {
            var serverIp = _config?.ServerIp ?? "127.0.0.1";
            var serverPort = _config?.ServerPort.ToString() ?? "8765";
            
            _logger.LogInformation($"接続先: http://{serverIp}:{serverPort}/metadata");
            _logger.LogInformation($"設定値: ServerIp={_config?.ServerIp}, ServerPort={_config?.ServerPort}");

            var result = new MetadataResult<Movie>
            {
                HasMetadata = false,
                Item = new Movie
                {
                    Name = info.Name,
                    Path = info.Path,
                    ProductionYear = info.Year
                }
            };

            try
            {
                var fileName = Path.GetFileNameWithoutExtension(info.Path);
                _logger.LogInformation($"ファイル名: {fileName}");

                var content = new StringContent(
                    $"postBody={Uri.EscapeDataString(fileName)}", 
                    Encoding.UTF8, 
                    "application/x-www-form-urlencoded");

                using var response = await _httpClient.PostAsync(
                    $"http://{serverIp}:{serverPort}/metadata",
                    content,
                    cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogInformation($"サーバーからの応答: {responseContent}");

                    try
                    {
                        var metadata = JsonSerializer.Deserialize<CustomMovieData>(responseContent);

                        if (metadata != null)
                        {
                            _logger.LogInformation($"デシリアライズ結果: Title={metadata.Title}, Summary={metadata.Summary}, Studio={metadata.Studio}");
                            
                            result.HasMetadata = true;
                            result.Item.Name = metadata.Title ?? info.Name;
                            result.Item.Overview = metadata.Summary;
                            
                            // 日付を解析してPremiereDateとProductionYearを設定
                            if (DateTime.TryParseExact(
                                metadata.CreateTime, 
                                "yyyy-MM-dd HH:mm:ss",
                                CultureInfo.InvariantCulture,
                                DateTimeStyles.None,
                                out DateTime createDate))
                            {
                                result.Item.PremiereDate = createDate;
                                result.Item.ProductionYear = createDate.Year;
                                _logger.LogInformation($"公開日を設定: {createDate}");
                            }

                            // スタジオを設定
                            if (!string.IsNullOrEmpty(metadata.Studio))
                            {
                                result.Item.Studios = new[] { metadata.Studio };
                            }

                            // タグを設定
                            if (!string.IsNullOrEmpty(metadata.Tags))
                            {
                                result.Item.Tags = metadata.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries);
                            }

                            // 出演者を設定
                            if (!string.IsNullOrEmpty(metadata.Artist))
                            {
                                var artists = metadata.Artist.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                                foreach (var artistName in artists)
                                {
                                    var person = new PersonInfo
                                    {
                                        Name = artistName,
                                        Type = PersonType.Actor
                                    };
                                    result.AddPerson(person);
                                }
                                _logger.LogInformation($"出演者を設定: {metadata.Artist}");
                            }

                            // ポスター画像のURLを設定
                            if (!string.IsNullOrEmpty(metadata.PosterUrl))
                            {
                                // RemoteImageInfoを作成
                                var imageInfo = new RemoteImageInfo
                                {
                                    ProviderName = Name,
                                    Url = metadata.PosterUrl,
                                    Type = ImageType.Primary
                                };
                                
                                // HasMetadataをtrueに設定
                                result.HasMetadata = true;
                                
                                // GetImagesメソッドで画像情報を提供するようにする
                                _logger.LogInformation($"ポスター画像のURLを設定: {metadata.PosterUrl}");
                            }

                            // タグを設定
                            if (!string.IsNullOrEmpty(metadata.Tags))
                            {
                                result.Item.Tags = metadata.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries);
                                
                                // コミュニティ評価を設定
                                var rating = metadata.GetCommunityRating();
                                if (rating.HasValue)
                                {
                                    result.Item.CommunityRating = rating.Value;
                                    _logger.LogInformation($"コミュニティ評価を設定: {rating.Value}");
                                }
                            }

                            // 評論家評価を設定
                            if (metadata.Score.HasValue)
                            {
                                result.Item.CriticRating = metadata.Score.Value;
                                _logger.LogInformation($"評論家評価を設定: {metadata.Score.Value}");
                            }

                        }
                        else
                        {
                            _logger.LogWarning("メタデータのデシリアライズ結果がnullでした");
                        }
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogError($"JSONのデシリアライズに失敗しました: {ex.Message}");
                        _logger.LogError($"受信したJSON: {responseContent}");
                    }
                }
                else
                {
                    _logger.LogWarning($"メタデータの取得に失敗しました: {response.StatusCode}");
                    var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogWarning($"エラーレスポンス: {errorContent}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"メタデータの取得中にエラーが発生しました: {ex.Message} ({serverIp}:{serverPort})");
            }

            return result;
        }

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(MovieInfo searchInfo, CancellationToken cancellationToken)
        {
            var serverIp = _config?.ServerIp ?? "127.0.0.1";
            var serverPort = _config?.ServerPort.ToString() ?? "8765";
            
            _logger.LogInformation($"検索接続先: http://{serverIp}:{serverPort}/search");
            
            var results = new List<RemoteSearchResult>();
            
            try
            {
                var content = new StringContent(
                    $"query={Uri.EscapeDataString(searchInfo.Name)}", 
                    Encoding.UTF8, 
                    "application/x-www-form-urlencoded");

                using var response = await _httpClient.PostAsync(
                    $"http://{serverIp}:{serverPort}/search",
                    content,
                    cancellationToken);

                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                
                var searchResults = JsonSerializer.Deserialize<List<CustomMovieData>>(json);
                
                if (searchResults != null)
                {
                    foreach (var result in searchResults)
                    {
                        results.Add(new RemoteSearchResult
                        {
                            Name = result.Title,
                            ProductionYear = result.Year,
                            ImageUrl = result.PosterUrl
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"検索中にエラーが発生しました: {ex.Message} ({serverIp}:{serverPort})");
            }

            return results;
        }

        // IRemoteImageProviderの実装
        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            
            if (item is Movie movie)
            {
                var fileName = Path.GetFileNameWithoutExtension(movie.Path);
                var serverIp = _config?.ServerIp ?? "127.0.0.1";
                var serverPort = _config?.ServerPort.ToString() ?? "8765";
                
                try
                {
                    var content = new StringContent(
                        $"postBody={Uri.EscapeDataString(fileName)}", 
                        Encoding.UTF8, 
                        "application/x-www-form-urlencoded");

                    using var response = await _httpClient.PostAsync(
                        $"http://{serverIp}:{serverPort}/metadata",
                        content,
                        cancellationToken);

                    if (response.IsSuccessStatusCode)
                    {
                        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                        var metadata = JsonSerializer.Deserialize<CustomMovieData>(responseContent);

                        if (!string.IsNullOrEmpty(metadata?.PosterUrl))
                        {
                            images.Add(new RemoteImageInfo
                            {
                                ProviderName = Name,
                                Url = metadata.PosterUrl,
                                Type = ImageType.Primary
                            });
                            _logger.LogInformation($"画像情報を追加: {metadata.PosterUrl}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"画像情報の取得中にエラー: {ex.Message}");
                }
            }

            return images;
        }

        public bool Supports(BaseItem item) => item is Movie;

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        {
            return new[] { ImageType.Primary };
        }

        public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation($"画像の取得を開始: {url}");
                
                // UNCパスかどうかをチェック
                if (url.StartsWith("\\\\"))
                {
                    try
                    {
                        // UNCパスをLinuxパスに変換
                        string linuxPath = ConvertUncToLinuxPath(url);
                        _logger.LogInformation($"変換後のパス: {linuxPath}");

                        // ファイルが存在するか確認
                        if (File.Exists(linuxPath))
                        {
                            // ファイルを読み込む
                            var imageBytes = await File.ReadAllBytesAsync(linuxPath, cancellationToken);
                            
                            // レスポンスを作成
                            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
                            response.Content = new ByteArrayContent(imageBytes);
                            
                            // Content-Typeを設定
                            string extension = Path.GetExtension(linuxPath).ToLower();
                            string contentType = extension switch
                            {
                                ".jpg" or ".jpeg" => "image/jpeg",
                                ".png" => "image/png",
                                _ => "application/octet-stream"
                            };
                            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
                            
                            _logger.LogInformation($"ローカルファイルからの画像取得に成功: {linuxPath}");
                            return response;
                        }
                        else
                        {
                            _logger.LogWarning($"ファイルが存在しません: {linuxPath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"ローカルファイルの読み込み中にエラー: {url}, Error: {ex.Message}");
                    }
                }
                // 通常のHTTP/HTTPSリクエスト
                else if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && 
                        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                {
                    var response = await _httpClient.GetAsync(url, cancellationToken);
                    if (response.IsSuccessStatusCode)
                    {
                        _logger.LogInformation($"画像の取得に成功: {url}");
                        return response;
                    }
                    else
                    {
                        _logger.LogWarning($"画像の取得に失敗: {url}, StatusCode: {response.StatusCode}");
                    }
                }
                else
                {
                    _logger.LogWarning($"サポートされていないURL形式: {url}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"画像の取得中にエラーが発生: {url}, Error: {ex.Message}");
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        }

        private string ConvertUncToLinuxPath(string uncPath)
        {
            // \\server\share\path\file.jpg 形式のパスを処理
            var parts = uncPath.TrimStart('\\').Split('\\');
            
            // NASの特定のパターンを変換
            if (parts.Length >= 2 && parts[0] == "shun920" && parts[1] == "av")
            {
                // 残りのパスを結合
                var remainingPath = string.Join("/", parts.Skip(2));
                return $"/volume1/av/{remainingPath}";
            }

            // その他のパターンの場合はそのまま返す
            return uncPath;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _httpClient?.Dispose();
            }
        }
    }
}