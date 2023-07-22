using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Discord;
using FMBot.Bot.Models;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.Repositories;
using Microsoft.Extensions.Options;
using Npgsql;
using Serilog;

namespace FMBot.Bot.Services;

public class ImportService
{
    private readonly HttpClient _httpClient;
    private readonly TimeService _timeService;
    private readonly BotSettings _botSettings;

    public ImportService(HttpClient httpClient, TimeService timeService, IOptions<BotSettings> botSettings)
    {
        this._httpClient = httpClient;
        this._timeService = timeService;
        this._botSettings = botSettings.Value;
    }

    public async Task<(bool success, List<SpotifyEndSongImportModel> result)> HandleSpotifyFiles(IEnumerable<IAttachment> attachments)
    {
        try
        {
            var spotifyPlays = new List<SpotifyEndSongImportModel>();

            foreach (var attachment in attachments.Where(w => w?.Url != null).GroupBy(g => g.Filename))
            {
                await using var stream = await this._httpClient.GetStreamAsync(attachment.First().Url);

                var result = await JsonSerializer.DeserializeAsync<List<SpotifyEndSongImportModel>>(stream);

                spotifyPlays.AddRange(result);
            }

            return (true, spotifyPlays);
        }
        catch (Exception e)
        {
            Log.Error("Error while attempting to process Spotify import file", e);
            return (false, null);
        }
    }

    public async Task<List<UserPlay>> SpotifyImportToUserPlays(int userId, List<SpotifyEndSongImportModel> spotifyPlays)
    {
        var userPlays = new List<UserPlay>();

        var invalidPlays = 0;

        foreach (var spotifyPlay in spotifyPlays
                     .Where(w => w.MasterMetadataAlbumArtistName != null &&
                                 w.MasterMetadataTrackName != null))
        {
            var validScrobble = await this._timeService.IsValidScrobble(
                spotifyPlay.MasterMetadataAlbumArtistName, spotifyPlay.MasterMetadataTrackName, spotifyPlay.MsPlayed);

            if (validScrobble)
            {
                userPlays.Add(new UserPlay
                {
                    UserId = userId,
                    TimePlayed = DateTime.SpecifyKind(spotifyPlay.Ts, DateTimeKind.Utc),
                    ArtistName = spotifyPlay.MasterMetadataAlbumArtistName,
                    AlbumName = spotifyPlay.MasterMetadataAlbumAlbumName,
                    TrackName = spotifyPlay.MasterMetadataTrackName,
                    PlaySource = PlaySource.SpotifyImport,
                    MsPlayed = spotifyPlay.MsPlayed
                });
            }
            else
            {
                invalidPlays++;
            }
        }

        Log.Information("Importing: SpotifyImportToUserPlays found {validPlays} and {invalidPlays}", userPlays.Count, invalidPlays);

        return userPlays;
    }

    public async Task<List<UserPlay>> RemoveDuplicateSpotifyImports(int userId, IEnumerable<UserPlay> userPlays)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var existingPlays = await PlayRepository.GetUserPlays(userId, connection, 9999999);

        var timestamps = existingPlays
            .Where(w => w.PlaySource == PlaySource.SpotifyImport)
            .Select(s => s.TimePlayed)
            .ToHashSet();

        var playsToReturn = new List<UserPlay>();
        foreach (var userPlay in userPlays)
        {
            if (!timestamps.Contains(userPlay.TimePlayed))
            {
                playsToReturn.Add(userPlay);
                timestamps.Add(userPlay.TimePlayed);
            }
        }

        return playsToReturn;
    }

    public async Task UpdateExistingPlays(int userId)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        await PlayRepository.SetDefaultSourceForPlays(userId, connection);
        Log.Information("Importing: Set default source for {userId}", userId);
    }

    public async Task InsertImportPlays(IList<UserPlay> plays)
    {
        if (plays != null && plays.Any())
        {
            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            await PlayRepository.InsertTimeSeriesPlays(plays, connection);
            Log.Information("Importing: Inserted {importCount} plays for {userId}", plays.Count(), plays.First().UserId);
        }
        else
        {
            Log.Error("Importing: Tried to insert 0 import plays!");
        }
    }

    public async Task RemoveImportPlays(int userId)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        await PlayRepository.RemoveAllImportPlays(userId, connection);

        Log.Information("Importing: Removed imported plays for {userId}", userId);
    }

    public async Task<bool> HasImported(int userId)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        return await PlayRepository.HasImported(userId, connection);
    }
}
