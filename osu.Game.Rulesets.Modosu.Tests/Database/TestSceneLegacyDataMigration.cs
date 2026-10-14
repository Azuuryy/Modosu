using System;
using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Models;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Modosu.Configuration;
using osu.Game.Rulesets.Modosu.Database;
using osu.Game.Scoring;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.Modosu.Tests.Database
{
    public partial class TestSceneLegacyDataMigration : OsuTestScene
    {
        protected override bool UseFreshStoragePerRun => true;

        [Test]
        public void TestLocalDataSurvivesRename()
        {
            Guid scoreId = Guid.NewGuid();
            Guid officialScoreId = Guid.NewGuid();
            Guid presetId = Guid.NewGuid();
            const string profiles = "[{\"Name\":\"Preserved local profile\"}]";

            AddStep("seed old practice data", () => Realm.Write(realm =>
            {
                var legacy = realm.Add(new RulesetInfo { ShortName = "mosu", OnlineID = -1 });
                if (realm.Find<RulesetInfo>("modosu") == null)
                    realm.Add(new ModosuRuleset().RulesetInfo.Clone());
                realm.Add(new RealmRulesetSetting { RulesetName = "mosu", Key = "ProfilesJson", Value = profiles });
                realm.Add(new RealmRulesetSetting { RulesetName = "mosu", Key = "CollectionsImported", Value = "True" });
                realm.Add(new RealmRulesetSetting { RulesetName = "modosu", Key = "CollectionsImported", Value = "False" });
                var score = new ScoreInfo(new BeatmapInfo { Ruleset = legacy, Hash = "legacy-map" }, legacy) { ID = scoreId, Hash = "legacy-replay-hash", TotalScore = 123456 };
                score.RealmUser.Username = "Local practice user";
                score.Files.Add(new RealmNamedFileUsage(new RealmFile { Hash = "replay-file-hash" }, "replay.osr"));
                realm.Add(score);
                var official = realm.Find<RulesetInfo>("osu") ?? realm.Add(new RulesetInfo { ShortName = "osu", OnlineID = 0 });
                realm.Add(new ScoreInfo(new BeatmapInfo { Ruleset = official, Hash = "official-map" }, official) { ID = officialScoreId, Hash = "official-control" });
                realm.Add(new ModPreset { ID = presetId, Ruleset = legacy, Name = "Legacy RandomV2", ModsJson = "[{\"Acronym\":\"RDV2\"}]" });
            }));

            AddStep("load renamed configuration twice", () =>
            {
                using var config = new ModosuRulesetConfigManager(new SettingsStore(Realm), new ModosuRuleset().RulesetInfo);
                Assert.That(config.Get<string>(ModosuRulesetSetting.ProfilesJson), Is.EqualTo(profiles));
                Assert.That(config.Get<bool>(ModosuRulesetSetting.CollectionsImported), Is.False);
                LegacyPracticeDataMigration.Prepare(new SettingsStore(Realm), new ModosuRuleset().RulesetInfo);
            });
            AddAssert("score identity changed, replay and user preserved", () => Realm.Run(realm =>
            {
                var score = realm.Find<ScoreInfo>(scoreId)!;
                return score.Ruleset.ShortName == "modosu" && score.Ruleset.OnlineID == -1
                    && score.Hash == "legacy-replay-hash" && score.TotalScore == 123456 && score.BeatmapInfo!.Ruleset.ShortName == "modosu"
                    && score.RealmUser.Username == "Local practice user"
                    && score.Files.Single().Filename == "replay.osr" && score.Files.Single().File.Hash == "replay-file-hash";
            }));
            AddAssert("preset migrated and official score untouched", () => Realm.Run(realm =>
                realm.Find<ModPreset>(presetId)!.Ruleset.ShortName == "modosu"
                && realm.Find<ScoreInfo>(officialScoreId)!.Ruleset.ShortName == "osu"
                && realm.Find<ScoreInfo>(officialScoreId)!.Ruleset.OnlineID == 0));
            AddAssert("legacy settings retained without duplicate current keys", () => Realm.Run(realm =>
                realm.All<RealmRulesetSetting>().Count(s => s.RulesetName == "mosu") == 2
                && realm.All<RealmRulesetSetting>().Count(s => s.RulesetName == "modosu" && s.Key == "ProfilesJson") == 1));
            AddStep("reject an unexpected online legacy identity", () =>
            {
                Realm.Write(realm => realm.Find<RulesetInfo>("mosu")!.OnlineID = 0);
                Assert.Throws<InvalidOperationException>(() => LegacyPracticeDataMigration.Prepare(new SettingsStore(Realm), new ModosuRuleset().RulesetInfo));
            });
        }
    }
}
