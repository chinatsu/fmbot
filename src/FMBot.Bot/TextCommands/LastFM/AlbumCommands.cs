using System;
using System.Linq;
using System.Threading.Tasks;
using Discord.Commands;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Bot.Services.ThirdParty;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain.Models;
using FMBot.LastFM.Domain.Enums;
using FMBot.LastFM.Domain.Types;
using FMBot.LastFM.Repositories;
using FMBot.Persistence.Domain.Models;
using Microsoft.Extensions.Options;
using Constants = FMBot.Domain.Constants;

namespace FMBot.Bot.TextCommands.LastFM;

[Name("Albums")]
public class AlbumCommands : BaseCommandModule
{
    private readonly CensorService _censorService;
    private readonly GuildService _guildService;
    private readonly IIndexService _indexService;
    private readonly IPrefixService _prefixService;
    private readonly IUpdateService _updateService;
    private readonly LastFmRepository _lastFmRepository;
    private readonly SpotifyService _spotifyService;
    private readonly PlayService _playService;
    private readonly SettingService _settingService;
    private readonly UserService _userService;
    private readonly TrackService _trackService;
    private readonly TimeService _timeService;
    private readonly FriendsService _friendsService;
    private readonly AlbumBuilders _albumBuilders;
    private readonly WhoKnowsAlbumService _whoKnowsAlbumService;
    private readonly WhoKnowsPlayService _whoKnowsPlayService;
    private readonly WhoKnowsService _whoKnowsService;
    private readonly TimerService _timer;
    private readonly AlbumService _albumService;

    private InteractiveService Interactivity { get; }

    public AlbumCommands(
        CensorService censorService,
        GuildService guildService,
        IIndexService indexService,
        IPrefixService prefixService,
        IUpdateService updateService,
        LastFmRepository lastFmRepository,
        PlayService playService,
        SettingService settingService,
        UserService userService,
        WhoKnowsAlbumService whoKnowsAlbumService,
        WhoKnowsPlayService whoKnowsPlayService,
        WhoKnowsService whoKnowsService,
        InteractiveService interactivity,
        TrackService trackService,
        SpotifyService spotifyService,
        IOptions<BotSettings> botSettings,
        FriendsService friendsService,
        TimerService timer,
        TimeService timeService, AlbumService albumService, AlbumBuilders albumBuilders) : base(botSettings)
    {
        this._censorService = censorService;
        this._guildService = guildService;
        this._indexService = indexService;
        this._lastFmRepository = lastFmRepository;
        this._playService = playService;
        this._prefixService = prefixService;
        this._settingService = settingService;
        this._updateService = updateService;
        this._userService = userService;
        this._whoKnowsAlbumService = whoKnowsAlbumService;
        this._whoKnowsPlayService = whoKnowsPlayService;
        this._whoKnowsService = whoKnowsService;
        this.Interactivity = interactivity;
        this._trackService = trackService;
        this._spotifyService = spotifyService;
        this._friendsService = friendsService;
        this._timer = timer;
        this._timeService = timeService;
        this._albumService = albumService;
        this._albumBuilders = albumBuilders;
    }

    [Command("album", RunMode = RunMode.Async)]
    [Summary("Shows album you're currently listening to or searching for.")]
    [Examples(
        "ab",
        "album",
        "album Ventura Anderson .Paak",
        "ab Boy Harsher | Yr Body Is Nothing")]
    [Alias("ab")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Albums)]
    public async Task AlbumAsync([Remainder] string albumValues = null)
    {
        try
        {
            _ = this.Context.Channel.TriggerTypingAsync();

            var contextUser = await this._userService.GetUserWithDiscogs(this.Context.User.Id);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

            var response = await this._albumBuilders.AlbumAsync(new ContextModel(this.Context, prfx, contextUser), albumValues);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("albumplays", RunMode = RunMode.Async)]
    [Summary("Shows playcount for current album or the one you're searching for.\n\n" +
             "You can also mention another user to see their playcount.")]
    [Examples(
        "abp",
        "albumplays",
        "albumplays @user",
        "albumplays lfm:fm-bot",
        "albumplays The Slow Rush",
        "abp The Beatles | Yesterday",
        "abp The Beatles | Yesterday @user")]
    [Alias("abp", "albumplay", "abplays", "albump", "album plays")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Albums)]
    public async Task AlbumPlaysAsync([Remainder] string albumValues = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(albumValues, contextUser, this.Context);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        var response = await this._albumBuilders.AlbumPlaysAsync(new ContextModel(this.Context, prfx, contextUser), userSettings, userSettings.NewSearchValue);

        await this.Context.SendResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [Command("cover", RunMode = RunMode.Async)]
    [Summary("Cover for current album or the one you're searching for.")]
    [Examples(
        "co",
        "cover",
        "cover la priest inji")]
    [Alias("abc", "abco", "co", "albumcover", "album cover")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Albums, CommandCategory.Charts)]
    public async Task AlbumCoverAsync([Remainder] string albumValues = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        try
        {
            var album = await this.SearchAlbum(albumValues, contextUser.UserNameLastFM, contextUser.SessionKeyLastFm,
                useCachedAlbums: true, userId: contextUser.UserId);
            if (album == null)
            {
                return;
            }

            var response = await this._albumBuilders.CoverAsync(new ContextModel(this.Context, prfx, contextUser), albumValues);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("topalbums", RunMode = RunMode.Async)]
    [Summary("Shows your or someone else's top albums over a certain time period.")]
    [Options(Constants.CompactTimePeriodList, Constants.UserMentionExample,
        Constants.BillboardExample, Constants.ExtraLargeExample)]
    [Examples("tab", "topalbums", "tab a lfm:fm-bot", "topalbums weekly @user", "tab bb xl")]
    [Alias("abl", "abs", "tab", "albumlist", "top albums", "albums", "albumslist")]
    [UsernameSetRequired]
    [SupportsPagination]
    [CommandCategories(CommandCategory.Albums)]
    public async Task TopAlbumsAsync([Remainder] string extraOptions = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var userSettings = await this._settingService.GetUser(extraOptions, contextUser, this.Context);
            var topListSettings = SettingService.SetTopListSettings(extraOptions);
            userSettings.RegisteredLastFm ??= await this._indexService.AddUserRegisteredLfmDate(userSettings.UserId);
            var timeSettings = SettingService.GetTimePeriod(extraOptions, registeredLastFm: userSettings.RegisteredLastFm);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

            var response = await this._albumBuilders.TopAlbumsAsync(new ContextModel(this.Context, prfx, contextUser),
                topListSettings, timeSettings, userSettings);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("whoknowsalbum", RunMode = RunMode.Async)]
    [Summary("Shows what other users listen to an album in your server")]
    [Alias("wa", "wka", "wkab", "wab", "wkab", "wk album", "whoknows album")]
    [Examples("wa", "whoknowsalbum", "whoknowsalbum the beatles abbey road", "whoknowsalbum Metallica & Lou Reed | Lulu")]
    [UsernameSetRequired]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Albums, CommandCategory.WhoKnows)]
    public async Task WhoKnowsAlbumAsync([Remainder] string albumValues = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var currentSettings = new WhoKnowsSettings
            {
                WhoKnowsMode = contextUser.Mode ?? WhoKnowsMode.Embed,
                NewSearchValue = albumValues
            };

            var settings = this._settingService.SetWhoKnowsSettings(currentSettings, albumValues, contextUser.UserType);

            var response = await this._albumBuilders.WhoKnowsAlbumAsync(new ContextModel(this.Context, prfx, contextUser), settings.WhoKnowsMode, settings.NewSearchValue);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("globalwhoknowsalbum", RunMode = RunMode.Async)]
    [Summary("Shows what other users listen to the an album on .fmbot")]
    [Examples("gwa", "globalwhoknowsalbum", "globalwhoknowsalbum the beatles abbey road", "globalwhoknowsalbum Metallica & Lou Reed | Lulu")]
    [Alias("gwa", "gwka", "gwab", "gwkab", "globalwka", "globalwkalbum", "globalwhoknows album")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Albums, CommandCategory.WhoKnows)]
    public async Task GlobalWhoKnowsAlbumAsync([Remainder] string albumValues = null)
    {
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        _ = this.Context.Channel.TriggerTypingAsync();
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        var currentSettings = new WhoKnowsSettings
        {
            WhoKnowsMode = contextUser.Mode ?? WhoKnowsMode.Embed,
            HidePrivateUsers = false,
            ShowBotters = false,
            AdminView = false,
            NewSearchValue = albumValues
        };

        var settings = this._settingService.SetWhoKnowsSettings(currentSettings, albumValues, contextUser.UserType);

        try
        {
            var response = await this._albumBuilders.GlobalWhoKnowsAlbumAsync(new ContextModel(this.Context, prfx, contextUser), settings, settings.NewSearchValue);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            if (!string.IsNullOrEmpty(e.Message) && e.Message.Contains("The server responded with error 50013: Missing Permissions"))
            {
                await this.Context.HandleCommandException(e, sendReply: false);
                await ReplyAsync("Error while replying: The bot is missing permissions.\n" +
                                 "Make sure it has permission to 'Embed links' and 'Attach Images'");
            }
            else
            {
                await this.Context.HandleCommandException(e);
            }
        }
    }

    [Command("friendwhoknowsalbum", RunMode = RunMode.Async)]
    [Summary("Shows who of your friends listen to an album")]
    [Examples("fwa", "fwka COMA", "friendwhoknows", "friendwhoknowsalbum the beatles abbey road", "friendwhoknowsalbum Metallica & Lou Reed | Lulu")]
    [Alias("fwa", "fwka", "fwkab", "fwab", "friendwhoknows album", "friends whoknows album", "friend whoknows album")]
    [UsernameSetRequired]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Albums, CommandCategory.WhoKnows, CommandCategory.Friends)]
    public async Task FriendWhoKnowsAlbumAsync([Remainder] string albumValues = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var contextUser = await this._userService.GetUserWithFriendsAsync(this.Context.User);

        try
        {
            var currentSettings = new WhoKnowsSettings
            {
                WhoKnowsMode = contextUser.Mode ?? WhoKnowsMode.Embed,
                NewSearchValue = albumValues
            };

            var settings = this._settingService.SetWhoKnowsSettings(currentSettings, albumValues, contextUser.UserType);

            var response = await this._albumBuilders
                .FriendsWhoKnowAlbumAsync(new ContextModel(this.Context, prfx, contextUser), currentSettings.WhoKnowsMode, settings.NewSearchValue);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            if (!string.IsNullOrEmpty(e.Message) && e.Message.Contains("The server responded with error 50013: Missing Permissions"))
            {
                await this.Context.HandleCommandException(e, sendReply: false);
                await ReplyAsync("Error while replying: The bot is missing permissions.\n" +
                                 "Make sure it has permission to 'Embed links' and 'Attach Images'");
            }
            else
            {
                await this.Context.HandleCommandException(e);
            }
        }
    }

    [Command("albumtracks", RunMode = RunMode.Async)]
    [Summary("Shows track playcounts for a specific album")]
    [Examples("abt", "albumtracks", "albumtracks de jeugd van tegenwoordig machine", "albumtracks U2 | The Joshua Tree")]
    [Alias("abt", "abtracks", "albumt")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Albums)]
    public async Task AlbumTracksAsync([Remainder] string albumValues = null)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        _ = this.Context.Channel.TriggerTypingAsync();

        try
        {
            var userSettings = await this._settingService.GetUser(albumValues, contextUser, this.Context);

            var album = await this.SearchAlbum(userSettings.NewSearchValue, contextUser.UserNameLastFM, contextUser.SessionKeyLastFm, userSettings.UserNameLastFm);
            if (album == null)
            {
                return;
            }

            var response = await this._albumBuilders.AlbumTracksAsync(new ContextModel(this.Context, prfx, contextUser),
                userSettings, userSettings.NewSearchValue);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("serveralbums", RunMode = RunMode.Async)]
    [Summary("Top albums for your server, optionally for a specific artist.")]
    [Options("Time periods: `weekly`, `monthly` and `alltime`", "Order options: `plays` and `listeners`", "Artist name")]
    [Examples("sab", "sab a p", "serveralbums", "serveralbums alltime", "serveralbums listeners weekly", "serveralbums the beatles monthly")]
    [Alias("sab", "stab", "servertopalbums", "serveralbum", "server albums")]
    [RequiresIndex]
    [GuildOnly]
    [CommandCategories(CommandCategory.Albums)]
    public async Task GuildAlbumsAsync([Remainder] string guildAlbumsOptions = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

        var guildListSettings = new GuildRankingSettings
        {
            ChartTimePeriod = TimePeriod.Weekly,
            TimeDescription = "weekly",
            OrderType = OrderType.Listeners,
            AmountOfDays = 7,
            NewSearchValue = guildAlbumsOptions
        };

        try
        {
            guildListSettings = SettingService.SetGuildRankingSettings(guildListSettings, guildAlbumsOptions);
            var timeSettings = SettingService.GetTimePeriod(guildListSettings.NewSearchValue, guildListSettings.ChartTimePeriod, cachedOrAllTimeOnly: true);

            if (timeSettings.UsePlays || timeSettings.TimePeriod is TimePeriod.AllTime or TimePeriod.Monthly or TimePeriod.Weekly)
            {
                guildListSettings = SettingService.TimeSettingsToGuildRankingSettings(guildListSettings, timeSettings);
            }

            var response =
                await this._albumBuilders.GuildAlbumsAsync(new ContextModel(this.Context, prfx), guild, guildListSettings);

            _ = this.Interactivity.SendPaginatorAsync(
                response.StaticPaginator,
                this.Context.Channel,
                TimeSpan.FromMinutes(DiscordConstants.PaginationTimeoutInSeconds));

            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    private async Task<AlbumInfo> SearchAlbum(string albumValues, string lastFmUserName, string sessionKey = null,
        string otherUserUsername = null, bool useCachedAlbums = false, int? userId = null)
    {
        string searchValue;
        if (!string.IsNullOrWhiteSpace(albumValues) && albumValues.Length != 0)
        {
            searchValue = albumValues;
            if (searchValue.ToLower() == "featured")
            {
                searchValue = $"{this._timer._currentFeatured.ArtistName} | {this._timer._currentFeatured.AlbumName}";
            }
            if (searchValue.Contains(" | "))
            {
                if (otherUserUsername != null)
                {
                    lastFmUserName = otherUserUsername;
                }

                var searchArtistName = searchValue.Split(" | ")[0];
                var searchAlbumName = searchValue.Split(" | ")[1];

                Response<AlbumInfo> albumInfo;
                if (useCachedAlbums)
                {
                    albumInfo = await GetCachedAlbum(searchArtistName, searchAlbumName, lastFmUserName, userId);
                }
                else
                {
                    albumInfo = await this._lastFmRepository.GetAlbumInfoAsync(searchArtistName, searchAlbumName,
                        lastFmUserName);
                }

                if (!albumInfo.Success && albumInfo.Error == ResponseStatus.MissingParameters)
                {
                    this._embed.WithDescription($"Album `{searchAlbumName}` by `{searchArtistName}`could not be found, please check your search values and try again.");
                    await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                    this.Context.LogCommandUsed(CommandResponse.NotFound);
                    return null;
                }
                if (!albumInfo.Success || albumInfo.Content == null)
                {
                    this._embed.ErrorResponse(albumInfo.Error, albumInfo.Message, this.Context.Message.Content, this.Context.User, "album");
                    await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                    this.Context.LogCommandUsed(CommandResponse.LastFmError);
                    return null;
                }
                return albumInfo.Content;
            }
        }
        else
        {
            var recentScrobbles = await this._lastFmRepository.GetRecentTracksAsync(lastFmUserName, 1, useCache: true, sessionKey: sessionKey);

            if (await GenericEmbedService.RecentScrobbleCallFailedReply(recentScrobbles, lastFmUserName, this.Context))
            {
                return null;
            }

            if (otherUserUsername != null)
            {
                lastFmUserName = otherUserUsername;
            }

            var lastPlayedTrack = recentScrobbles.Content.RecentTracks[0];

            if (string.IsNullOrWhiteSpace(lastPlayedTrack.AlbumName))
            {
                this._embed.WithDescription($"The track you're scrobbling (**{lastPlayedTrack.TrackName}** by **{lastPlayedTrack.ArtistName}**) does not have an album associated with it according to Last.fm.\n" +
                                            $"Please note that .fmbot is not associated with Last.fm.");

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return null;
            }

            Response<AlbumInfo> albumInfo;
            if (useCachedAlbums)
            {
                albumInfo = await GetCachedAlbum(lastPlayedTrack.ArtistName, lastPlayedTrack.AlbumName, lastFmUserName, userId);
            }
            else
            {
                albumInfo = await this._lastFmRepository.GetAlbumInfoAsync(lastPlayedTrack.ArtistName, lastPlayedTrack.AlbumName,
                    lastFmUserName);
            }

            if (albumInfo?.Content == null || !albumInfo.Success)
            {
                this._embed.WithDescription($"Last.fm did not return a result for **{lastPlayedTrack.AlbumName}** by **{lastPlayedTrack.ArtistName}**.\n" +
                                            $"This usually happens on recently released albums or on albums by smaller artists. Please try again later.\n\n" +
                                            $"Please note that .fmbot is not associated with Last.fm.");

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return null;
            }

            return albumInfo.Content;
        }

        var result = await this._lastFmRepository.SearchAlbumAsync(searchValue);
        if (result.Success && result.Content.Any())
        {
            var album = result.Content[0];

            if (otherUserUsername != null)
            {
                lastFmUserName = otherUserUsername;
            }

            Response<AlbumInfo> albumInfo;
            if (useCachedAlbums)
            {
                albumInfo = await GetCachedAlbum(album.ArtistName, album.Name, lastFmUserName, userId);
            }
            else
            {
                albumInfo = await this._lastFmRepository.GetAlbumInfoAsync(album.ArtistName, album.Name,
                    lastFmUserName);
            }

            return albumInfo.Content;
        }

        if (result.Success)
        {
            this._embed.WithDescription($"Album could not be found, please check your search values and try again.");
            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed(CommandResponse.NotFound);
            return null;
        }

        this._embed.WithDescription($"Last.fm returned an error: {result.Status}");
        await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
        this.Context.LogCommandUsed(CommandResponse.LastFmError);
        return null;
    }

    private async Task<Response<AlbumInfo>> GetCachedAlbum(string artistName, string albumName, string lastFmUserName, int? userId = null)
    {
        Response<AlbumInfo> albumInfo;
        var cachedAlbum = await this._albumService.GetAlbumFromDatabase(artistName, albumName);
        if (cachedAlbum != null)
        {
            albumInfo = new Response<AlbumInfo>
            {
                Content = this._albumService.CachedAlbumToAlbumInfo(cachedAlbum),
                Success = true
            };

            if (userId.HasValue)
            {
                var userPlaycount = await this._whoKnowsAlbumService.GetAlbumPlayCountForUser(cachedAlbum.ArtistName,
                        cachedAlbum.Name, userId.Value);
                albumInfo.Content.UserPlaycount = userPlaycount;
            }
        }
        else
        {
            albumInfo = await this._lastFmRepository.GetAlbumInfoAsync(artistName, albumName,
                lastFmUserName);
        }

        return albumInfo;
    }
}
