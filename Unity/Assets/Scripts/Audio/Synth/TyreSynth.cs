using System;

namespace CarRace.UnityGame.Audio
{
    /// <summary>
    /// Everything a car makes besides its engine, from what its wheels and body are doing:
    ///
    ///   Road: the roar of tyres on asphalt, low noise that rises in level and brightness
    ///   with speed.
    ///   Squeal: tyres past their grip, as two narrow resonances in noise that drift a little,
    ///   rising in pitch with how far past the limit they are. Driven by the tyre model's own
    ///   grip usage, so it starts exactly where grip runs out.
    ///   Off road: grass and sand, a deeper rumble and a crackle of stones and stalks.
    ///   Kerbs: a thump each time a wheel crosses a kerb block, so the rate follows speed.
    ///   Scrape: body against a barrier, bright grinding noise, as loud as the sliding is fast.
    ///   Wind: the air past the car, for the player's own car only, growing with the square
    ///   of speed.
    ///
    /// Set the fields from the game; call Next once per output sample.
    /// </summary>
    public sealed class TyreSynth
    {
        public float SpeedMs;
        /// <summary>0 to 1: how far past their grip the tyres are, summed over the four and scaled.</summary>
        public float Squeal;
        /// <summary>0 to 1: the share of wheels on grass or sand.</summary>
        public float OffRoad;
        /// <summary>0 to 1: the share of wheels on a kerb.</summary>
        public float Kerb;
        public float KerbBlockM = 2f;
        /// <summary>Metres a second the body slides along a barrier; 0 when not touching one.</summary>
        public float ScrapeMs;
        public float Wind;
        public float Volume = 1f;

        readonly float _sampleRate;
        Noise _noise;
        Smoothed _squeal, _offRoad, _kerb, _scrape, _speed;
        Biquad _road, _squealLow, _squealHigh, _rumble, _crackle, _thump, _grind, _grindLow, _wind;
        float _drift, _driftTarget, _kerbPhase, _brown;

        public TyreSynth(float sampleRate, uint seed = 7)
        {
            _sampleRate = sampleRate;
            _noise = new Noise(seed);
            _squeal = new Smoothed(0f, 0.05f, sampleRate);
            _offRoad = new Smoothed(0f, 0.08f, sampleRate);
            _kerb = new Smoothed(0f, 0.03f, sampleRate);
            _scrape = new Smoothed(0f, 0.04f, sampleRate);
            _speed = new Smoothed(0f, 0.05f, sampleRate);
            _road = Biquad.Make(Biquad.Kind.LowPass, 400f, 0.7f, sampleRate);
            _squealLow = Biquad.Make(Biquad.Kind.BandPass, 900f, 7f, sampleRate);
            _squealHigh = Biquad.Make(Biquad.Kind.BandPass, 1700f, 9f, sampleRate);
            _rumble = Biquad.Make(Biquad.Kind.LowPass, 160f, 0.9f, sampleRate);
            _crackle = Biquad.Make(Biquad.Kind.HighPass, 1500f, 0.7f, sampleRate);
            _thump = Biquad.Make(Biquad.Kind.LowPass, 90f, 2.5f, sampleRate);
            _grind = Biquad.Make(Biquad.Kind.BandPass, 2600f, 1.2f, sampleRate);
            _grindLow = Biquad.Make(Biquad.Kind.LowPass, 300f, 0.8f, sampleRate);
            _wind = Biquad.Make(Biquad.Kind.BandPass, 700f, 0.5f, sampleRate);
        }

        public float Next()
        {
            float dt = 1f / _sampleRate;
            float speed = _speed.Toward(SpeedMs);
            float squeal = _squeal.Toward(Math.Clamp(Squeal, 0f, 1f));
            float offRoad = _offRoad.Toward(Math.Clamp(OffRoad, 0f, 1f));
            float kerb = _kerb.Toward(Math.Clamp(Kerb, 0f, 1f));
            float scrape = _scrape.Toward(Math.Clamp(ScrapeMs / 15f, 0f, 1f));
            float white = _noise.Next();
            _brown = 0.98f * _brown + 0.02f * white;       // rolled off, for rumbles
            float pace = Math.Clamp(speed / 60f, 0f, 1.5f);

            // Road roar, brighter with speed; quieter where the wheels are off the asphalt.
            _road.Tune(250f + speed * 6f);
            float road = _road.Process(white) * 0.16f * MathF.Pow(pace, 1.3f) * (1f - 0.7f * offRoad);

            // Squeal: the resonances wander slowly, and rise with how hard the tyres slide.
            if (MathF.Abs(_drift - _driftTarget) < 0.002f) _driftTarget = (_noise.Next()) * 0.06f;
            _drift += (_driftTarget - _drift) * 3f * dt;
            float rise = 1f + 0.25f * squeal + _drift;
            _squealLow.Tune(900f * rise);
            _squealHigh.Tune(1700f * rise);
            float sq = (_squealLow.Process(white) + 0.6f * _squealHigh.Process(white)) * 2.2f
                     * squeal * MathF.Min(1f, speed / 4f);

            // Grass and sand: rumble plus a sparse crackle.
            float crackle = _noise.Unit() < 0.04f ? white : 0f;
            float dirt = (_rumble.Process(white) * 0.5f + _crackle.Process(crackle) * 0.35f) * offRoad * MathF.Min(1f, pace * 1.5f);

            // Kerbs: a thump every block.
            _kerbPhase += speed / KerbBlockM * dt;
            float kick = 0f;
            if (_kerbPhase >= 1f) { _kerbPhase -= 1f; kick = 1f; }
            float kerbs = _thump.Process(kick * 30f + _brown * 0.5f) * 0.5f * kerb;

            // Barrier scrape.
            float grind = (_grind.Process(white) + 0.6f * _grindLow.Process(white)) * 0.6f * scrape;

            float wind = _wind.Process(white) * 0.12f * Wind * pace * pace;

            return MathF.Tanh((road + sq + dirt + kerbs + grind + wind) * 1.2f) * Volume;
        }
    }
}
