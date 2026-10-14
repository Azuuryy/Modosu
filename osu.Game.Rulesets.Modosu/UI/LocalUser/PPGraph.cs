using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Bindables;
using Humanizer;
using osu.Framework.Extensions.LocalisationExtensions;
using osu.Game.Users;
using osu.Game.Overlays.Profile;

namespace osu.Game.Rulesets.Modosu.UI.LocalUser
{
    public partial class PPGraph : UserGraph<int, int>
    {
        public readonly Bindable<UserStatistics?> Statistics = new Bindable<UserStatistics?>();

        protected override float GetDataPointHeight(int rank) => MathF.Pow(rank, 4);
        private const int pp_days = 88;

        protected override void LoadComplete()
        {
            base.LoadComplete();
            Statistics.BindValueChanged(e => updateStatistics(e.NewValue), true);
        }

        private void updateStatistics(UserStatistics? statistics)
        {
            // This is local PP history, so IsRanked is intentionally false for Modosu profiles.
            int[]? history = statistics?.RankHistory?.Data;

            if (history == null || history.Length == 0 || statistics?.PP == null)
            {
                Data = null;
                return;
            }

            var points = history.Select((pp, index) => new KeyValuePair<int, int>(index, pp)).ToArray();

            // Keep zero-value days so a profile with only one recent PP update still has a
            // visible history. Ensure the final sample matches the current local PP total.
            points[^1] = new KeyValuePair<int, int>(points[^1].Key, Math.Max(points[^1].Value, (int)statistics.PP.Value));
            Data = points.Any(point => point.Value > 0) ? points : null;
        }

        protected override UserGraphTooltipContent GetTooltipContent(int index, int rank)
        {
            int days = pp_days - index + 1;

            return new UserGraphTooltipContent
            {
                Name = @"PP",
                Count = rank.ToLocalisableString("#,##0"),
                Time = days == 0 ? "now" : $"{"day".ToQuantity(days)} ago",
            };
        }
    }
}
