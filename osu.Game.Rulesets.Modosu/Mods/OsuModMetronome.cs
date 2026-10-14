using System.Linq;
using osu.Framework.Audio;
using osu.Framework.Audio.Track;
using osu.Framework.Bindables;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Configuration;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Beatmaps.Timing;
using osu.Game.Graphics;
using osu.Game.Graphics.UserInterface;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.Modosu.Mods
{
    public partial class OsuModMetronome : Mod, IApplicableToDrawableRuleset<OsuHitObject>
    {
        public override bool Ranked => false;
        public override bool AlwaysValidForSubmission => false;
        public override bool ValidForMultiplayer => false;
        public override bool ValidForMultiplayerAsFreeMod => false;

        public override string Name => "Metronome";

        public override LocalisableString Description =>
            "Plays metronome ticks at a configurable rate relative to the beatmap BPM, with a higher-pitched accent on the downbeat.";

        public override string Acronym => "ME";

        public override ModType Type => ModType.Fun;

        public override IconUsage? Icon => OsuIcon.Metronome;

        [SettingSource(
            "Tempo scale",
            "Scale the metronome rate from 0.25x to 4x relative to the beatmap BPM.",
            SettingControlType = typeof(MetronomeScaleSetting))]
        public BindableInt TempoScale { get; } = new BindableInt(4)
        {
            MinValue = 1,
            MaxValue = 16,
            Default = 4,
        };

        public void ApplyToDrawableRuleset(DrawableRuleset<OsuHitObject> drawableRuleset)
        {
            double firstHitTime = drawableRuleset.Beatmap.HitObjects.FirstOrDefault()?.StartTime ?? 0;
            drawableRuleset.Overlays.Add(new DividedMetronomeBeat(firstHitTime, TempoScale.Value));
        }

        public partial class MetronomeScaleSetting : SettingsSlider<int, MetronomeScaleSlider>
        {
        }

        public partial class MetronomeScaleSlider : RoundedSliderBar<int>
        {
            public override LocalisableString TooltipText => $"{Current.Value / 4.0:0.##}x";
        }

        private sealed partial class DividedMetronomeBeat : MetronomeBeat
        {
            private readonly int divisor;
            private const int beatsPerTick = 4;

            public DividedMetronomeBeat(double firstHitTime, int tempoScale)
                : base(firstHitTime)
            {
                // Each slider step is one quarter of a beat per tick.
                // For example, 12 subdivisions with one tick per 4 subdivisions gives 3x.
                divisor = tempoScale;
                Divisor = divisor;
            }

            protected override void OnNewBeat(
                int beatIndex,
                TimingControlPoint timingPoint,
                EffectControlPoint effectPoint,
                ChannelAmplitudes amplitudes)
            {
                if (beatIndex % beatsPerTick != 0)
                    return;

                if (divisor == 1)
                {
                    base.OnNewBeat(beatIndex, timingPoint, effectPoint, amplitudes);
                    return;
                }

                // BeatSyncedContainer counts each subdivision as a beat. Scale the time signature
                // so accents still correspond to beatmap downbeats.
                var scaledTimingPoint = new TimingControlPoint();
                scaledTimingPoint.CopyFrom(timingPoint);
                scaledTimingPoint.TimeSignature =
                    new TimeSignature(timingPoint.TimeSignature.Numerator * divisor);

                base.OnNewBeat(beatIndex, scaledTimingPoint, effectPoint, amplitudes);
            }
        }
    }
}