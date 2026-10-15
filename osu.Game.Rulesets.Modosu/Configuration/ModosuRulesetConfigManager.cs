using osu.Game.Configuration;
using osu.Game.Rulesets.Configuration;
using osu.Game.Rulesets.Osu.Configuration;
using osu.Game.Rulesets.Modosu.Database;

namespace osu.Game.Rulesets.Modosu.Configuration
{
    public class ModosuRulesetConfigManager : RulesetConfigManager<ModosuRulesetSetting>
    {
        private readonly OsuRulesetConfigManager baseConfig;

        public ModosuRulesetConfigManager(SettingsStore? settings, RulesetInfo ruleset, int? variant = null)
            : base(LegacyPracticeDataMigration.Prepare(settings, ruleset)!, ruleset, variant)
        {
            baseConfig = new OsuRulesetConfigManager(settings, ruleset, variant);
        }

        protected override void InitialiseDefaults()
        {
            base.InitialiseDefaults();
            SetDefault(ModosuRulesetSetting.SuggestedSongsMinStars, 0.0, 0, 10, 0.1);
            SetDefault(ModosuRulesetSetting.SuggestedSongsMaxStars, 10.1, 0, 10.1, 0.1);
            SetDefault(ModosuRulesetSetting.ProfilesJson, "[]");
            SetDefault(ModosuRulesetSetting.PresetsImported, false);
            SetDefault(ModosuRulesetSetting.CollectionsImported, false);
            SetDefault(ModosuRulesetSetting.IncludeRelaxAutopilotPP, false);
        }
    }

    public enum ModosuRulesetSetting
    {
        SuggestedSongsMinStars,
        SuggestedSongsMaxStars,
        ProfilesJson,
        PresetsImported,
        CollectionsImported,
        IncludeRelaxAutopilotPP,
    }
}
