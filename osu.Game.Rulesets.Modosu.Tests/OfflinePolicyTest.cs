using System;
using System.Linq;
using NUnit.Framework;
using osu.Game.Online.API;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Modosu.Offline;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Modosu.Mods;
using osu.Game.Rulesets.Osu.Beatmaps;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Scoring;
using osu.Game.Rulesets.Osu.Objects;
using osuTK;

namespace osu.Game.Rulesets.Modosu.Tests
{
    [TestFixture]
    public class OfflinePolicyTest
    {
        [Test]
        public void RulesetKeepsCustomIdentity()
        {
            var ruleset = new ModosuRuleset();
            Assert.That(ruleset.RulesetInfo.OnlineID, Is.EqualTo(-1));
            Assert.That(ruleset.ShortName, Is.EqualTo("modosu"));
        }

        [Test]
        public void EveryCustomModIsUnrankedAndExcludedFromMultiplayer()
        {
            var types = typeof(ModosuRuleset).Assembly.GetTypes().Where(t => !t.IsAbstract
                && typeof(Mod).IsAssignableFrom(t) && t.Namespace == "osu.Game.Rulesets.Modosu.Mods").ToArray();
            Assert.That(types, Is.Not.Empty);
            foreach (var type in types)
            {
                var mod = (Mod)Activator.CreateInstance(type)!;
                Assert.Multiple(() =>
                {
                    Assert.That(mod.Ranked, Is.False, type.Name);
                    Assert.That(mod.AlwaysValidForSubmission, Is.False, type.Name);
                    Assert.That(mod.ValidForMultiplayer, Is.False, type.Name);
                    Assert.That(mod.ValidForMultiplayerAsFreeMod, Is.False, type.Name);
                });
            }
        }

        [Test]
        public void RelaxAndAutopilotScoresRespectLocalPPPreference()
        {
            var relaxScore = new ScoreInfo { Mods = new Mod[] { new osu.Game.Rulesets.Modosu.Mods.OsuModRelax() } };
            var autopilotScore = new ScoreInfo { Mods = new Mod[] { new osu.Game.Rulesets.Modosu.Mods.OsuModAutopilot() } };
            var osuRelaxScore = new ScoreInfo { Mods = new Mod[] { new osu.Game.Rulesets.Osu.Mods.OsuModRelax() } };
            var osuAutopilotScore = new ScoreInfo { Mods = new Mod[] { new osu.Game.Rulesets.Osu.Mods.OsuModAutopilot() } };
            var normalScore = new ScoreInfo();

            Assert.Multiple(() =>
            {
                Assert.That(LocalUserManager.ShouldIncludeScoreInPP(relaxScore, false), Is.False);
                Assert.That(LocalUserManager.ShouldIncludeScoreInPP(autopilotScore, false), Is.False);
                Assert.That(LocalUserManager.ShouldIncludeScoreInPP(osuRelaxScore, false), Is.False);
                Assert.That(LocalUserManager.ShouldIncludeScoreInPP(osuAutopilotScore, false), Is.False);
                Assert.That(LocalUserManager.ShouldIncludeScoreInPP(normalScore, false), Is.True);
                Assert.That(LocalUserManager.ShouldIncludeScoreInPP(relaxScore, true), Is.True);
                Assert.That(LocalUserManager.ShouldIncludeScoreInPP(autopilotScore, true), Is.True);
                Assert.That(LocalUserManager.ShouldIncludeScoreInPP(osuRelaxScore, true), Is.True);
                Assert.That(LocalUserManager.ShouldIncludeScoreInPP(osuAutopilotScore, true), Is.True);
            });
        }

        [Test]
        public void GuardDisconnectsInitialAccountAndEveryReconnectState()
        {
            var api = new DummyAPIAccess();
            using var guard = new OfflineAccountGuard(api, action => action());
            Assert.That(api.State.Value, Is.EqualTo(APIState.Offline));
            foreach (var state in new[] { APIState.Connecting, APIState.RequiresSecondFactorAuth, APIState.Online, APIState.Failing })
            {
                api.SetState(state);
                Assert.That(api.State.Value, Is.EqualTo(APIState.Offline), state.ToString());
            }
        }

        [Test]
        public void GuardUnsubscribesWhenDisposed()
        {
            var api = new DummyAPIAccess();
            var guard = new OfflineAccountGuard(api, action => action());
            guard.Dispose();
            api.SetState(APIState.Online);
            Assert.That(api.State.Value, Is.EqualTo(APIState.Online));
        }

        private static OsuBeatmap createPracticeBeatmap()
        {
            var beatmap = new OsuBeatmap
            {
                BeatmapInfo = new BeatmapInfo { Difficulty = new BeatmapDifficulty() },
                ControlPointInfo = new ControlPointInfo(),
            };
            beatmap.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
            for (int i = 0; i < 100; i++)
                beatmap.HitObjects.Add(new HitCircle { StartTime = i * 125, Position = new Vector2(100 + i % 4 * 50, 150) });
            return beatmap;
        }

        [Test]
        public void RandomV2RemainsRepeatableWithoutAnApiProvider()
        {
            var beatmap = createPracticeBeatmap();
            var original = beatmap.HitObjects.Select(h => h.Position).ToArray();
            var mod = new OsuModRandomV2 { Seed = { Value = 12345 } };
            mod.ApplyToBeatmap(beatmap);
            var first = beatmap.HitObjects.Select(h => h.Position).ToArray();
            Assert.That(first, Is.Not.EqualTo(original));
            mod.ApplyToBeatmap(beatmap);
            Assert.That(beatmap.HitObjects.Select(h => h.Position), Is.EqualTo(first));
        }

        [Test]
        public void CircleGenerationCreatesPlayableLocalPatternsWithoutAnApiProvider()
        {
            var beatmap = createPracticeBeatmap();
            var original = beatmap.HitObjects.ToArray();
            new OsuModCircleGeneration { FullMap = { Value = false }, Count = { Value = 32 }, Distance = { Value = 30 } }.ApplyToBeatmap(beatmap);
            Assert.That(beatmap.HitObjects.Count, Is.GreaterThanOrEqualTo(32));
            Assert.That(beatmap.HitObjects.Any(original.Contains), Is.False);
            Assert.That(beatmap.HitObjects.Select(h => h.Position).Distinct().Count(), Is.GreaterThan(1));
            Assert.That(beatmap.HitObjects.Zip(beatmap.HitObjects.Skip(1), (a, b) => b.StartTime > a.StartTime).All(increasing => increasing), Is.True);
        }
    }
}
