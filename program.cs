using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using SpotifyAPI.Web;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    // Your Client ID and Client Secret from the Spotify Developer Dashboard
    private const string clientId = "";
    private const string clientSecret = "";

    static async Task Main(string[] args)
    {
        // --- Spotify Section ---
        // Authenticate with Spotify
        var spotify = await AuthenticateSpotify();

        // Get the playlist ID 
        string playlistUrl = ""; // enter the spotify playlistURL you want to convert
        string playlistId = ExtractPlaylistIdFromUrl(playlistUrl);

        // Retrieve the playlist details
        var playlist = await GetSpotifyPlaylist(spotify, playlistId);

        // Display the playlist details
        Console.WriteLine($"Playlist Name: {playlist.Name}");
        Console.WriteLine($"Tracks in the Playlist:");
        int index = 1;
        foreach (var item in playlist.Tracks.Items)
        {
            var track = item.Track as FullTrack;
            Console.WriteLine($"{index}. {track.Name} by {string.Join(", ", track.Artists.Select(a => a.Name))}");
            index++;
        }

        // --- YouTube Section ---
        // Authenticate with YouTube API
        var youtubeService = await AuthenticateYouTube();

        // Create a new YouTube playlist
        var createdPlaylist = await CreateYouTubePlaylist(youtubeService, playlist.Name);

        // Search and add each song from Spotify to YouTube playlist
        foreach (var item in playlist.Tracks.Items)
        {
            var track = item.Track as FullTrack;

            if (track != null)
            {
                string searchQuery = $"{track.Name} {track.Artists.FirstOrDefault()?.Name}";

                try
                {
                    var searchResult = await SearchYouTubeTrack(youtubeService, searchQuery);

                    if (searchResult != null)
                    {
                        await AddTrackToYouTubePlaylist(youtubeService, createdPlaylist.Id, searchResult);
                        Console.WriteLine($"Added: {track.Name} by {string.Join(", ", track.Artists.Select(a => a.Name))}");
                    }
                    else
                    {
                        Console.WriteLine($"Track '{track.Name}' not found on YouTube.");
                    }
                }
                catch (Google.GoogleApiException ex)
                {
                    // Log the error and continue with the next track
                    Console.WriteLine($"Error adding track '{track.Name}' to YouTube playlist: {ex.Message}");
                }
                catch (Exception ex)
                {
                    // Catch any other exceptions and log them
                    Console.WriteLine($"An unexpected error occurred while processing '{track.Name}': {ex.Message}");
                }
            }
        }

        Console.WriteLine("Finished adding songs to YouTube playlist.");
    }

    //  Spotify Methods 
    private static async Task<SpotifyClient> AuthenticateSpotify()
    {
        var config = SpotifyClientConfig.CreateDefault();
        var request = new ClientCredentialsRequest(clientId, clientSecret);
        var response = await new OAuthClient(config).RequestToken(request);

        var spotify = new SpotifyClient(config.WithToken(response.AccessToken));
        return spotify;
    }

    private static async Task<FullPlaylist> GetSpotifyPlaylist(SpotifyClient spotify, string playlistId)
    {
        var playlist = await spotify.Playlists.Get(playlistId);
        return playlist;
    }

    private static string ExtractPlaylistIdFromUrl(string url)
    {
        var uri = new Uri(url);
        string[] segments = uri.AbsolutePath.Split('/');
        return segments[^1]; // The playlist ID is the last segment of the URL
    }

    //  YouTube Methods

    // Method to authenticate YouTube API using OAuth 2.0
    private static async Task<YouTubeService> AuthenticateYouTube()
    {
        UserCredential credential;
        using (var stream = new FileStream("client_secret.json", FileMode.Open, FileAccess.Read))
        {
            credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                GoogleClientSecrets.Load(stream).Secrets,
                new[] { YouTubeService.Scope.YoutubeForceSsl },
                "user",
                CancellationToken.None,
                new FileDataStore("YouTube.Auth.Store")
            );
        }

        var youtubeService = new YouTubeService(new BaseClientService.Initializer()
        {
            HttpClientInitializer = credential,
            ApplicationName = "YoutifyApp"
        });

        return youtubeService;
    }

    // Method to create a new YouTube playlist
    private static async Task<Playlist> CreateYouTubePlaylist(YouTubeService youtubeService, string playlistName)
    {
        var newPlaylist = new Playlist
        {
            Snippet = new PlaylistSnippet
            {
                Title = $"Spotify Playlist: {playlistName}",
                Description = "Playlist converted from Spotify",
            },
            Status = new PlaylistStatus
            {
                PrivacyStatus = "public" // You can change this to "private" or "unlisted"
            }
        };

        var createPlaylistRequest = youtubeService.Playlists.Insert(newPlaylist, "snippet,status");
        var createdPlaylist = await createPlaylistRequest.ExecuteAsync();
        Console.WriteLine($"Created YouTube playlist: {createdPlaylist.Snippet.Title}");
        return createdPlaylist;
    }

    // Method to search for a track on YouTube
    private static async Task<SearchResult> SearchYouTubeTrack(YouTubeService youtubeService, string query)
    {
        var searchRequest = youtubeService.Search.List("snippet");
        searchRequest.Q = query;
        searchRequest.MaxResults = 1;

        var searchResponse = await searchRequest.ExecuteAsync();
        return searchResponse.Items.FirstOrDefault();
    }

    // Method to add a track to YouTube playlist
    private static async Task AddTrackToYouTubePlaylist(YouTubeService youtubeService, string playlistId, SearchResult searchResult)
    {
        var playlistItem = new PlaylistItem
        {
            Snippet = new PlaylistItemSnippet
            {
                PlaylistId = playlistId,
                ResourceId = new ResourceId
                {
                    Kind = "youtube#video",
                    VideoId = searchResult.Id.VideoId
                }
            }
        };

        var addItemRequest = youtubeService.PlaylistItems.Insert(playlistItem, "snippet");
        await addItemRequest.ExecuteAsync();
    }
}
