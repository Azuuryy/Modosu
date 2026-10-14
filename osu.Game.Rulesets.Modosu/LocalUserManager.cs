using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Rulesets.Modosu.Configuration;
using osu.Game.Rulesets.Modosu.Extensions;
using osu.Game.Rulesets.Modosu.Models;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Framework.Allocation;
using osu.Game.Database;
using osu.Game.Online;
using Realms;
using osu.Game.Users;

namespace osu.Game.Rulesets.Modosu
{
    [Cached]
    public partial class LocalUserManager : Component
    {
        private readonly RealmAccess realm;
        private readonly ModosuRulesetConfigManager config;
        private readonly IAPIProvider api;
        // NOTE all in one for now
        private readonly Dictionary<string, UserStatistics> statisticsCache = new Dictionary<string, UserStatistics>();

        private string? cacheKey(RulesetInfo ruleset)
        {
            if (ruleset == null)
                return null;
            return statisticsKey(ruleset, ActiveProfile.Value ?? "default");
        }

        private static string statisticsKey(RulesetInfo ruleset, string profileName)
            => $"{ruleset.ShortName}:{profileName}";
        private readonly ModosuRuleset ruleset;
        public RulesetInfo RulesetInfo => ruleset.RulesetInfo;

        private readonly Bindable<string> activeProfileBindable = new Bindable<string>();
        public Bindable<string> ActiveProfile => activeProfileBindable;

        public event Action<string>? ProfileChanged;
        public event Action? ProfilesChanged;

        public void SetActiveProfile(string name)
        {

            if (activeProfileBindable.Value == name) return;
            activeProfileBindable.Value = name;

            var profiles = getProfiles();
            foreach (var p in profiles)
                p.IsActive = p.Name == name;
            setProfiles(profiles);

            ProfileChanged?.Invoke(name);
            _ = RefreshStatisticsAsync(ruleset.RulesetInfo);
        }

        public List<LocalProfile> GetProfiles() => getProfiles();

        private List<LocalProfile> getProfiles()
        {
            var json = config.Get<string>(ModosuRulesetSetting.ProfilesJson);
            return json.FromJson<List<LocalProfile>>() ?? new List<LocalProfile>();
        }

        private void setProfiles(List<LocalProfile> profiles)
            => config.SetValue(ModosuRulesetSetting.ProfilesJson, profiles.ToJson());

        public void EnsureDefaultProfile()
        {
            var profiles = getProfiles();

            if (profiles.Count == 0 && api.LocalUser.Value?.Username is string username && !string.IsNullOrEmpty(username))
            {
                profiles.Add(new LocalProfile { Name = username, IsActive = true });
                setProfiles(profiles);
                SetActiveProfile(username);
            }
            else
            {
                var active = profiles.FirstOrDefault(p => p.IsActive);
                if (active != null)
                    SetActiveProfile(active.Name);
                else
                {
                    var first = profiles.FirstOrDefault();
                    if (first != null)
                    {
                        first.IsActive = true;
                        setProfiles(profiles);
                        SetActiveProfile(first.Name);
                    }
                }
            }

            // Initialize statistics after profiles are ensured
            if (statisticsInitialised == null || statisticsInitialised.IsCompleted)
                statisticsInitialised = InitialiseStatisticsAsync();
        }

        public void AddProfile(string name)
        {
            var profiles = getProfiles();
            if (profiles.Any(p => p.Name == name))
                return;
            profiles.Add(new LocalProfile { Name = name, IsActive = false });
            setProfiles(profiles);
            ProfilesChanged?.Invoke();
        }

        /// <summary>
        /// Whether a play should count towards play count, mirroring the main game's score submission
        /// gates: at least one successful hit (misses don't count) and a non-zero total score.
        /// Autoplay/cinema plays are never counted (not a real play).
        /// </summary>
        public static bool ShouldCountPlay(ScoreInfo score)
            => score.TotalScore > 0
               && score.Statistics.Any(s => s.Key.IsHit() && s.Value > 0)
               && score.Mods.All(m => m is not OsuModAutoplay && m is not OsuModCinema);

        /// <summary>
        /// Increments the persisted play count for a profile, mirroring the main game:
        /// a play is counted once its score is recorded (which only happens for passed plays locally).
        /// </summary>
        public void IncrementPlayCount(string profileName)
        {
            var profiles = getProfiles();
            var profile = profiles.FirstOrDefault(p => p.Name == profileName);
            if (profile == null)
                return;

            profile.PlayCount++;
            setProfiles(profiles);

            // keep the cached statistics fresh so the profile display updates immediately.
            var key = statisticsKey(ruleset.RulesetInfo, profileName);
            if (statisticsCache.TryGetValue(key, out var stats))
            {
                stats.PlayCount = profile.PlayCount;
                StatisticsUpdated?.Invoke(new UserStatisticsUpdate(ruleset.RulesetInfo, null, stats));
            }
        }

        public void RemoveProfile(string name)
        {
            string? fallbackName = null;
            var profiles = getProfiles();
            var profile = profiles.FirstOrDefault(p => p.Name == name);

            if (profile != null && profiles.Count > 1)
            {
                if (ActiveProfile.Value == name)
                    fallbackName = profiles.FirstOrDefault(p => p.Name != name)?.Name;

                profiles.Remove(profile);
                setProfiles(profiles);

                // Remove all scores under this profile for modosusu ruleset only
                realm.Write(rm =>
                {
                    rm.RemoveRange(rm.All<ScoreInfo>().Filter("RealmUser.Username == $0 && Ruleset.ShortName == $1", name, ruleset.RulesetInfo.ShortName));
                });
            }

            if (fallbackName != null)
                SetActiveProfile(fallbackName);
            ProfilesChanged?.Invoke();
        }

        /// <summary>
        /// Returns the <see cref="UserStatistics"/> currently available for the given ruleset.
        /// This may return null if the requested statistics has not been fetched before yet.
        /// </summary>
        /// <param name="ruleset">The ruleset to return the corresponding <see cref="UserStatistics"/> for.</param>
        public UserStatistics? GetStatisticsFor(RulesetInfo ruleset)
        {
            var key = cacheKey(ruleset);
            if (key == null)
                return null;
            return statisticsCache.GetValueOrDefault(key);
        }

        public UserStatistics? GetStatisticsForProfile(string profileName, RulesetInfo ruleset)
        {
            var key = statisticsKey(ruleset, profileName);
            return statisticsCache.GetValueOrDefault(key);
        }

        private Task? statisticsInitialised;

        private async Task InitialiseStatisticsAsync()
        {
            statisticsCache.Clear();

            if (api.LocalUser.Value == null)
                return;

            var profileNames = GetProfileNames();
            foreach (var name in profileNames)
            {
                var user = await GetLocalUserWithStatisticsForUsernameAsync(name, ruleset.RulesetInfo).ConfigureAwait(false);
                if (user.Statistics != null)
                {
                    var key = statisticsKey(ruleset.RulesetInfo, name);
                    statisticsCache[key] = user.Statistics;
                }
            }

            // Fire update for active profile so toolbar picks it up
            var activeKey = cacheKey(ruleset.RulesetInfo);
            if (activeKey != null && statisticsCache.TryGetValue(activeKey, out var activeStats))
            {
                var update = new UserStatisticsUpdate(ruleset.RulesetInfo, null, activeStats);
                StatisticsUpdated?.Invoke(update);
            }
        }

        public Task EnsureStatisticsLoadedAsync()
        {
            return statisticsInitialised ?? Task.CompletedTask;
        }

        public async Task UpdateUserStatisticsAsync(RulesetInfo ruleset, Action<UserStatisticsUpdate>? callback = null)
        {
            var user = await GetLocalUserWithStatisticsUncached(ruleset).ConfigureAwait(false);
            UpdateStatistics(user.Statistics, ruleset, callback);
        }

        public async Task RefreshStatisticsAsync(RulesetInfo ruleset)
        {
            await UpdateUserStatisticsAsync(ruleset).ConfigureAwait(false);
        }

        public List<string> GetProfileNames()
        {
            return getProfiles().Select(p => p.Name).OrderBy(n => n).ToList();
        }

        private void CacheStatistics(UserStatistics stats, string profileName, RulesetInfo ruleset)
        {
            var key = statisticsKey(ruleset, profileName);
            var oldStatistics = statisticsCache.GetValueOrDefault(key);
            statisticsCache[key] = stats;
            var update = new UserStatisticsUpdate(ruleset, oldStatistics, stats);
            StatisticsUpdated?.Invoke(update);
        }

        public event Action<UserStatisticsUpdate>? StatisticsUpdated;

        public void UpdateStatistics(UserStatistics newStatistics, RulesetInfo ruleset, Action<UserStatisticsUpdate>? callback = null)
        {
            var key = cacheKey(ruleset);
            if (key == null) return;
            var oldStatistics = statisticsCache.GetValueOrDefault(key);
            statisticsCache[key] = newStatistics;

            var update = new UserStatisticsUpdate(ruleset, oldStatistics, newStatistics);
            callback?.Invoke(update);
            StatisticsUpdated?.Invoke(update);
        }



        public LocalUserManager(ModosuRuleset ruleset, RealmAccess realm, ModosuRulesetConfigManager config, IAPIProvider api)
        {
            this.ruleset = ruleset;
            this.realm = realm;
            this.config = config;
            this.api = api;

            api.LocalUser.BindValueChanged(_ =>
            {
                // queuing up requests directly on user change is unsafe, as the API status may have not been updated yet.
                // schedule a frame to allow the API to be in its correct state sending requests.
                EnsureDefaultProfile();
            }, true);


        }

        public async Task<APIUser> GetLocalUserWithStatisticsAsync(RulesetInfo ruleset)
        {
            if (api.LocalUser.Value == null)
                return await GetLocalUserWithStatisticsUncached(ruleset).ConfigureAwait(false);

            var cacheK = cacheKey(ruleset);
            if (GetStatisticsFor(ruleset) is UserStatistics stats)
            {
                return new APIUser
                {
                    Id = api.LocalUser.Value.Id,
                    Username = ActiveProfile.Value ?? api.LocalUser.Value.Username,
                    CountryCode = api.LocalUser.Value.CountryCode,
                    CoverUrl = api.LocalUser.Value.CoverUrl,
                    Statistics = stats
                };
            }
            return await GetLocalUserWithStatisticsUncached(ruleset).ConfigureAwait(false);
        }

        public (float totalPp, float totalAccuracy) CalculateUserTotalPerformanceAggregates(IEnumerable<ScoreInfo> scores)
        {
            ScoreInfo[] groupedScores = scores.ToArray();

            // Build the diminishing sum
            double factor = 1;
            double totalPp = 0;
            double totalAccuracy = 0;

            foreach (var score in groupedScores)
            {
                totalPp += score.PP!.Value * factor;
                totalAccuracy += score.Accuracy * factor;
                factor *= 0.95;
            }

            // Legacy compatibility factor
            totalPp += (417.0 - 1.0 / 3.0) * (1.0 - Math.Pow(0.995, groupedScores.Length));

            // Normalize accuracy
            if (groupedScores.Length > 0)
            {
                totalAccuracy *= 100.0 / (20 * (1 - Math.Pow(0.95, groupedScores.Length)));
            }

            if (double.IsNegative(totalPp) || double.IsNaN(totalPp) || double.IsInfinity(totalPp))
                throw new InvalidOperationException($"Calculating total PP resulted in invalid value ({totalPp})");

            if (double.IsNaN(totalAccuracy) || double.IsInfinity(totalAccuracy))
                throw new InvalidOperationException($"Calculating total accuracy resulted in invalid value ({totalAccuracy})");

            totalAccuracy = Math.Clamp(totalAccuracy, 0, 100);

            return ((float)totalPp, (float)totalAccuracy);
        }

        public async Task<APIUser> GetLocalUserWithStatisticsUncached(RulesetInfo ruleset)
        {
            var username = ActiveProfile.Value ?? api.LocalUser.Value?.Username ?? "unknown";
            return await GetLocalUserWithStatisticsForUsernameAsync(username, ruleset).ConfigureAwait(false);
        }

        public async Task<APIUser> GetLocalUserWithStatisticsForUsernameAsync(string username, RulesetInfo ruleset)
        {
            return await Task.Run(() =>
            {
                var allScores = GetBestScores(username, ruleset);
                var recordedScores = GetAllScores(username, ruleset);

                // Play count is persisted per profile in the profile JSON, incremented when a score is recorded.
                int playCount = getProfiles().FirstOrDefault(p => p.Name == username)?.PlayCount ?? 0;


                var scoresWithPP = allScores.Where(s => s.PP.HasValue).ToList();

                // --- Rank History Calculation (Last 90 Days) ---
                var rankHistoryData = new int[90];
                var today = DateTimeOffset.UtcNow.Date;
                var firstHistoryDate = today.AddDays(-89);
                bool hasScoresBeforeHistory = scoresWithPP.Any(s => s.Date.Date < firstHistoryDate);

                for (int i = 0; i < 90; i++)
                {
                    var historyDate = today.AddDays(-89 + i);

                    // Note: This logic assumes 'allScores' is already sorted by PP Desc (which the extensions do).
                    // However, for history, we strictly need to filter by date first, then recalculate "Best" for that specific day.
                    // The current logic approximates this by taking the *current* best list and filtering by date.
                    var scoresUpToHistoryDate = scoresWithPP.Where(s => s.Date.Date <= historyDate);

                    (var pp, var acc) = CalculateUserTotalPerformanceAggregates(scoresUpToHistoryDate);
                    // If there is no PP history before the graph window, start at zero.
                    // This gives the graph a clear baseline before the first local play.
                    rankHistoryData[i] = i == 0 && !hasScoresBeforeHistory ? 0 : (pp > 0 ? (int)pp : 0);
                }

                (float currentTotalPP, float accuracy) = CalculateUserTotalPerformanceAggregates(allScores);


                var localUser = api.LocalUser.Value;
                return new APIUser
                {
                    Id = localUser?.Id ?? 0,
                    Username = username,
                    CountryCode = localUser != null ? localUser.CountryCode : default,
                    CoverUrl = localUser?.CoverUrl ?? "",
                    Statistics = new UserStatistics
                    {
                        IsRanked = false,
                        PP = (decimal)currentTotalPP,
                        Accuracy = accuracy,
                        GlobalRank = currentTotalPP > 0 ? estimateGlobalRank(currentTotalPP) : null,
                        RankHistory = new APIRankHistory
                        {
                            Data = rankHistoryData,
                            Mode = ruleset.ShortName
                        },
                        PlayCount = playCount,
                        RankedScore = recordedScores.Sum(s => s.TotalScore),
                        TotalScore = recordedScores.Sum(s => s.TotalScore),
                        TotalHits = recordedScores.Sum(s => s.Statistics.Where(h => h.Key.IsHit()).Sum(h => h.Value)),
                        PlayTime = (int)recordedScores.Sum(s => s.BeatmapInfo?.Length ?? 0),
                        MaxCombo = recordedScores.Any() ? recordedScores.Max(s => s.MaxCombo) : 0,
                        GradesCount = new UserStatistics.Grades
                        {
                            SSPlus = allScores.Count(s => s.Rank == ScoreRank.XH),
                            SS = allScores.Count(s => s.Rank == ScoreRank.X),
                            SPlus = allScores.Count(s => s.Rank == ScoreRank.SH),
                            S = allScores.Count(s => s.Rank == ScoreRank.S),
                            A = allScores.Count(s => s.Rank == ScoreRank.A)
                        },
                    }
                };
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets best performance scores for a specific user (Top Ranks).
        /// Replaces: ByUsername
        /// </summary>
        public List<ScoreInfo> GetBestScores(string username, RulesetInfo ruleset)
            => queryScores(ruleset, username);

        /// <summary>Gets every locally recorded score for career totals (including scores without PP).</summary>
        private List<ScoreInfo> GetAllScores(string username, RulesetInfo ruleset)
            => realm.Run(r => r.All<ScoreInfo>()
                              .Filter("RealmUser.Username == $0 && DeletePending == false && Ruleset.ShortName == $1 SORT(Date DESC)", username, ruleset.ShortName)
                              .ToList().Detach());

        // Approximation of the September 2026 sampled osu! PP-to-global-rank curve from
        // osu-local-profiles (10,000 random profiles). Points are (PP, estimated rank).
        // The rank is a local estimate, not an official leaderboard position.
        private static readonly (double pp, int rank)[] rankCurve =
        {
            (2.11, 2852394), (45.65, 2142296), (139.97, 1750230), (375.42, 1336787),
            (721.22, 1020639), (1148.03, 779960), (1621.4, 595532), (2125.51, 454565),
            (2708.02, 335739), (3302.75, 247883), (3978.95, 177279), (4665.01, 126882),
            (5403.43, 87234), (6164.53, 58191), (6911.02, 39304), (7677.87, 26024),
            (8519.1, 16828), (9518.04, 9896), (10741.3, 4951), (11762, 2910),
            (13010.5, 1549), (15002.6, 553), (17051.7, 211), (18305.4, 120),
        };

        private static int estimateGlobalRank(double pp)
        {
            if (pp <= rankCurve[0].pp)
                return rankCurve[0].rank;
            if (pp >= rankCurve[^1].pp)
                return rankCurve[^1].rank;

            for (int i = 1; i < rankCurve.Length; i++)
            {
                if (pp <= rankCurve[i].pp)
                {
                    var previous = rankCurve[i - 1];
                    var next = rankCurve[i];
                    double progress = (pp - previous.pp) / (next.pp - previous.pp);
                    return (int)Math.Round(previous.rank + (next.rank - previous.rank) * progress);
                }
            }

            return rankCurve[^1].rank;
        }

        /// <summary>
        /// Gets all scores that have a replay file available (Local scores), sorted by PP.
        /// Replaces: All
        /// </summary>
        public List<ScoreInfo> GetLocalScores(RulesetInfo ruleset)
            => queryScores(ruleset);

        /// <summary>
        /// Gets recent local scores (with replay files).
        /// Replaces: Recent(ruleset)
        /// </summary>
        public List<ScoreInfo> GetRecentScores(RulesetInfo ruleset)
            => queryScores(ruleset, recent: true);

        /// <summary>
        /// Gets recent scores for a specific user.
        /// Replaces: Recent(ruleset, username)
        /// </summary>
        public List<ScoreInfo> GetRecentScores(string username, RulesetInfo ruleset)
            => queryScores(ruleset, username, recent: true);

        /// <summary>
        /// The single realm score query behind all four public accessors — the filter shape is decided
        /// here once: an optional username predicate, then the standard "playable local score" prefix,
        /// then a sort/limit suffix.
        /// </summary>
        private List<ScoreInfo> queryScores(RulesetInfo ruleset, string? username = null, bool recent = false)
        {
            string sortSuffix = recent
                ? " SORT(Date DESC) LIMIT(50)"
                : " SORT(PP DESC) DISTINCT(BeatmapInfo.ID,BeatmapInfo.DifficultyName)";

            if (username != null)
            {
                string filter = "RealmUser.Username == $0 && PP != null && DeletePending == false && RankInt != -1 && Ruleset.ShortName == $1" + sortSuffix;
                return realm.Run(r => r.All<ScoreInfo>().Filter(filter, username, ruleset.ShortName).ToList().Detach());
            }

            string noUserFilter = "PP != null && DeletePending == false && RankInt != -1 && Ruleset.ShortName == $0" + sortSuffix;
            return realm.Run(r => r.All<ScoreInfo>().Filter(noUserFilter, ruleset.ShortName).ToList().Detach());
        }


    }
}
